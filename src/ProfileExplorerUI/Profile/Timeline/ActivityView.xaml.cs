// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ProfileExplorer.Core;

namespace ProfileExplorer.UI.Profile;

public class MarkedSamples {
  public MarkedSamples(int index, ProfileCallTreeNode node, List<SampleIndex> samples, HighlightingStyle style) {
    Index = index;
    Node = node;
    Samples = samples;
    Style = style;
  }

  public int Index { get; set; }
  public ProfileCallTreeNode Node { get; set; }
  public List<SampleIndex> Samples { get; set; }
  public HighlightingStyle Style { get; set; }
}

public partial class ActivityView : FrameworkElement, INotifyPropertyChanged {
  private const double DefaultSliceWidth = 4;
  private const double TimeBarHeight = 18;
  private const double MinSampleHeight = 4;
  private const double TopMarginY = 1 + TimeBarHeight;
  private const double BottomMarginY = 0;
  private const string DefaultFont = "Segoe UI";
  private const double DefaultTextSize = 11;
  private const double MarkerHeight = 8;
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
  private bool hasAllSelection_;
  private TimeSpan selectionStartTime_;
  private TimeSpan selectionEndTime_;
  private TimeSpan filterStartTime_;
  private TimeSpan filterEndTime_;
  private bool hasFilter_;
  private Typeface font_;
  private double fontSize_;
  private Brush backColor_;
  private TimeSpan startTime_;
  private TimeSpan endTime_;
  private List<MarkedSamples> markedSamples_;
  private List<SampleIndex> selectedSamples_;
  private CancelableTaskInstance sliceTask_;
  private bool isThreadIncluded_;
  private Brush timeBarBackColor_;
  private Brush filteredBackColor_;
  private Brush filteredOutColor_;
  private Pen filteredOutBorderColor_;
  private Brush markerBackColor_;
  private bool isTimeBarVisible_;
  private Brush sampleBackColor_;
  private Pen sampleBorderColor_;
  private Brush selectionBackColor_;
  private Pen selectionBorderColor_;
  private Pen markerBorderColor_;
  private Pen positionLinePen_;

  public ActivityView() {
    InitializeComponent();
    filteredOutColor_ = ColorBrushes.GetTransparentBrush(Colors.WhiteSmoke, 190);
    selectionBackColor_ = ColorBrushes.GetTransparentBrush("#A7D5F5", 150); // SelectedBackgroundBrush
    filteredBackColor_ = ColorBrushes.GetBrush(Colors.Linen);
    selectionBorderColor_ = ColorPens.GetPen(Colors.Black);
    markerBackColor_ = ColorBrushes.GetTransparentBrush("#0F92EF", 120);
    markerBorderColor_ = ColorPens.GetPen(Colors.DimGray);
    positionLinePen_ = ColorPens.GetBoldPen(Colors.DarkBlue);
    filteredOutBorderColor_ = ColorPens.GetBoldPen(Colors.Black);
    markedSamples_ = new List<MarkedSamples>();
    ThreadId = -1;
    DataContext = this;

    MouseLeftButtonDown += OnMouseLeftButtonDown;
    MouseLeftButtonUp += ActivityView_MouseLeftButtonUp;
    MouseMove += ActivityView_MouseMove;
    MouseLeave += ActivityView_MouseLeave;
    PreviewMouseWheel += ActivityView_PreviewMouseWheel;
  }

  public List<MarkedSamples> MarkedSamples => markedSamples_;
  public RelayCommand<object> FilterTimeRangeCommand => new(obj => ApplyTimeRangeFilter());
  public RelayCommand<object> ClearSelectionCommand => new(obj => ClearSelectedTimeRange());
  public RelayCommand<object> RemoveTimeRangeFilterCommand => new(obj => RemoveTimeRangeFilter());
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

  public bool HasAllTimeSelected => hasSelection_ && hasAllSelection_;

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

  public Brush TimeBarBackColor {
    get => timeBarBackColor_;
    set {
      SetField(ref timeBarBackColor_, value);
      Redraw();
    }
  }

  public Brush FilteredBackColor {
    get => filteredBackColor_;
    set {
      SetField(ref filteredBackColor_, value);
      Redraw();
    }
  }

  public Brush FilteredOutColor {
    get => filteredOutColor_;
    set {
      SetField(ref filteredOutColor_, value);
      Redraw();
    }
  }

