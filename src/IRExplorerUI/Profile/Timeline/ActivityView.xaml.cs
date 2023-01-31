using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using IRExplorerCore;
using IRExplorerCore.Utilities;
using IRExplorerUI.Utilities;
using static IRExplorerUI.NativeMethods;

namespace IRExplorerUI.Profile;

public record SampleTimeRangeInfo(TimeSpan StartTime, TimeSpan EndTime,
                                  int StartSampleIndex, int EndSampleIndex,
                                  int ThreadId);

public class ProfileSampleFilter {
    public SampleTimeRangeInfo TimeRange { get; set; }
    public List<int> ThreadIds { get; set; }

    public override string ToString() {
        return $"TimeRange: {TimeRange}, ThreadIds: {ThreadIds}";
    }
}

public record SampleTimePointInfo(TimeSpan Time, int SampleIndex, int ThreadId);

//? TODO: Use SampleIndex in SampleTimePointInfo/Range
public record SampleIndex(int Index, TimeSpan Time);

public partial class ActivityView : FrameworkElement, INotifyPropertyChanged {
    struct Slice {
        public TimeSpan Weight;
        public int FirstSampleIndex;
        public int SampleCount;

        public Slice(TimeSpan weight, int firstSampleIndex, int sampleCount) {
            Weight = weight;
            FirstSampleIndex = firstSampleIndex;
            SampleCount = sampleCount;
        }
    }

    class SliceList {
        public int ThreadId { get; set; }
        public TimeSpan TotalWeight { get; set; }
        public TimeSpan MaxWeight { get; set; }
        public TimeSpan TimePerSlice { get; set; }
        public List<Slice> Slices { get; set; }
        public int MaxSlices { get; set; }

        public SliceList(int threadId) {
            ThreadId = threadId;
            Slices = new List<Slice>();
            MaxWeight = TimeSpan.MinValue;
        }
    }

    const double SliceWidth = 8;
    const double TimeBarHeight = 18;
    const double MinSampleHeight = 4;
    const double TopMarginY = 1 + TimeBarHeight;
    const double BottomMarginY = 0;
    const string DefaultFont = "Segoe UI";
    const double DefaultTextSize = 11;
    const double MarkerHeight = 8;

    private bool initialized_;
    private DrawingVisual visual_;
    private GlyphRunCache glyphs_;
    private ProfileData profile_;
    private TimeSpan samplingInterval_;
    private double sliceWidth_;
    private double maxSampleHeight_;
    private double maxWidth_;
    private double maxHeight_;
    private double prevMaxWidth_;
    private double topMargin_;
    private double bottomMargin_;
    private Rect visibleArea_;
    private List<SliceList> slices_;
    private bool showPositionLine_;
    private double positionLineX_;
    private bool startedSelection_;
    private bool hasSelection_;
    private TimeSpan selectionStartTime_;
    private TimeSpan selectionEndTime_;
    private TimeSpan SelectionTimeDiff => selectionEndTime_ - selectionStartTime_;
    private TimeSpan filterStartTime_;
    private TimeSpan filterEndTime_;
    private bool hasFilter_;
    private Typeface font_;
    private double fontSize_;
    private Brush backColor_;
    private TimeSpan startTime_;
    private TimeSpan endTime_;
    private List<SampleIndex> markedSamples_;
    private CancelableTaskInstance sliceTask_;

    public ActivityView() {
        InitializeComponent();
        filteredOutColor_ = ColorBrushes.GetTransparentBrush(Colors.WhiteSmoke, 190);
        selectionBackColor_ = ColorBrushes.GetTransparentBrush("#A7D5F5", 150); // SelectedBackgroundBrush
        filteredBackColor_ = ColorBrushes.GetBrush(Colors.Linen);
        selectionBorderColor_ = ColorPens.GetPen(Colors.Black);
        markerBackColor_ = ColorBrushes.GetTransparentBrush("#0F92EF", 180);;
        positionLinePen_ = ColorPens.GetBoldPen(Colors.DarkBlue);
        filteredOutBorderColor_ = ColorPens.GetBoldPen(Colors.Black);
        ThreadId = -1;
        DataContext = this;

        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseLeftButtonUp += ActivityView_MouseLeftButtonUp;
        MouseMove += ActivityView_MouseMove;
        MouseLeave += ActivityView_MouseLeave;
        PreviewMouseWheel += ActivityView_PreviewMouseWheel;
    }

    public RelayCommand<object> FilterTimeRangeCommand =>
        new((obj) => ApplyTimeRangeFilter());

    public RelayCommand<object> ClearSelectionCommand =>
        new((obj) => ClearSelectedTimeRange());

    public RelayCommand<object> RemoveTimeRangeFilterCommand =>
        new((obj) => RemoveTimeRangeFilter());

