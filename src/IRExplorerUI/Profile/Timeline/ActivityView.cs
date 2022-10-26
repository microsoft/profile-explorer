using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Diagnostics;
using IRExplorerCore.Utilities;

namespace IRExplorerUI.Profile;

public class ActivityView : FrameworkElement {
    class SliceList {
        public int ThreadId { get; set; }
        public TimeSpan TotalWeight { get; set; }
        public TimeSpan MaxWeight { get; set; }
        public List<TimeSpan> Slices { get; set; }

        public SliceList(int threadId) {
            ThreadId = threadId;
            Slices = new List<TimeSpan>();
            MaxWeight = TimeSpan.MinValue;
        }
    }

    const double MarginY = 2;
    const double SliceWidth = 8;
    const double MaxSampleHeight = 50;

    private DrawingVisual visual_;
    private ProfileData profile_;
    private double maxWidth_;
    private Rect visibleArea_;
    private List<SliceList> slices_;
    private bool showPositionLine_;
    private double positionLineX_;
    private bool startedSelection_;
    private bool hasSelection_;
    private double selectionStartX_;
    private double selectionEndX_;


    public ActivityView() {
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseLeftButtonUp += ActivityView_MouseLeftButtonUp;
        MouseMove += ActivityView_MouseMove;
        MouseLeave += ActivityView_MouseLeave;
    }

    private void ActivityView_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) {
        if (startedSelection_) {
            startedSelection_ = false;
            hasSelection_ = true;
            selectionEndX_ = e.GetPosition(this).X;
            ReleaseMouseCapture();
            Redraw();
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
            selectionEndX_ = e.GetPosition(this).X;
            Redraw();
        }
        else {
            showPositionLine_ = true;
            positionLineX_ = e.GetPosition(this).X;
            Redraw();
        }

    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        if (hasSelection_) {
            hasSelection_ = false;
        }

        startedSelection_ = true;
        selectionStartX_ = e.GetPosition(this).X;
        selectionEndX_ = selectionStartX_;
        CaptureMouse();
        Redraw();
    }

    public async Task Initialize(ProfileData profile, Rect visibleArea) {
        profile_ = profile;
        visibleArea_ = visibleArea;
        maxWidth_ = visibleArea.Width;
        visual_ = new DrawingVisual();
        visual_.Drawing?.Freeze();
        AddVisualChild(visual_);
        AddLogicalChild(visual_);

        slices_ = await Task.Run(() => ComputeSampleSlices(profile));
        Redraw();
    }

    private List<SliceList> ComputeSampleSlices(ProfileData profile, int threadId = -1) {
        if (profile.Samples.Count == 0) {
            return new List<SliceList>();
        }

        var start = profile.Samples[0].Sample.Time.Ticks;
        var end = profile.Samples[^1].Sample.Time.Ticks;

        double slices = maxWidth_ / SliceWidth;
        var timeDiff = TimeSpan.FromTicks(end - start);
        double timePerSlice = (double)timeDiff.Ticks / slices;
        var sliceSeriesDict = new Dictionary<int, SliceList>();

        var sw3 = Stopwatch.StartNew();

        foreach (var (sample, stack) in profile.Samples) {
            if(threadId != -1 && stack.Context.ThreadId != threadId) {
                continue;
            }

            var slice = (int)((sample.Time.Ticks - start) / timePerSlice);

            //int queryThreadId = stack.Context.ThreadId;
            int queryThreadId = 0;

            var sliceList = sliceSeriesDict.GetOrAddValue(queryThreadId,
                () => new SliceList(queryThreadId) {});

            if(slice < sliceList.Slices.Count) {
                sliceList.Slices[slice] += sample.Weight;
            } else {
                for(int i = sliceList.Slices.Count; i < slice; i++) {
                    sliceList.Slices.Add(TimeSpan.Zero);
                }

                sliceList.Slices.Add(sample.Weight);
            }

            sliceList.TotalWeight += sample.Weight;
            sliceList.MaxWeight = TimeSpan.FromTicks(Math.Max(sliceList.MaxWeight.Ticks, 
                                                              sliceList.Slices[slice].Ticks));
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
        using var graphDC = visual_.RenderOpen();
        graphDC.DrawRectangle(Brushes.Azure, null, visibleArea_);

        if (slices_ == null) {
            return;
        }
        
        // lay on top technique

        foreach (var list in slices_) {
            var sliceWidth = maxWidth_ / list.Slices.Count;

            for(int i = 0; i < list.Slices.Count; i++) {
                var weight = list.Slices[i];

                double height = ((double)weight.Ticks / (double)list.MaxWeight.Ticks) * MaxSampleHeight;

                if (i * sliceWidth + sliceWidth < visibleArea_.Left) {
                    continue;
                }
                else if (i * sliceWidth > visibleArea_.Right) {
                    break;
                }

                var rect = new Rect(i * sliceWidth - visibleArea_.Left, 
                                    MaxSampleHeight - height + MarginY, 
                                    sliceWidth, height);
                graphDC.DrawRectangle(Brushes.DodgerBlue, ColorPens.GetPen(Colors.Black) , rect);
            }
        }

        if (showPositionLine_) {
            var lineStart = new Point(positionLineX_, 0);
            var lineEnd = new Point(positionLineX_, MaxSampleHeight + MarginY);
            graphDC.DrawLine(ColorPens.GetBoldPen(Colors.Gold), lineStart, lineEnd);
            //? text ms
        }

        if (startedSelection_ || hasSelection_) {
            var startX = selectionStartX_;
            var endX = selectionEndX_;
            if (endX < startX) {
                (startX, endX) = (endX, startX);
            }

            var selectionWidth = Math.Max(1, endX - startX);
            var rect = new Rect(startX, 0, selectionWidth, MaxSampleHeight + MarginY);
            var opacity = startedSelection_ ? 150 : 100;
            graphDC.DrawRectangle(ColorBrushes.GetTransparentBrush(Colors.Gold, opacity), ColorPens.GetPen(Colors.Black), rect);
        }
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
}