  public Pen FilteredOutBorderColor {
    get => filteredOutBorderColor_;
    set {
      SetField(ref filteredOutBorderColor_, value);
      Redraw();
    }
  }

  public Brush MarkerBackColor {
    get => markerBackColor_;
    set {
      SetField(ref markerBackColor_, value);
      Redraw();
    }
  }

  public bool IsTimeBarVisible {
    get => isTimeBarVisible_;
    set {
      SetField(ref isTimeBarVisible_, value);
      Redraw();
    }
  }

  public Brush SamplesBackColor {
    get => sampleBackColor_;
    set {
      SetField(ref sampleBackColor_, value);
      Redraw();
    }
  }

  public Pen SampleBorderColor {
    get => sampleBorderColor_;
    set {
      SetField(ref sampleBorderColor_, value);
      Redraw();
    }
  }

  public Brush SelectionBackColor {
    get => selectionBackColor_;
    set {
      SetField(ref selectionBackColor_, value);
      Redraw();
    }
  }

  public Pen SelectionBorderColor {
    get => selectionBorderColor_;
    set {
      SetField(ref selectionBorderColor_, value);
      Redraw();
    }
  }

  public Pen MarkerBorderColor {
    get => markerBorderColor_;
    set {
      SetField(ref markerBorderColor_, value);
      Redraw();
    }
  }

  public Pen PositionLinePen {
    get => positionLinePen_;
    set {
      SetField(ref positionLinePen_, value);
      Redraw();
    }
  }

  protected override int VisualChildrenCount => 1;
  private TimeSpan SelectionTimeDiff => selectionEndTime_ - selectionStartTime_;
  public event PropertyChangedEventHandler PropertyChanged;
  public event EventHandler<SampleTimeRangeInfo> SelectingTimeRange;
  public event EventHandler<SampleTimeRangeInfo> SelectedTimeRange;
  public event EventHandler<SampleTimeRangeInfo> FilteredTimeRange;
  public event EventHandler<bool> ThreadIncludedChanged;
  public event EventHandler ClearedSelectedTimeRange;
  public event EventHandler ClearedFilteredTimeRange;
  public event EventHandler<SampleTimePointInfo> HoveringTimePoint;
  public event EventHandler<SampleTimePointInfo> SelectedTimePoint;
  public event EventHandler ClearedTimePoint;

  public void SelectTimeRange(SampleTimeRangeInfo range) {
    selectionStartTime_ = range.StartTime - startTime_;
    selectionEndTime_ = range.EndTime - startTime_;
    hasSelection_ = true;
    startedSelection_ = false;
    hasAllSelection_ = false;
    UpdateSelectionState();
  }

  public void ClearSelectedTimeRange() {
    if (hasSelection_) {
      hasSelection_ = false;
      hasAllSelection_ = false;
      startedSelection_ = false;
      UpdateSelectionState();
    }
  }

  public Task Initialize(ProfileData profile, Rect visibleArea, int threadId = -1) {
    if (initialized_) {
      return Task.CompletedTask;
    }

    initialized_ = true;
    isThreadIncluded_ = true;
    profile_ = profile;
    visibleArea_ = visibleArea;
    PreviousIsThreadIncluded = true;
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
    return StartComputeSampleSlices(DefaultSliceWidth);
  }

  public void InitializeDone() {
    OnPropertyChanged(nameof(ThreadWeight));
    OnPropertyChanged(nameof(ThreadId));
    OnPropertyChanged(nameof(ThreadName));
    Redraw();
  }

  public void SetMaxWidth(double maxWidth) {
    if (Math.Abs(maxWidth - maxWidth_) < double.Epsilon) {
      return;
    }

    prevMaxWidth_ = maxWidth;
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

  public void SelectSamples(List<SampleIndex> samples) {
    selectedSamples_ = samples;
    Redraw();
  }

  public void MarkSamples(ProfileCallTreeNode node, List<SampleIndex> samples, HighlightingStyle style) {
    markedSamples_.RemoveAll(s => s.Node == node);
    markedSamples_.Add(new MarkedSamples(markedSamples_.Count, node, samples, style));
    Redraw();
  }

  public void RemoveMarkedSamples(ProfileCallTreeNode node) {
    markedSamples_.RemoveAll(s => s.Node == node);
    Redraw();
  }

  public void ClearMarkedSamples() {
    markedSamples_.Clear();
    Redraw();
  }

  public void ClearSelectedSamples() {
    selectedSamples_ = null;
    Redraw();
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

  protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null) {
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
  }

  protected override Visual GetVisualChild(int index) {
    return visual_;
  }

  protected override Size MeasureOverride(Size availableSize) {
    if (visual_ == null) {
      return new Size(0, 0);
    }

    return visual_.ContentBounds.Size;
  }

  protected bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null) {
    if (EqualityComparer<T>.Default.Equals(field, value))
      return false;
    field = value;
    OnPropertyChanged(propertyName);
    return true;
  }