    public event EventHandler<SampleTimeRangeInfo> SelectingTimeRange;
    public event EventHandler<SampleTimeRangeInfo> SelectedTimeRange;
    public event EventHandler<SampleTimeRangeInfo> FilteredTimeRange;
    public event EventHandler<bool> ThreadIncludedChanged;
    public event EventHandler ClearedSelectedTimeRange;
    public event EventHandler ClearedFilteredTimeRange;

    public event EventHandler<SampleTimePointInfo> HoveringTimePoint;
    public event EventHandler<SampleTimePointInfo> SelectedTimePoint;
    public event EventHandler ClearedTimePoint;

    public int ThreadId { get; private set; }
    public string ThreadName { get; set; }
    public TimeSpan ThreadWeight => slices_ != null ? slices_[0].TotalWeight : TimeSpan.Zero;
    public bool IsSingleThreadView => ThreadId != -1;

    public bool HasSelection => hasSelection_;
    public TimeSpan SelectionStartTime => selectionStartTime_;
    public TimeSpan SelectionTime => selectionEndTime_ - selectionStartTime_;

    public bool HasFilter => hasFilter_;
    public TimeSpan FilteredTime => filterEndTime_ - filterStartTime_;
    public SampleTimeRangeInfo FilteredRange => GetFilteredTimeRange();
    public SampleTimePointInfo CurrentTimePoint => GetCurrentTimePoint();
    public double MaxViewWidth => maxWidth_;
    public bool PreviousIsThreadIncluded { get; set; }

    private bool isThreadIncluded_;
    public bool IsThreadIncluded {
        get => isThreadIncluded_;
        set {
            if (value != isThreadIncluded_) {
                PreviousIsThreadIncluded = isThreadIncluded_;
                SetField(ref isThreadIncluded_, value);
                ThreadIncludedChanged?.Invoke(this, value);
            }
        }
    }

    public int MaxCpuUsage {
        get {
            if (slices_ == null) {
                return 0;
            }

            double maxValue = double.MinValue;

            foreach (var slice in slices_[0].Slices) {
                double sliceCpuUsage = EstimateCpuUsage(slice, slices_[0].TimePerSlice, samplingInterval_);
                maxValue = Math.Max(sliceCpuUsage, maxValue);
            }

            return (int)Math.Ceiling(maxValue);
        }
    }

    public Brush BackColor {
        get => backColor_;
        set {
            SetField(ref backColor_, value);
            Redraw();
        }
    }

    private Brush timeBarBackColor_;
    public Brush TimeBarBackColor {
        get => timeBarBackColor_;
        set {
            SetField(ref timeBarBackColor_, value);
            Redraw();
        }
    }

    private Brush filteredBackColor_;
    public Brush FilteredBackColor {
        get => filteredBackColor_;
        set {
            SetField(ref filteredBackColor_, value);
            Redraw();
        }
    }

    private Brush filteredOutColor_;
    public Brush FilteredOutColor {
        get => filteredOutColor_;
        set {
            SetField(ref filteredOutColor_, value);
            Redraw();
        }
    }

    private Pen filteredOutBorderColor_;
    public Pen FilteredOutBorderColor {
        get => filteredOutBorderColor_;
        set {
            SetField(ref filteredOutBorderColor_, value);
            Redraw();
        }
    }

    private Brush markerBackColor_;
    public Brush MarkerBackColor {
        get => markerBackColor_;
        set {
            SetField(ref markerBackColor_, value);
            Redraw();
        }
    }

    private bool isTimeBarVisible_;
    public bool IsTimeBarVisible {
        get => isTimeBarVisible_;
        set {
            SetField(ref isTimeBarVisible_, value);
            Redraw();
        }
    }

    private Brush sampleBackColor_;
    public Brush SamplesBackColor {
        get => sampleBackColor_;
        set {
            SetField(ref sampleBackColor_, value);
            Redraw();
        }
    }

    private Pen sampleBorderColor_;
    public Pen SampleBorderColor {
        get => sampleBorderColor_;
        set {
            SetField(ref sampleBorderColor_, value);
            Redraw();
        }
    }

    private Brush selectionBackColor_;
    public Brush SelectionBackColor {
        get => selectionBackColor_;
        set {
            SetField(ref selectionBackColor_, value);
            Redraw();
        }
    }

    private Pen selectionBorderColor_;
    public Pen SelectionBorderColor {
        get => selectionBorderColor_;
        set {
            SetField(ref selectionBorderColor_, value);
            Redraw();
        }
    }

    private Pen positionLinePen_;
    public Pen PositionLinePen {
        get => positionLinePen_;
        set {
            SetField(ref positionLinePen_, value);
            Redraw();
        }
    }

