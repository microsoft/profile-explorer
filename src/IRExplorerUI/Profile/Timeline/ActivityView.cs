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
using IRExplorerCore.Utilities;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TaskbarClock;

namespace IRExplorerUI.Profile;


public record SampleTimeRangeInfo(TimeSpan StartTime, TimeSpan EndTime,
                                  int StartSampleIndex, int EndSampleIndex, int ThreadId);

public record SampleTimePointInfo(TimeSpan Time, int SampleIndex, int ThreadId);

public class ActivityView : FrameworkElement, INotifyPropertyChanged {
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
    const double TopMarginY = 1 + TimeBarHeight;
    const double BottomMarginY = 12;
    const double DefaultTextSize = 11;

    private bool initialized_;
    private DrawingVisual visual_;
    private GlyphRunCache glyphs_;
    private ProfileData profile_;
    private double sliceWidth_;
    private double sampleHeight_;
    private double maxWidth_;
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

    public ActivityView() {
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseLeftButtonUp += ActivityView_MouseLeftButtonUp;
        MouseMove += ActivityView_MouseMove;
        MouseLeave += ActivityView_MouseLeave;
    }

    public event EventHandler<SampleTimeRangeInfo> SelectingTimeRange;
    public event EventHandler<SampleTimeRangeInfo> SelectedTimeRange;
    public event EventHandler<SampleTimeRangeInfo> FilteredTimeRange;
    public event EventHandler ClearedSelectedTimeRange;
    public event EventHandler ClearedFilteredTimeRange;

    public event EventHandler<SampleTimePointInfo> HoveringTimePoint;
    public event EventHandler<SampleTimePointInfo> SelectedTimePoint;
    public event EventHandler ClearedTimePoint;

    public int ThreadId { get; private set; }
    public bool IsSingleThreadView => ThreadId != -1;
    
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

    private void UpdateSizes() {
        topMargin_ = IsTimeBarVisible ? TopMarginY : 2;
        bottomMargin_ = BottomMarginY;
        sampleHeight_ = visibleArea_.Height - topMargin_ - bottomMargin_;
    }


    public void SelectTimeRange(SampleTimeRangeInfo range) {
        selectionStartTime_ = range.StartTime - startTime_;
        selectionEndTime_ = range.EndTime - startTime_;
        hasSelection_ = true;
        startedSelection_ = false;
        Redraw();
    }

    public void ClearSelectedTimeRange() {
        if (hasSelection_) {
            hasSelection_ = false;
            startedSelection_ = false;
            Redraw();
        }
    }


    private SampleTimePointInfo GetSelectedTimePoint() {
        return new SampleTimePointInfo(selectionStartTime_ + startTime_, 
                                       TimeToSampleIndex(selectionStartTime_), ThreadId);
    }

    private SampleTimeRangeInfo GetSelectedTimeRange() {
        return new SampleTimeRangeInfo(selectionStartTime_ + startTime_, selectionEndTime_ + startTime_,
                                       TimeToSampleIndex(selectionStartTime_, SelectionTimeDiff),
                                       TimeToSampleIndexBack(selectionEndTime_, SelectionTimeDiff), ThreadId);
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
            ReleaseMouseCapture();
            Redraw();

            if (hasSelection_) {
                SelectedTimeRange?.Invoke(this, GetSelectedTimeRange());
            }
        }
        else {
            SelectedTimePoint?.Invoke(this, GetSelectedTimePoint());
        }
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