  private void UpdateSizes() {
    topMargin_ = IsTimeBarVisible ? TopMarginY : 2;
    bottomMargin_ = BottomMarginY;
    maxSampleHeight_ = visibleArea_.Height - topMargin_;
    maxHeight_ = Math.Max(visibleArea_.Height, TimeBarHeight);
  }

  private void UpdateSelectionState() {
    Redraw();
    OnPropertyChanged(nameof(HasSelection));
    OnPropertyChanged(nameof(SelectionTime));

    if (!hasSelection_) {
      ClearedSelectedTimeRange?.Invoke(this, EventArgs.Empty);
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
    (int startIndex, int endIndex) = TimeRangeToSampleIndex(selectionStartTime_, selectionEndTime_);
    return new SampleTimeRangeInfo(selectionStartTime_ + startTime_, selectionEndTime_ + startTime_,
                                   startIndex, endIndex, ThreadId);
  }

  private SampleTimeRangeInfo GetFilteredTimeRange() {
    (int startIndex, int endIndex) = TimeRangeToSampleIndex(filterStartTime_, filterEndTime_);
    return new SampleTimeRangeInfo(filterStartTime_ + startTime_, filterEndTime_ + startTime_,
                                   startIndex, endIndex, ThreadId);
  }

  private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
    var time = PositionToTime(e.GetPosition(this).X);

    if (hasSelection_) {
      if (time >= selectionStartTime_ && time <= selectionEndTime_) {
        if (e.ClickCount > 1) { // Check for double-click.
          ApplyTimeRangeFilter();
        }

        return;
      }

      // Deselect with click outside the selection.
      hasSelection_ = false;
      UpdateSelectionState();
    }

    if (hasFilter_) {
      if (time < filterStartTime_ || time > filterEndTime_) {
        if (e.ClickCount > 1) { // Check for double-click.
          RemoveTimeRangeFilter();
        }
      }
    }

    ClearedTimePoint?.Invoke(this, EventArgs.Empty);
    startedSelection_ = true;
    hasAllSelection_ = false;
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

  public void SelectAllTime() {
    SelectTimeRange(new SampleTimeRangeInfo(startTime_, endTime_, 0, 0, 0));
    hasAllSelection_ = true;
    SelectedTimeRange?.Invoke(this, GetSelectedTimeRange());
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

  private List<SliceList> ComputeSampleSlices(ProfileData profile, int threadId = -1) {
    if (profile.Samples.Count == 0) {
      return new List<SliceList>();
    }

    startTime_ = profile.Samples[0].Sample.Time;
    endTime_ = profile.Samples[^1].Sample.Time;
    double slices = maxWidth_ / sliceWidth_ * (prevMaxWidth_ / maxWidth_);
    var timeDiff = endTime_ - startTime_;
    double timePerSlice = timeDiff.Ticks / slices;
    double timePerSliceReciproc = 1.0 / timePerSlice;
    var sliceSeriesDict = new Dictionary<int, SliceList>();

    int sampleIndex = 0;
    int prevSliceIndex = -1;
    SliceList prevSliceList = null;
    var currentSlice = new Slice(TimeSpan.Zero, -1, 0);
    var dummySlice = new Slice(TimeSpan.Zero, -1, 0);

    var ranges = profile.ThreadSampleRanges.Ranges[threadId];
    var sampleSpan = CollectionsMarshal.AsSpan(profile.Samples);

    foreach (var range in ranges) {
      sampleIndex = range.StartIndex;

      for (int k = range.StartIndex; k < range.EndIndex; k++) {
        // if (threadId != -1 && stack.Context.ThreadId != threadId) {
        //   sampleIndex++;
        //   continue;
        // }

        ref var sample = ref sampleSpan[k].Sample;
        int sliceIndex = (int)((sample.Time - startTime_).Ticks * timePerSliceReciproc);

        //int queryThreadId = stack.Context.ThreadId;
        int queryThreadId = 0;

        if (sliceIndex != prevSliceIndex) {
          if (currentSlice.FirstSampleIndex != -1 && prevSliceList != null) {
            prevSliceList.Slices.Add(currentSlice);
            prevSliceList.TotalWeight += currentSlice.Weight;
            prevSliceList.MaxWeight = TimeSpan.FromTicks(Math.Max(prevSliceList.MaxWeight.Ticks,
                                                                  currentSlice.Weight.Ticks));
          }

          if (!sliceSeriesDict.TryGetValue(queryThreadId, out var sliceList)) {
            sliceList = new SliceList(queryThreadId, (int)Math.Ceiling(slices)) {
              TimePerSlice = TimeSpan.FromTicks((long)timePerSlice),
              MaxSlices = (int)slices
            };

            sliceSeriesDict[queryThreadId] = sliceList;
          }

          prevSliceIndex = sliceIndex;
          prevSliceList = sliceList;

          if (sliceIndex >= sliceList.Slices.Count) {
            for (int i = sliceList.Slices.Count; i < sliceIndex; i++) {
              sliceList.Slices.Add(dummySlice);
            }
          }

          currentSlice = new Slice(TimeSpan.Zero, sampleIndex, 0);
        }

        currentSlice.Weight += sample.Weight;
        currentSlice.SampleCount++;
        sampleIndex++;
      }
    }

    if (currentSlice.FirstSampleIndex != -1 && prevSliceList != null) {
      prevSliceList.Slices.Add(currentSlice);
      prevSliceList.TotalWeight += currentSlice.Weight;
      prevSliceList.MaxWeight = TimeSpan.FromTicks(Math.Max(prevSliceList.MaxWeight.Ticks,
                                                            currentSlice.Weight.Ticks));
    }

    if (sliceSeriesDict.Count == 0) {
      // Other code assumes there is at least one slice list, make a dummy one.
      return new List<SliceList> {new(threadId)};
    }

    return sliceSeriesDict.ToValueList();
  }

  private void UpdateFilterState() {
    Redraw();
    OnPropertyChanged(nameof(HasFilter));
    OnPropertyChanged(nameof(FilteredTime));
  }

  private void Redraw() {
    if (!initialized_ || visibleArea_.Width <= 0) {
      return;
    }

    UpdateSizes();

    // Wait for sample slices to be computed.
    if (sliceTask_ != null) {
      sliceTask_.WaitForTask();
    }

    if (slices_ == null || slices_.Count == 0) {
      return;
    }

    // Recompute the slices if zooming in/out, this will increase/reduces their number.
    //? TODO: This check is needed only when maxWidth_ changes.
    foreach (var list in slices_) {
      double scaledSliceWidth = maxWidth_ / list.MaxSlices;

      if (scaledSliceWidth > sliceWidth_ * 1.5) {
        double newWidth = Math.Min(DefaultSliceWidth,
                                   Math.Max(0.2, DefaultSliceWidth * (DefaultSliceWidth / scaledSliceWidth)));
        StartComputeSampleSlices(newWidth);
        return;
      }

      if (scaledSliceWidth < sliceWidth_ * 0.75) {
        double newWidth = Math.Min(DefaultSliceWidth,
                                   Math.Max(0.2, DefaultSliceWidth * (DefaultSliceWidth / scaledSliceWidth)));
        StartComputeSampleSlices(newWidth);
        return;
      }
    }

    // Start redrawing the visible part of the activity view.
    using var graphDC = visual_.RenderOpen();
    var area = new Rect(0, 0, visibleArea_.Width, visibleArea_.Height);
    graphDC.DrawRectangle(backColor_, null, area);

    if (hasFilter_) {
      DrawTimeRangeFilter(graphDC);
    }

    foreach (var list in slices_) {
      double scaledSliceWidth = maxWidth_ / list.MaxSlices;
      int startSlice = (int)(visibleArea_.Left / scaledSliceWidth);
      int endSlice = Math.Min((int)(visibleArea_.Right / scaledSliceWidth), list.Slices.Count);

      for (int i = startSlice; i < endSlice; i++) {
        var weight = list.Slices[i].Weight;
        double height = weight.Ticks / (double)list.MaxWeight.Ticks * maxSampleHeight_;

        if (height < double.Epsilon) {
          continue;
        }

        // Mark the slice that's under the mouse cursor.
        var backColor = sampleBackColor_;
        var borderColor = sampleBorderColor_;

        if (showPositionLine_ && !startedSelection_) {
          if (positionLineX_ + visibleArea_.Left > i * scaledSliceWidth &&
              positionLineX_ + visibleArea_.Left < (i + 1) * scaledSliceWidth) {
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
    }

    foreach (var samples in markedSamples_) {
      DrawMarkedSamples(samples, graphDC);
    }

    // Draw selected samples on top of marked samples.
    if (selectedSamples_ != null) {
      DrawSelectedSamples(selectedSamples_, graphDC);
    }

    if (IsTimeBarVisible) {
      DrawTimeBar(graphDC);
    }

    // Draw filter overlays and selection.
    if (hasFilter_ || !isThreadIncluded_) {
      DrawExcludedTimeRangeFilter(graphDC);
    }

    if (startedSelection_ || hasSelection_) {
      DrawSelection(graphDC);
    }

    if (showPositionLine_ && !startedSelection_) {
      DrawPositionLine(graphDC);
    }
  }

  private void DrawTimeRangeFilter(DrawingContext graphDC) {
    double startX = TimeToPosition(filterStartTime_);
    double endX = TimeToPosition(filterEndTime_);

    if (startX > visibleArea_.Width) {
      return;
    }

    startX = Math.Max(0, startX);
    endX = Math.Min(endX, visibleArea_.Width);
    double width = Math.Max(1, endX - startX);
    var rect = new Rect(startX, 0, width, maxHeight_);
    graphDC.DrawRectangle(filteredBackColor_, null, rect);
  }

  private void DrawPositionLine(DrawingContext graphDC) {
    var lineStart = new Point(positionLineX_, 0);
    var lineEnd = new Point(positionLineX_, maxHeight_);
    graphDC.DrawLine(positionLinePen_, lineStart, lineEnd);

    var time = PositionToTime(positionLineX_);
    double textY = topMargin_ + 2;
    string text = "";
    var slice = TimeToSlice(time);
    var timeDiff = endTime_ - startTime_;

    if (slice.HasValue) {
      double cpuUsage = EstimateCpuUsage(slice.Value, slices_[0].TimePerSlice, samplingInterval_);
      text += $"{time.AsTimeStringWithMilliseconds(timeDiff)} ms ({cpuUsage:F2} cores)";
    }
    else {
      text = time.AsTimeStringWithMilliseconds(timeDiff);
    }

    DrawText(text, positionLineX_, textY, Brushes.Black, graphDC, true, backColor_, sampleBorderColor_,
             HorizontalAlignment.Center, VerticalAlignment.Center);

    var markedSamples = FindMarkedSamples(time);

    if (markedSamples != null) {
      string markedText = markedSamples.Node.FunctionName;
      DrawText(markedText, positionLineX_, topMargin_, Brushes.Black, graphDC, true, backColor_, sampleBorderColor_,
               HorizontalAlignment.Center, VerticalAlignment.Center);
    }
  }

  private MarkedSamples FindMarkedSamples(TimeSpan time) {
    var closeTimeDiff = TimeSpan.FromMilliseconds(1);
    var querySample = new SampleIndex(0, time);

    foreach (var markedSamples in markedSamples_) {
      int index = markedSamples.Samples.BinarySearch(querySample,
                                                     Comparer<SampleIndex>.Create((a, b) => {
                                                       var timeDiff = a.Time - startTime_ - b.Time;

                                                       if (timeDiff > closeTimeDiff) {
                                                         return 1;
                                                       }

                                                       if (timeDiff < -closeTimeDiff) {
                                                         return -1;
                                                       }

                                                       return 0;
                                                     }));

      if (index >= 0) {
        return markedSamples;
      }
    }

    return null;
  }

  private void DrawSelection(DrawingContext graphDC) {
    double startX = TimeToPosition(selectionStartTime_);
    double endX = TimeToPosition(selectionEndTime_);

    if (endX < startX) {
      // Right-to-left mouse selection, ensure start < end.
      (startX, endX) = (endX, startX);
    }

    if (startX > visibleArea_.Width) {
      return;
    }

    double selectionWidth = Math.Max(1, endX - startX);
    double selectionHeight = maxHeight_;
    var rect = new Rect(startX, 0, selectionWidth, selectionHeight);
    graphDC.DrawRectangle(selectionBackColor_, selectionBorderColor_, rect);

    if (startedSelection_) {
      var time = TimeSpan.FromTicks(Math.Abs((selectionEndTime_ - selectionStartTime_).Ticks));
      double textX = startX + selectionWidth / 2;
      double textY = topMargin_ + 2;

      var timeDiff = endTime_ - startTime_;
      string text = time.AsTimeStringWithMilliseconds(timeDiff);
      DrawText(text, textX, textY, Brushes.Black, graphDC, true, backColor_, sampleBorderColor_,
               HorizontalAlignment.Center, VerticalAlignment.Center);
    }
  }

  private void DrawExcludedTimeRangeFilter(DrawingContext graphDC) {
    if (isThreadIncluded_) {
      double startX = TimeToPosition(filterStartTime_);
      double endX = TimeToPosition(filterEndTime_);

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

  private void DrawSelectedSamples(List<SampleIndex> samples, DrawingContext graphDC) {
    double y = visibleArea_.Height - MarkerHeight;
    DrawMarkedSamplesImpl(samples, markerBackColor_, null, topMargin_, maxSampleHeight_, graphDC);
  }

  private void DrawMarkedSamples(MarkedSamples samples, DrawingContext graphDC) {
    double y = visibleArea_.Height - MarkerHeight;
    y = Math.Max(0, y - samples.Index * (MarkerHeight / 2));
    DrawMarkedSamplesImpl(samples.Samples, samples.Style.BackColor, sampleBorderColor_, y, MarkerHeight, graphDC);
  }

  private void DrawMarkedSamplesImpl(List<SampleIndex> samples, Brush backColor, Pen borderColor,
                                     double y, double height, DrawingContext graphDC) {
    double startX = double.MaxValue;
    double endX = double.MaxValue;
    bool first = true;

    foreach (ref var sample in CollectionsMarshal.AsSpan(samples)) {
      double x = TimeToPosition(sample.Time - startTime_);

      if (x < 0)
        continue;
      if (x >= visibleArea_.Width)
        break;

      if (x - endX > 1) {
        double width = Math.Max(2, Math.Ceiling(endX - startX));
        var rect = new Rect(startX, y, width, height);
        graphDC.DrawRectangle(backColor, borderColor, rect);
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
      var rect = new Rect(startX, y, endX - startX, height);
      graphDC.DrawRectangle(backColor, borderColor, rect);
    }
  }

  private Task StartComputeSampleSlices(double newWidth) {
    sliceTask_ ??= new CancelableTaskInstance();
    sliceTask_.CreateTask();

    return Task.Run(() => {
      sliceTask_.CancelTaskAndWait();
      sliceWidth_ = newWidth;
      slices_ = ComputeSampleSlices(profile_, ThreadId);
      sliceTask_.CompleteTask();

      // Update UI.
      Dispatcher.BeginInvoke(() => {
        Redraw();
        OnPropertyChanged(nameof(MaxCpuUsage));
      });
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
    startX = currentSec * secondsTickDist / secondsPerTick;

    for (double x = startX; x < endX; x += secondsTickDist) {
      var tickRect = new Rect(x - visibleArea_.Left, visibleArea_.Top, 3, 4);
      graphDC.DrawRectangle(Brushes.Black, null, tickRect);
      DrawText(TimeSpan.FromSeconds(currentSec).AsTimeString(timeDiff),
               tickRect.Left, tickRect.Top + TextMarginY, secTextColor, graphDC, false);

      int subTicks = (int)(secondsTickDist / MinTickDistance);
      if (subTicks > 1 && subTicks % 2 == 0)
        subTicks--;

      double subTickDist = secondsTickDist / (subTicks + 1);
      double timePerSubTick = secondsPerTick * 1000.0 / (subTicks + 1);
      double msEndX = Math.Min(secondsTickDist - subTickDist, endX);
      double currentMs = timePerSubTick;

      for (double y = subTickDist; y <= msEndX; y += subTickDist) {
        var msTickRect = new Rect(x + y - visibleArea_.Left, visibleArea_.Top, 2, 3);
        graphDC.DrawRectangle(Brushes.DimGray, null, msTickRect);
        double time = currentSec + currentMs / 1000;

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
          string timeStr = string.Format("{0:0." + new string('0', digits + 1) + "}", time);
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
    if (visibleArea_.Width <= 0) {
      return;
    }

    var glyphInfo = glyphs_.GetGlyphs(text);
    var textMargin = new Thickness(2, 4, 4, 0);

    if (horizontalAlign == HorizontalAlignment.Center) {
      x = x - glyphInfo.TextWidth / 2;
    }

    if (verticalAlign == VerticalAlignment.Center) {
      y = y + maxSampleHeight_ / 2 - glyphInfo.TextHeight / 2;
    }

    //? TODO: Extra check for this situation that seems to happen
    //? on some machine but couldn't be reproduced...
    if (textMargin.Left >= visibleArea_.Width - glyphInfo.TextWidth) {
      return;
    }

    if (keepInView) {
      x = Math.Clamp(x, textMargin.Left, visibleArea_.Width - glyphInfo.TextWidth);
    }

    if (backColor != null) {
      double boxWidth = glyphInfo.TextWidth + textMargin.Right;
      double boxHeight = glyphInfo.TextHeight + textMargin.Bottom;

      var textRect = new Rect(x - textMargin.Left, y - textMargin.Top,
                              boxWidth, boxHeight);
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
    long ticks = (long)((visibleArea_.Left + positionX) / maxWidth_ * timeDiff.Ticks);
    return TimeSpan.FromTicks(ticks);
  }

  private double TimeToPosition(TimeSpan time) {
    var timeDiff = endTime_ - startTime_;
    return time.Ticks / (double)timeDiff.Ticks * maxWidth_ - visibleArea_.Left;
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
    long ticks = slices_[0].TimePerSlice.Ticks;
    if (ticks == 0) return null;

    int sliceIndex = (int)(time.Ticks / ticks);

    if (sliceIndex >= slices_[0].Slices.Count) {
      return null;
    }

    return slices_[0].Slices[sliceIndex];
  }

  private (int, int) TimeRangeToSampleIndex(TimeSpan startTime, TimeSpan endTime) {
    var timeRange = endTime - startTime;
    int startIndex = TimeToSampleIndex(startTime, timeRange);
    int endIndex = TimeToSampleIndexBack(endTime, timeRange);

    if (startIndex > endIndex) {
      (startIndex, endIndex) = (endIndex, startIndex);
    }

    return (startIndex, endIndex);
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
        break; // Early stop.
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
        for (int sampleIndex = slice.FirstSampleIndex + slice.SampleCount - 1; sampleIndex >= slice.FirstSampleIndex;
             sampleIndex--) {
          if (profile_.Samples[sampleIndex].Sample.Time <= queryTime) {
            if (!IsSingleThreadView || profile_.Samples[sampleIndex].Stack.Context.ThreadId == ThreadId) {
              return sampleIndex;
            }
          }
        }
      }

      if ((sliceIndex - i + 1) * slices_[0].TimePerSlice > timeRange) {
        break; // Early stop.
      }
    }

    return 0;
  }

  private double EstimateCpuUsage(Slice slice, TimeSpan timePerSlice, TimeSpan samplingInterval) {
    if (samplingInterval == TimeSpan.Zero) {
      return 0;
    }

    double samples100Percent = timePerSlice / samplingInterval;
    double usage = slice.SampleCount / samples100Percent;
    return usage;
  }

  public struct Slice {
    public TimeSpan Weight;
    public int FirstSampleIndex;
    public int SampleCount;

    public Slice(TimeSpan weight, int firstSampleIndex, int sampleCount) {
      Weight = weight;
      FirstSampleIndex = firstSampleIndex;
      SampleCount = sampleCount;
    }
  }

  public class SliceList {
    public SliceList(int threadId, int slices = 0) {
      ThreadId = threadId;
      Slices = new List<Slice>(slices);
      MaxWeight = TimeSpan.MinValue;
    }

    public int ThreadId { get; set; }
    public TimeSpan TotalWeight { get; set; }
    public TimeSpan MaxWeight { get; set; }
    public TimeSpan TimePerSlice { get; set; }
    public List<Slice> Slices { get; set; }
    public int MaxSlices { get; set; }
  }
}

public record SampleTimeRangeInfo(
  TimeSpan StartTime,
  TimeSpan EndTime,
  int StartSampleIndex,
  int EndSampleIndex,
  int ThreadId);

public record struct SampleTimePointInfo(TimeSpan Time, int SampleIndex, int ThreadId);

//? TODO: Use SampleIndex in SampleTimePointInfo/Range
public record struct SampleIndex(int Index, TimeSpan Time);