    private void UpdateSizes() {
        topMargin_ = IsTimeBarVisible ? TopMarginY : 2;
        bottomMargin_ = BottomMarginY;
        maxSampleHeight_ = visibleArea_.Height - topMargin_;
        maxHeight_ = Math.Max(visibleArea_.Height, TimeBarHeight);
    }

    public void SelectTimeRange(SampleTimeRangeInfo range) {
        selectionStartTime_ = range.StartTime - startTime_;
        selectionEndTime_ = range.EndTime - startTime_;
        hasSelection_ = true;
        startedSelection_ = false;
        UpdateSelectionState();
    }

    private void UpdateSelectionState() {
        Redraw();
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectionTime));
    }

    public void ClearSelectedTimeRange() {
        if (hasSelection_) {
            hasSelection_ = false;
            startedSelection_ = false;
            UpdateSelectionState();
        }
    }

    private SampleTimePointInfo GetSelectedTimePoint() {
        return new SampleTimePointInfo(selectionStartTime_ + startTime_,
                                       TimeToSampleIndex(selectionStartTime_), ThreadId);
    }

    private SampleTimePointInfo GetCurrentTimePoint() {
        var time = PositionToTime(positionLineX_);
        return new SampleTimePointInfo(time + startTime_,
                                       TimeToSampleIndex(time), ThreadId);
    }

    private SampleTimeRangeInfo GetSelectedTimeRange() {
        return new SampleTimeRangeInfo(selectionStartTime_ + startTime_, selectionEndTime_ + startTime_,
                                       TimeToSampleIndex(selectionStartTime_, SelectionTimeDiff),
                                       TimeToSampleIndexBack(selectionEndTime_, SelectionTimeDiff), ThreadId);
    }

    private SampleTimeRangeInfo GetFilteredTimeRange() {
        return new SampleTimeRangeInfo(filterStartTime_ + startTime_, filterEndTime_ + startTime_,
            TimeToSampleIndex(selectionStartTime_, SelectionTimeDiff),
            TimeToSampleIndexBack(selectionEndTime_, SelectionTimeDiff), ThreadId);
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        var time = PositionToTime(e.GetPosition(this).X);

        if (hasSelection_) {

            if (time >= selectionStartTime_ && time <= selectionEndTime_) {
                if (e.ClickCount > 1) {
                    ApplyTimeRangeFilter();
                }

                return;
            }

            hasSelection_ = false;
            UpdateSelectionState();
            ClearedSelectedTimeRange?.Invoke(this, EventArgs.Empty);
        }

        if (hasFilter_) {
            if (time < filterStartTime_ || time > filterEndTime_) {
                if (e.ClickCount > 1) {
                    RemoveTimeRangeFilter();
                }
            }
        }

        ClearedTimePoint?.Invoke(this, EventArgs.Empty);

        startedSelection_ = true;
        selectionStartTime_ = time;
        selectionEndTime_ = selectionStartTime_;
        e.Handled = true;
        CaptureMouse();
        Redraw();
    }

    private void ActivityView_PreviewMouseWheel(object sender, MouseWheelEventArgs e) {
        // Cancel any selection action.
        startedSelection_ = false;
        ReleaseMouseCapture();
    }

    private void RemoveTimeRangeFilter() {
        ClearTimeRangeFilter();
        ClearedFilteredTimeRange?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyTimeRangeFilter() {
        var range = GetSelectedTimeRange();
        FilterTimeRange(range);
        FilteredTimeRange?.Invoke(this, range);
    }

    private void ActivityView_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) {
        if (startedSelection_) {
            startedSelection_ = false;
            selectionEndTime_ = PositionToTime(e.GetPosition(this).X);

            // If selection is done moving to the left, end time becomes start time.
            if (selectionEndTime_ < selectionStartTime_) {
                (selectionStartTime_, selectionEndTime_) = (selectionEndTime_, selectionStartTime_);
            }

            hasSelection_ = SelectionTimeDiff.Ticks > 0;
            showPositionLine_ = false;
            ReleaseMouseCapture();
            UpdateSelectionState();

            if (hasSelection_) {
                SelectedTimeRange?.Invoke(this, GetSelectedTimeRange());
            }
        }
        else {
            SelectedTimePoint?.Invoke(this, GetSelectedTimePoint());
        }

        e.Handled = true;
    }

    private void ActivityView_MouseLeave(object sender, MouseEventArgs e) {
        if (showPositionLine_) {
            showPositionLine_ = false;
            Redraw();
        }
    }

    private void ActivityView_MouseMove(object sender, MouseEventArgs e) {
        if (startedSelection_) {
            selectionEndTime_ = PositionToTime(e.GetPosition(this).X);
            Redraw();

            SelectingTimeRange?.Invoke(this, GetSelectedTimeRange());
        }
        else {
            showPositionLine_ = true;
            positionLineX_ = e.GetPosition(this).X;
            Redraw();

            HoveringTimePoint?.Invoke(this, GetCurrentTimePoint());
        }

    }

    public async Task Initialize(ProfileData profile, Rect visibleArea, int threadId = -1) {
        if (initialized_) {
            return;
        }

        initialized_ = true;
        isThreadIncluded_ = true;
        PreviousIsThreadIncluded = true;
        profile_ = profile;
        visibleArea_ = visibleArea;
        ThreadId = threadId;

        var thread = profile.FindThread(threadId);
        if (thread != null) {
            ThreadName = thread.Name;
        }

        samplingInterval_ = profile_.Report.SamplingInterval;
        maxWidth_ = prevMaxWidth_ = visibleArea.Width;
        visual_ = new DrawingVisual();
        visual_.Drawing?.Freeze();
        AddVisualChild(visual_);
        AddLogicalChild(visual_);

        font_ = new Typeface(DefaultFont);
        fontSize_ = DefaultTextSize;
        glyphs_ = new GlyphRunCache(font_, fontSize_, VisualTreeHelper.GetDpi(visual_).PixelsPerDip);

        StartComputeSampleSlices(SliceWidth);
        
        OnPropertyChanged(nameof(ThreadWeight));
        OnPropertyChanged(nameof(ThreadId));
        OnPropertyChanged(nameof(ThreadName));
        Redraw();
    }

    private List<SliceList> ComputeSampleSlices(ProfileData profile, int threadId = -1) {
        if (profile.Samples.Count == 0) {
            return new List<SliceList>();
        }

        startTime_ = profile.Samples[0].Sample.Time;
        endTime_ = profile.Samples[^1].Sample.Time;
        double slices = (maxWidth_ / sliceWidth_) * (prevMaxWidth_ / maxWidth_);
        prevMaxWidth_ = maxWidth_;

        var timeDiff = endTime_ - startTime_;
        double timePerSlice = (double)timeDiff.Ticks / slices;
        double timePerSliceReciproc = 1.0 / timePerSlice;
        var sliceSeriesDict = new Dictionary<int, SliceList>();

        var sw3 = Stopwatch.StartNew();
        int sampleIndex = 0;

        int prevSliceIndex = -1;
        SliceList prevSliceList = null;
        
        foreach (var (sample, stack) in profile.Samples) {
            if (threadId != -1 && stack.Context.ThreadId != threadId) {
                sampleIndex++;
                continue;
            }

            var sliceIndex = (int)((double)(sample.Time - startTime_).Ticks * timePerSliceReciproc);

            //int queryThreadId = stack.Context.ThreadId;
            int queryThreadId = 0;
            SliceList sliceList = null;
            
            if (sliceIndex == prevSliceIndex) {
                sliceList = prevSliceList;
            }
            else {
                sliceList = sliceSeriesDict.GetOrAddValue(queryThreadId,
                () => new SliceList(queryThreadId) {
                    TimePerSlice = TimeSpan.FromTicks((long)timePerSlice),
                    MaxSlices = (int)slices
                });
                
                prevSliceIndex = sliceIndex;
                prevSliceList = sliceList;
            }

            if (sliceIndex < sliceList.Slices.Count) {
                var sliceSpan = CollectionsMarshal.AsSpan(sliceList.Slices);
                sliceSpan[sliceIndex].Weight += sample.Weight;
                sliceSpan[sliceIndex].SampleCount++;
            }
            else {
                for (int i = sliceList.Slices.Count; i < sliceIndex; i++) {
                    sliceList.Slices.Add(new Slice(TimeSpan.Zero, -1, 0));
                }

                sliceList.Slices.Add(new Slice(sample.Weight, sampleIndex, 1));
            }

            sliceList.TotalWeight += sample.Weight;
            sliceList.MaxWeight = TimeSpan.FromTicks(Math.Max(sliceList.MaxWeight.Ticks,
                                                              sliceList.Slices[sliceIndex].Weight.Ticks));
            sampleIndex++;
        }

        if (sliceSeriesDict.Count == 0) {
            // Other code assumes there is at least one slice list, make a dummy one.
            return new List<SliceList>() { new SliceList(threadId) };
        }

        //Trace.WriteLine($"ComputeSampleSlices {sw3.ElapsedMilliseconds}ms");
        return sliceSeriesDict.ToValueList();
    }

    public void SetMaxWidth(double maxWidth) {
        if (Math.Abs(maxWidth - maxWidth_) < double.Epsilon) {
            return;
        }

        maxWidth_ = maxWidth;
        Redraw();
        InvalidateMeasure();
    }


    public void FilterTimeRange(SampleTimeRangeInfo range) {
        filterStartTime_ = range.StartTime - startTime_;
        filterEndTime_ = range.EndTime - startTime_;
        hasFilter_ = true;
        ClearSelectedTimeRange();
        UpdateFilterState();
    }

    public void ClearTimeRangeFilter() {
        hasFilter_ = false;
        UpdateFilterState();
    }

    private void UpdateFilterState() {
        Redraw();
        OnPropertyChanged(nameof(HasFilter));
        OnPropertyChanged(nameof(FilteredTime));
    }


    public void MarkSamples(List<SampleIndex> samples) {
        markedSamples_ = samples;
        Redraw();
    }

    public void ClearMarkedSamples() {
        markedSamples_ = null;
        Redraw();
    }


    private void Redraw() {
        if (!initialized_) {
            return;
        }

        UpdateSizes();
        using var graphDC = visual_.RenderOpen();
        var area = new Rect(0, 0, visibleArea_.Width, visibleArea_.Height);
        graphDC.DrawRectangle(backColor_, null, area);

        // Wait for sample slices to be computed.
        if (sliceTask_ != null) {
            sliceTask_.WaitForTask();
        }

        if (slices_ == null) {
            return;
        }

        if (hasFilter_) {
            DrawTimeRangeFilter(graphDC);
        }

        foreach (var list in slices_) {
            var scaledSliceWidth = maxWidth_ / list.MaxSlices;
            int startSlice = (int)(visibleArea_.Left / scaledSliceWidth);
            int endSlice = Math.Min((int)(visibleArea_.Right / scaledSliceWidth), list.Slices.Count);

            for (int i = startSlice; i < endSlice; i++) {
                var weight = list.Slices[i].Weight;
                double height = ((double)weight.Ticks / (double)list.MaxWeight.Ticks) * maxSampleHeight_;

                if (height < double.Epsilon) {
                    continue;
                }

                // Mark the slice that's under the mouse cursor.
                var backColor = sampleBackColor_;
                var borderColor = sampleBorderColor_;

                if (showPositionLine_ && !startedSelection_) {
                    if ((positionLineX_ + visibleArea_.Left) > i * scaledSliceWidth &&
                        (positionLineX_ + visibleArea_.Left) < (i + 1) * scaledSliceWidth) {
                        var newColor = ColorUtils.AdjustLight(((SolidColorBrush)sampleBackColor_).Color, 0.75f);
                        backColor = ColorBrushes.GetBrush(newColor);
                    }
                }

                height = Math.Max(height, MinSampleHeight);
                var rect = new Rect(i * scaledSliceWidth - visibleArea_.Left,
                                    maxSampleHeight_ - height + topMargin_,
                                    scaledSliceWidth, height);
                graphDC.DrawRectangle(backColor, borderColor, rect);
            }

            // Recompute the slices if zooming in/out, this will increase/reduces their number.
            if (scaledSliceWidth > 2 * SliceWidth) {
                double newWidth = Math.Max(1, SliceWidth * (SliceWidth / scaledSliceWidth));
                if (newWidth < sliceWidth_) {
                    StartComputeSampleSlices(newWidth);
                }
            }
            else if (scaledSliceWidth < SliceWidth / 2) {
                double newWidth = Math.Max(1, SliceWidth * (SliceWidth / scaledSliceWidth));
                if (newWidth > sliceWidth_) {
                    StartComputeSampleSlices(newWidth);
                }
            }
        }

        if (markedSamples_ != null) {
            DrawMarkedSamples(graphDC);
        }

        if (IsTimeBarVisible) {
            DrawTimeBar(graphDC);
        }

        if (hasFilter_ || !isThreadIncluded_) {
            DrawExcludedTimeRangeFilter(graphDC);
        }

        if (startedSelection_ || hasSelection_) {
            DrawSelection(graphDC);
        }

        if (showPositionLine_ && !startedSelection_) {
            DrawPositionLine(graphDC);
        }

        foreach (var ev in profile_.Events) {
            var startX = TimeToPosition(ev.Sample.Time);

            if (startX > visibleArea_.Width) {
                return;
            }

            graphDC.DrawRectangle(ev.Sample.CounterId != 0 ? Brushes.Red : Brushes.Blue, selectionBorderColor_,
                new Rect(startX, 0, 2, visibleArea_.Height));
        }
    }
    
    private void DrawTimeRangeFilter(DrawingContext graphDC) {
        var startX = TimeToPosition(filterStartTime_);
        var endX = TimeToPosition(filterEndTime_);

        if (startX > visibleArea_.Width) {
            return;
        }

        startX = Math.Max(0, startX);
        endX = Math.Min(endX, visibleArea_.Width);
        var width = Math.Max(1, endX - startX);
        var rect = new Rect(startX, 0, width, maxHeight_);
        graphDC.DrawRectangle(filteredBackColor_, null, rect);
    }

    private void DrawPositionLine(DrawingContext graphDC) {
        var lineStart = new Point(positionLineX_, 0);
        var lineEnd = new Point(positionLineX_, maxHeight_);
        graphDC.DrawLine(positionLinePen_, lineStart, lineEnd);

        var time = PositionToTime(positionLineX_);
        var textY = topMargin_ + 2;
        string text = "";
        var slice = TimeToSlice(time);

        if (slice.HasValue) {
            double cpuUsage = EstimateCpuUsage(slice.Value, slices_[0].TimePerSlice, samplingInterval_);
            text += $"{cpuUsage:F2}, {time.AsMillisecondsString()}";
            //text += $" (W {slice.Value.Weight.AsSecondsString()})";
        }
        else {
            text = time.AsMillisecondsString();
        }

        DrawText(text, positionLineX_, textY, Brushes.Black, graphDC, true, backColor_, sampleBorderColor_,
                 HorizontalAlignment.Center, VerticalAlignment.Center);
    }

    private void DrawSelection(DrawingContext graphDC) {
        var startX = TimeToPosition(selectionStartTime_);
        var endX = TimeToPosition(selectionEndTime_);

        if (endX < startX) {
            (startX, endX) = (endX, startX);
        }

        if (startX > visibleArea_.Width) {
            return;
        }

        var selectionWidth = Math.Max(1, endX - startX);
        var selectionHeight = maxHeight_;
        var rect = new Rect(startX, 0, selectionWidth, selectionHeight);
        graphDC.DrawRectangle(selectionBackColor_, selectionBorderColor_, rect);

        if (startedSelection_) {
            var time = TimeSpan.FromTicks(Math.Abs((selectionEndTime_ - selectionStartTime_).Ticks));
            var textX = startX + selectionWidth / 2;
            var textY = topMargin_ + 2;
            var text = time.AsMillisecondsString();
            DrawText(text, textX, textY, Brushes.Black, graphDC, true, backColor_, sampleBorderColor_,
                     HorizontalAlignment.Center, VerticalAlignment.Center);
        }
    }

    private void DrawExcludedTimeRangeFilter(DrawingContext graphDC) {
        if (isThreadIncluded_) {
            var startX = TimeToPosition(filterStartTime_);
            var endX = TimeToPosition(filterEndTime_);

            if (startX > 0) {
                var beforeRect = new Rect(0, 0, startX, maxHeight_);
                graphDC.DrawRectangle(filteredOutColor_, null, beforeRect);
                graphDC.DrawLine(filteredOutBorderColor_, new Point(startX, 0), new Point(startX, maxHeight_));
            }

            if (endX < visibleArea_.Width) {
                var afterRect = new Rect(endX, 0, visibleArea_.Width - endX, maxHeight_);
                graphDC.DrawRectangle(filteredOutColor_, null, afterRect);
                graphDC.DrawLine(filteredOutBorderColor_, new Point(endX, 0), new Point(endX, maxHeight_));
            }
        }
        else {
            var region = new Rect(0, 0, visibleArea_.Width, visibleArea_.Height);
            graphDC.DrawRectangle(filteredOutColor_, null, region);
        }
    }

    private void DrawMarkedSamples(DrawingContext graphDC) {
        double startX = double.MaxValue;
        double endX = double.MaxValue;
        double y = visibleArea_.Height - MarkerHeight;
        bool first = true;

        foreach (var sample in markedSamples_) {
            var x = TimeToPosition(sample.Time - startTime_);

            if (x < 0)
                continue;
            else if (x >= visibleArea_.Width)
                break;

            if (x - endX > 1) {
                double width = Math.Max(2, Math.Ceiling(endX - startX));
                var rect = new Rect(startX, y, width, MarkerHeight);
                graphDC.DrawRectangle(markerBackColor_, null, rect);
                startX = x;
                endX = x;
            }
            else if (first) {
                startX = x;
                endX = x;
                first = false;
            }
            else {
                endX = x;
            }
        }

        if (endX - startX > 1) {
            var rect = new Rect(startX, y, endX - startX, MarkerHeight);
            graphDC.DrawRectangle(markerBackColor_, null, rect);
        }
    }

    private void StartComputeSampleSlices(double newWidth) {
        sliceTask_ ??= new CancelableTaskInstance();
        sliceTask_.CreateTask();

        Task.Run(() => {
            sliceWidth_ = newWidth;
            slices_ = ComputeSampleSlices(profile_, ThreadId);
            sliceTask_.CompleteTask();

            // Update UI.
            Dispatcher.BeginInvoke(() => OnPropertyChanged(nameof(MaxCpuUsage)));
        });
    }

    private void DrawTimeBar(DrawingContext graphDC) {
        const double MinTickDistance = 60;
        const double MinSecondTickDistance = 40;
        const double TextMarginY = 7;
        var secTextColor = Brushes.Black;
        var msTextColor = Brushes.DimGray;

        var bar = new Rect(0, visibleArea_.Top,
                           visibleArea_.Width, TimeBarHeight);
        graphDC.DrawRectangle(timeBarBackColor_, null, bar);

        // Decide how many major (per second) ticks to use for the entire time range.
        var timeDiff = endTime_ - startTime_;
        double maxTicks = maxWidth_ / MinSecondTickDistance;
        double tickCount = Math.Ceiling(Math.Min(maxTicks, timeDiff.TotalSeconds));
        double secondsPerTick = timeDiff.TotalSeconds / tickCount;

        // Recompute the tick count and duration after rounding up
        // to ensure each tick corresponds to an integer number of seconds.
        tickCount = timeDiff.TotalSeconds / Math.Round(secondsPerTick);
        secondsPerTick = timeDiff.TotalSeconds / tickCount;
        double secondsTickDist = maxWidth_ / tickCount;

        // Adjust start position to the nearest multiple of the tick time.
        double startX = visibleArea_.Left;
        double endX = Math.Min(visibleArea_.Right, maxWidth_);
        double currentSec = Math.Floor(startX / secondsTickDist) * secondsPerTick;
        startX = (currentSec * secondsTickDist) / secondsPerTick;

        for (double x = startX; x < endX; x += secondsTickDist) {
            var tickRect = new Rect(x - visibleArea_.Left, visibleArea_.Top, 3, 4);
            graphDC.DrawRectangle(Brushes.Black, null, tickRect);
            DrawText($"{(int)Math.Round(currentSec)}s", tickRect.Left, tickRect.Top + TextMarginY, secTextColor, graphDC, false);
            
            int subTicks = (int)(secondsTickDist / MinTickDistance);
            if (subTicks > 1 && subTicks % 2 == 0)
                subTicks--;

            double subTickDist = secondsTickDist / (subTicks + 1);
            double timePerSubTick = (secondsPerTick * 1000.0) / (subTicks + 1);
            double msEndX = Math.Min(secondsTickDist - subTickDist, endX);
            double currentMs = timePerSubTick;

            for (double y = subTickDist; y <= msEndX; y += subTickDist) {
                var msTickRect = new Rect(x + y - visibleArea_.Left, visibleArea_.Top, 2, 3);
                graphDC.DrawRectangle(Brushes.DimGray, null, msTickRect);
                double time = (currentSec + currentMs / 1000);

                if (subTicks == 1) {
                    DrawText($"{time:0.0}", msTickRect.Left, msTickRect.Top + TextMarginY, msTextColor, graphDC, false);
                }
                else if (subTicks <= 10) {
                    DrawText($"{time:0.00}", msTickRect.Left, msTickRect.Top + TextMarginY, msTextColor, graphDC, false);
                }
                else if (subTicks <= 100) {
                    DrawText($"{time:0.000}", msTickRect.Left, msTickRect.Top + TextMarginY, msTextColor, graphDC, false);
                }
                else {
                    int digits = (int)Math.Ceiling(Math.Log10(subTicks));
                    var timeStr = String.Format("{0:0." + new string('0', digits + 1) + "}", time);
                    DrawText(timeStr, msTickRect.Left, msTickRect.Top + TextMarginY, msTextColor, graphDC, false);
                }

                currentMs += timePerSubTick;
            }

            currentSec += secondsPerTick;
        }
    }

    private void DrawText(string text, double x, double y, Brush color, DrawingContext dc,
                          bool keepInView = true,
                          Brush backColor = null, Pen borderColor = null,
                          HorizontalAlignment horizontalAlign = HorizontalAlignment.Center,
                          VerticalAlignment verticalAlign = VerticalAlignment.Top) {
        var glyphInfo = glyphs_.GetGlyphs(text);
        var textMargin = new Thickness(2, 4, 4, 0);

        if (horizontalAlign == HorizontalAlignment.Center) {
            x = x - glyphInfo.TextWidth / 2;
        }

        if (verticalAlign == VerticalAlignment.Center) {
            y = y + maxSampleHeight_ / 2 - glyphInfo.TextHeight / 2;
        }

        if (keepInView) {
            x = Math.Clamp(x, textMargin.Left, visibleArea_.Width - glyphInfo.TextWidth);
        }

        if (backColor != null) {
            var textRect = new Rect(x - textMargin.Left, y - textMargin.Top,
                                    glyphInfo.TextWidth + textMargin.Right, glyphInfo.TextHeight + textMargin.Bottom);
            dc.DrawRectangle(backColor, borderColor, textRect);
        }

        y = y + glyphInfo.TextHeight / 2;
        dc.PushTransform(new TranslateTransform(x, y));
        dc.DrawGlyphRun(color, glyphInfo.Glyphs);
        dc.Pop();
    }

    private TimeSpan PositionToTime(double positionX) {
        positionX = Math.Max(0, positionX);
        var timeDiff = endTime_ - startTime_;
        var ticks = (long)((visibleArea_.Left + positionX) / maxWidth_ * timeDiff.Ticks);
        return TimeSpan.FromTicks(ticks);
    }

    private double TimeToPosition(TimeSpan time) {
        var timeDiff = endTime_ - startTime_;
        return ((double)time.Ticks / (double)timeDiff.Ticks * maxWidth_) - visibleArea_.Left;
    }

    private int TimeToSampleIndex(TimeSpan time) {
        // Search for a nearby sample in both directions.
        var searchRange = TimeSpan.FromMilliseconds(10);
        int index = TimeToSampleIndex(time, searchRange);

        if (index > 0) {
            return index;
        }

        return TimeToSampleIndexBack(time, searchRange);
    }

    private Slice? TimeToSlice(TimeSpan time) {
        int sliceIndex = (int)(time.Ticks / slices_[0].TimePerSlice.Ticks);

        if (sliceIndex >= slices_[0].Slices.Count) {
            return null;
        }

        return slices_[0].Slices[sliceIndex];
    }

    private int TimeToSampleIndex(TimeSpan time, TimeSpan timeRange) {
        var queryTime = time + startTime_;
        int sliceIndex = (int)(time.Ticks / slices_[0].TimePerSlice.Ticks);
        timeRange += startTime_;

        for (int i = sliceIndex; i < slices_[0].Slices.Count; i++) {
            var slice = slices_[0].Slices[i];

            if (slice.FirstSampleIndex >= 0) {
                for (int sampleIndex = slice.FirstSampleIndex;
                    sampleIndex < slice.FirstSampleIndex + slice.SampleCount; sampleIndex++) {
                    if (profile_.Samples[sampleIndex].Sample.Time >= queryTime) {
                        if (!IsSingleThreadView || profile_.Samples[sampleIndex].Stack.Context.ThreadId == ThreadId) {
                            return sampleIndex;
                        }
                    }
                }
            }

            if ((i + 1) * slices_[0].TimePerSlice > timeRange) {
                break;
            }
        }

        return 0;
    }

    private int TimeToSampleIndexBack(TimeSpan time, TimeSpan timeRange) {
        var queryTime = time + startTime_;
        int sliceIndex = (int)(time.Ticks / slices_[0].TimePerSlice.Ticks);
        sliceIndex = Math.Min(sliceIndex, slices_[0].Slices.Count - 1);
        timeRange += startTime_;

        for (int i = sliceIndex; i >= 0; i--) {
            var slice = slices_[0].Slices[i];

            if (slice.FirstSampleIndex >= 0) {
                for (int sampleIndex = slice.FirstSampleIndex + slice.SampleCount - 1; sampleIndex >= slice.FirstSampleIndex; sampleIndex--) {
                    if (profile_.Samples[sampleIndex].Sample.Time <= queryTime) {
                        if (!IsSingleThreadView || profile_.Samples[sampleIndex].Stack.Context.ThreadId == ThreadId) {
                            return sampleIndex;
                        }
                    }
                }
            }

            if ((sliceIndex - i + 1) * slices_[0].TimePerSlice > timeRange) {
                break;
            }
        }

        return 0;
    }

    private double EstimateCpuUsage(Slice slice, TimeSpan timePerSlice, TimeSpan samplingInterval) {
        if (samplingInterval == TimeSpan.Zero) {
            return 0;
        }

        var samples100Percent = timePerSlice / samplingInterval;
        var usage = (double)slice.SampleCount / samples100Percent;
        return usage;
    }

    protected override int VisualChildrenCount => 1;

    protected override Visual GetVisualChild(int index) {
        return visual_;
    }

    protected override Size MeasureOverride(Size availableSize) {
        if (visual_ == null) {
            return new Size(0, 0);
        }

        return visual_.ContentBounds.Size;
    }

    public void Reset() {
        if (visual_ == null) {
            return;
        }

        RemoveVisualChild(visual_);
        RemoveLogicalChild(visual_);
    }

    public void SetHorizontalOffset(double offset) {
        visibleArea_ = new Rect(offset, 0, visibleArea_.Width, visibleArea_.Height);
        Redraw();
    }

    public void SetVisibleWidth(double width) {
        visibleArea_ = new Rect(visibleArea_.Left, 0, width, visibleArea_.Height);
        Redraw();
    }

    public event PropertyChangedEventHandler PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null) {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null) {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}