            HoveringTimePoint?.Invoke(this, GetSelectedTimePoint());
        }

    }

    public void FilterTimeRange(SampleTimeRangeInfo range) {
        filterStartTime_ = range.StartTime - startTime_;
        filterEndTime_ = range.EndTime - startTime_;
        hasFilter_ = true;
        ClearSelectedTimeRange();
        Redraw();
    }

    public void FilterAllOut() {
        filterStartTime_ = filterEndTime_ = TimeSpan.Zero;
        hasFilter_ = true;
        ClearSelectedTimeRange();
        Redraw();
    }

    public void ClearTimeRangeFilter() {
        if (hasFilter_) {
            hasFilter_ = false;
            Redraw();
        }
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        var time = PositionToTime(e.GetPosition(this).X);
        
        if (hasSelection_) {

            if (time >= selectionStartTime_ && time <= selectionEndTime_) {
                if (e.ClickCount > 1) {
                    var range = GetSelectedTimeRange();
                    FilterTimeRange(range);
                    FilteredTimeRange?.Invoke(this, range);
                }

                return;
            }

            hasSelection_ = false;
            ClearedSelectedTimeRange?.Invoke(this, EventArgs.Empty);
        }


        if (hasFilter_) {
            if (time < filterStartTime_ || time > filterEndTime_) {
                if (e.ClickCount > 1) {
                    ClearTimeRangeFilter();
                    ClearedFilteredTimeRange?.Invoke(this, EventArgs.Empty);
                }
            }
        }
        
        ClearedTimePoint?.Invoke(this, EventArgs.Empty);

        startedSelection_ = true;
        selectionStartTime_ = time;
        selectionEndTime_ = selectionStartTime_;
        CaptureMouse();
        Redraw();
    }

    public async Task Initialize(ProfileData profile, Rect visibleArea, int threadId = -1) {
        if (initialized_) {
            return;
        }

        initialized_ = true;
        profile_ = profile;
        visibleArea_ = visibleArea;
        ThreadId = threadId;
        
        maxWidth_ = visibleArea.Width;
        visual_ = new DrawingVisual();
        visual_.Drawing?.Freeze();
        AddVisualChild(visual_);
        AddLogicalChild(visual_);

        backColor_ = Brushes.WhiteSmoke;
        font_ = new Typeface("Segoe UI");
        fontSize_ = DefaultTextSize;
        glyphs_ = new GlyphRunCache(font_, fontSize_, VisualTreeHelper.GetDpi(visual_).PixelsPerDip);

        sliceWidth_ = SliceWidth;
        slices_ = await Task.Run(() => ComputeSampleSlices(profile, threadId));
        Redraw();
    }

    private List<SliceList> ComputeSampleSlices(ProfileData profile, int threadId = -1) {
        if (profile.Samples.Count == 0) {
            return new List<SliceList>();
        }

        startTime_ = profile.Samples[0].Sample.Time;
        endTime_ = profile.Samples[^1].Sample.Time;
        double slices = maxWidth_ / sliceWidth_;
        var timeDiff = endTime_ - startTime_;
        double timePerSlice = (double)timeDiff.Ticks / slices;
        var sliceSeriesDict = new Dictionary<int, SliceList>();

        var sw3 = Stopwatch.StartNew();
        int sampleIndex = 0;

        foreach (var (sample, stack) in profile.Samples) {
            if(threadId != -1 && stack.Context.ThreadId != threadId) {
                sampleIndex++;
                continue;
            }

            var sliceIndex = (int)((sample.Time - startTime_).Ticks / timePerSlice);

            //int queryThreadId = stack.Context.ThreadId;
            int queryThreadId = 0;

            var sliceList = sliceSeriesDict.GetOrAddValue(queryThreadId,
                () => new SliceList(queryThreadId) {
                    TimePerSlice = TimeSpan.FromTicks((long)timePerSlice),
                    MaxSlices = (int)slices
                });

            if(sliceIndex < sliceList.Slices.Count) {
                var sliceSpan = CollectionsMarshal.AsSpan(sliceList.Slices);
                sliceSpan[sliceIndex].Weight += sample.Weight;
                sliceSpan[sliceIndex].SampleCount++;
            } else {
                for(int i = sliceList.Slices.Count; i < sliceIndex; i++) {
                    sliceList.Slices.Add(new Slice(TimeSpan.Zero, -1, 0));
                }

                sliceList.Slices.Add(new Slice(sample.Weight, sampleIndex, 1));
            }

            sliceList.TotalWeight += sample.Weight;
            sliceList.MaxWeight = TimeSpan.FromTicks(Math.Max(sliceList.MaxWeight.Ticks,
                                                              sliceList.Slices[sliceIndex].Weight.Ticks));
            sampleIndex++;
        }

        Trace.WriteLine($"ComputeSampleSlices {sw3.ElapsedMilliseconds}ms");
        return sliceSeriesDict.ToValueList();
    }

    public void UpdateMaxWidth(double maxWidth) {
        maxWidth_ = maxWidth;
        Redraw();
        InvalidateMeasure();
    }
    
    private void Redraw() {
        if (!initialized_) {
            return;
        }

        UpdateSizes();
        using var graphDC = visual_.RenderOpen();
        var area = new Rect(0, 0, visibleArea_.Width, visibleArea_.Height);
        graphDC.DrawRectangle(backColor_, null, area);

        if (slices_ == null) {
            return;
        }

        // lay on top technique

        foreach (var list in slices_) {
            var scaledSliceWidth = maxWidth_ / list.MaxSlices;

            for(int i = 0; i < list.Slices.Count; i++) {
                var weight = list.Slices[i].Weight;
                double height = ((double)weight.Ticks / (double)list.MaxWeight.Ticks) * sampleHeight_;

                if(height < double.Epsilon) {
                    continue;
                }

                if (i * scaledSliceWidth + scaledSliceWidth < visibleArea_.Left) {
                    continue;
                }
                else if (i * scaledSliceWidth > visibleArea_.Right) {
                    break;
                }

                var rect = new Rect(i * scaledSliceWidth - visibleArea_.Left,
                                    sampleHeight_ - height + topMargin_,
                                    scaledSliceWidth, height);
                graphDC.DrawRectangle(sampleBackColor_, sampleBorderColor_, rect);
            }

            //if (scaledSliceWidth > 2 * sliceWidth_) {
            //    sliceWidth_ = Math.Max(1, sliceWidth_ / 2);
            //    ComputeSampleSlices(profile_, ThreadId);
            //}
        }

        if (IsTimeBarVisible) {
            DrawTimeBar(graphDC);
        }
        
        if (hasFilter_) {
            var startX = TimeToPosition(filterStartTime_);
            var endX = TimeToPosition(filterEndTime_);
            var brush = ColorBrushes.GetTransparentBrush(Colors.Gray, 120);

            if (startX > 0) {
                var beforeRect = new Rect(0, 0, startX, sampleHeight_ + topMargin_);
                graphDC.DrawRectangle(brush, null, beforeRect);
            }

            if (endX < visibleArea_.Width) {
                var afterRect = new Rect(endX, 0, visibleArea_.Width - endX, sampleHeight_ + topMargin_);
                graphDC.DrawRectangle(brush, null, afterRect);
            }
        }

        if (startedSelection_ || hasSelection_) {
            var startX = TimeToPosition(selectionStartTime_);
            var endX = TimeToPosition(selectionEndTime_);

            if (endX < startX) {
                (startX, endX) = (endX, startX);
            }

            var selectionWidth = Math.Max(1, endX - startX);
            var selectionHeight = sampleHeight_ + topMargin_;
            var rect = new Rect(startX, 0, selectionWidth, selectionHeight);
            var opacity = startedSelection_ ? 120 : 100;
            graphDC.DrawRectangle(ColorBrushes.GetTransparentBrush(Colors.Gold, opacity), ColorPens.GetPen(Colors.Black), rect);

            var time = TimeSpan.FromTicks(Math.Abs((selectionEndTime_ - selectionStartTime_).Ticks));
            var textX = startX + selectionWidth / 2;
            var textY = selectionHeight + bottomMargin_ / 2 - 3;
            DrawText(time.AsMillisecondsString(), textX, textY, Brushes.MediumBlue, graphDC, backColor_);
        }
        
        if (showPositionLine_ && !startedSelection_) {
            var lineStart = new Point(positionLineX_, 0);
            var lineEnd = new Point(positionLineX_, sampleHeight_ + topMargin_);
            graphDC.DrawLine(ColorPens.GetBoldPen(Colors.DarkBlue), lineStart, lineEnd);

            var time = PositionToTime(positionLineX_);
            var textY = sampleHeight_ + topMargin_ + bottomMargin_ / 2 - 3;
            var text = time.AsMillisecondsString();

            var slice = TimeToSlice(time);

            if (slice.HasValue) {
                text += $" ({slice.Value.SampleCount}, {slice.Value.Weight.AsMillisecondsString()})";
            }

            DrawText(text, positionLineX_, textY, Brushes.Black, graphDC, backColor_);

            
        }
    }

    private void DrawTimeBar(DrawingContext graphDC) {
        const double MinTickDistance = 50;
        const double MinSecondTickDistance = 50;
        const double TextMarginY = 7;
        var secTextColor = Brushes.Black;
        var msTextColor = Brushes.DimGray;

        var bar = new Rect(0, visibleArea_.Top,
                           visibleArea_.Width, TimeBarHeight);
        graphDC.DrawRectangle(Brushes.AliceBlue, null, bar);

        var timeDiff = endTime_ - startTime_;
        double maxSecTicks = maxWidth_ / MinSecondTickDistance;
        double secTicks = Math.Ceiling(Math.Min(maxSecTicks, timeDiff.TotalSeconds));
        double secPerTick = timeDiff.TotalSeconds / secTicks;
        secTicks = timeDiff.TotalSeconds / Math.Ceiling(secPerTick);
        secPerTick = timeDiff.TotalSeconds / secTicks;
        double secondTickDist = maxWidth_ / secTicks;

        double startX = Math.Max(0,  -secondTickDist);
        double endX = Math.Min(visibleArea_.Right, maxWidth_);
        //double currentSec = startX / secondTickDist;
        double currentSec = Math.Floor(startX / secondTickDist);

        for (double x = startX; x < endX; x += secondTickDist) {
            var tickRect = new Rect(x - visibleArea_.Left, visibleArea_.Top, 3, 4);
            graphDC.DrawRectangle(Brushes.Black, null, tickRect);
            DrawText($"{(int)Math.Round(currentSec)}s", tickRect.Left, tickRect.Top + TextMarginY, secTextColor, graphDC);

            double subTicks = secondTickDist / MinTickDistance;
            double subTickDist = secondTickDist / subTicks;
            double timePerSubTick = 1000.0 / subTicks;
            double msEndX = Math.Min(secondTickDist - subTickDist, endX);
            double currentMs = timePerSubTick;

            for (double y = subTickDist; y < msEndX; y += subTickDist) {
                var msTickRect = new Rect(x + y - visibleArea_.Left, visibleArea_.Top, 2, 3);
                graphDC.DrawRectangle(Brushes.DimGray, null, msTickRect);
                double time = (currentSec + currentMs / 1000);

                if (subTicks <= 10) {
                    DrawText($"{time:0.0}", msTickRect.Left, msTickRect.Top + TextMarginY, msTextColor, graphDC);
                }
                else if (subTicks <= 100) {
                    DrawText($"{time:0.00}", msTickRect.Left, msTickRect.Top + TextMarginY, msTextColor, graphDC);
                }
                else {
                    int digits = (int)Math.Ceiling(Math.Log10(subTicks));
                    var timeStr = String.Format("{0:0." + new string('0', digits) + "}", time);
                    DrawText(timeStr, msTickRect.Left, msTickRect.Top + TextMarginY, msTextColor, graphDC);
                }

                currentMs += timePerSubTick;
            }

            currentSec += secPerTick;
        }
    }

    private void DrawText(string text, double x, double y, Brush color, DrawingContext dc,
                          Brush backColor = null, HorizontalAlignment alignment = HorizontalAlignment.Center) {
        var glyphInfo = glyphs_.GetGlyphs(text);
        
        if (backColor != null) {
            var textRect = new Rect(x - glyphInfo.TextWidth / 2 - 1, y - 1, glyphInfo.TextWidth + 2, glyphInfo.TextHeight);
            dc.DrawRectangle(backColor, null, textRect);
        }

        if (alignment == HorizontalAlignment.Center) {
            x = x - glyphInfo.TextWidth / 2;
        }

        y = y + glyphInfo.TextHeight / 2;
        dc.PushTransform(new TranslateTransform(x, y));
        dc.DrawGlyphRun(color, glyphInfo.Glyphs);
        dc.Pop();
    }

    private TimeSpan PositionToTime(double positionX) {
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
        var searchRange = TimeSpan.FromMilliseconds(1);
        int index = TimeToSampleIndex(time, searchRange);

        if (index != -1) {
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
        int sliceIndex = (int)(time.Ticks / slices_[0].TimePerSlice.Ticks);
        var queryTime = time + startTime_;
        timeRange += startTime_;

        for (int i = sliceIndex; i < slices_[0].Slices.Count; i++) {
            var slice = slices_[0].Slices[i];

            if (slice.FirstSampleIndex >= 0) {
                for(int sampleIndex = slice.FirstSampleIndex; sampleIndex < slice.FirstSampleIndex + slice.SampleCount; sampleIndex++) {
                    if (profile_.Samples[sampleIndex].Sample.Time >= queryTime) {
                        return sampleIndex;
                    }
                }
            }

            if((i + 1) * slices_[0].TimePerSlice > timeRange) {
                break;
            }
        }
        
        return 0;
    }

    private int TimeToSampleIndexBack(TimeSpan time, TimeSpan timeRange) {
        int sliceIndex = (int)(time.Ticks / slices_[0].TimePerSlice.Ticks);
        sliceIndex = Math.Min(sliceIndex, slices_[0].Slices.Count - 1);
        var queryTime = time + startTime_;
        timeRange += startTime_;
        
        for (int i = sliceIndex; i >= 0; i--) {
            var slice = slices_[0].Slices[i];

            if (slice.FirstSampleIndex >= 0) {
                for(int sampleIndex = slice.FirstSampleIndex + slice.SampleCount; sampleIndex >= slice.FirstSampleIndex; sampleIndex--) {
                    if (profile_.Samples[sampleIndex].Sample.Time <= queryTime) {
                        return sampleIndex;
                    }
                }
            }

            if((sliceIndex - i + 1) * slices_[0].TimePerSlice > timeRange) {
                break;
            }
        }

        return 0;
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

    public void UpdateVisibleArea(Rect visibleArea) {
        visibleArea_ = visibleArea;
        Redraw();
    }

    public event PropertyChangedEventHandler PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null) {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null) {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}