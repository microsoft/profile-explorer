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

    const double SliceWidth = 8;
    const double TimeBarHeight = 18;
    const double TopMarginY = 1 + TimeBarHeight;
    const double BottomMarginY = 12;
    const double DefaultTextSize = 11;
    
    private DrawingVisual visual_;
    private GlyphRunCache glyphs_;
    private ProfileData profile_;
    private double sampleHeight_;
    private double maxWidth_;
    private Rect visibleArea_;
    private List<SliceList> slices_;
    private bool showPositionLine_;
    private double positionLineX_;
    private bool startedSelection_;
    private bool hasSelection_;
    private TimeSpan selectionStartTime_;
    private TimeSpan selectionEndTime_;
    private Typeface font_;
    private double fontSize_;
    private TimeSpan startTime_;
    private TimeSpan endTime_;

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
            selectionEndTime_ = PositionToTime(e.GetPosition(this).X);
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
            selectionEndTime_ = PositionToTime(e.GetPosition(this).X);
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
        selectionStartTime_ = PositionToTime(e.GetPosition(this).X);
        selectionEndTime_ = selectionStartTime_;
        CaptureMouse();
        Redraw();
    }

    public async Task Initialize(ProfileData profile, Rect visibleArea) {
        profile_ = profile;
        visibleArea_ = visibleArea;
        maxWidth_ = visibleArea.Width;
        sampleHeight_ = visibleArea_.Height - TopMarginY - BottomMarginY;
        visual_ = new DrawingVisual();
        visual_.Drawing?.Freeze();
        AddVisualChild(visual_);
        AddLogicalChild(visual_);

        font_ = new Typeface("Segoe UI");
        fontSize_ = DefaultTextSize;
        glyphs_ = new GlyphRunCache(font_, fontSize_, VisualTreeHelper.GetDpi(visual_).PixelsPerDip);
        
        slices_ = await Task.Run(() => ComputeSampleSlices(profile));
        Redraw();
    }

    private List<SliceList> ComputeSampleSlices(ProfileData profile, int threadId = -1) {
        if (profile.Samples.Count == 0) {
            return new List<SliceList>();
        }

        startTime_ = profile.Samples[0].Sample.Time;
        endTime_ = profile.Samples[^1].Sample.Time;
        double slices = maxWidth_ / SliceWidth;
        var timeDiff = endTime_ - startTime_;
        double timePerSlice = (double)timeDiff.Ticks / slices;
        var sliceSeriesDict = new Dictionary<int, SliceList>();

        var sw3 = Stopwatch.StartNew();

        foreach (var (sample, stack) in profile.Samples) {
            if(threadId != -1 && stack.Context.ThreadId != threadId) {
                continue;
            }

            var slice = (int)((sample.Time - startTime_).Ticks / timePerSlice);

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
        var area = new Rect(0, 0, visibleArea_.Width, visibleArea_.Height);
        graphDC.DrawRectangle(Brushes.WhiteSmoke, null, area);

        if (slices_ == null) {
            return;
        }
        
        // lay on top technique

        foreach (var list in slices_) {
            var sliceWidth = maxWidth_ / list.Slices.Count;

            for(int i = 0; i < list.Slices.Count; i++) {
                var weight = list.Slices[i];

                double height = ((double)weight.Ticks / (double)list.MaxWeight.Ticks) * sampleHeight_;

                if (i * sliceWidth + sliceWidth < visibleArea_.Left) {
                    continue;
                }
                else if (i * sliceWidth > visibleArea_.Right) {
                    break;
                }

                var rect = new Rect(i * sliceWidth - visibleArea_.Left,
                    sampleHeight_ - height + TopMarginY, 
                                    sliceWidth, height);
                graphDC.DrawRectangle(Brushes.DodgerBlue, ColorPens.GetPen(Colors.Black) , rect);
            }
        }

        DrawTimeBar(graphDC);

        if (startedSelection_ || hasSelection_) {
            var startX = TimeToPosition(selectionStartTime_);
            var endX = TimeToPosition(selectionEndTime_);

            if (endX < startX) {
                (startX, endX) = (endX, startX);
            }

            var selectionWidth = Math.Max(1, endX - startX);
            var selectionHeight = sampleHeight_ + TopMarginY;
            var rect = new Rect(startX, 0, selectionWidth, selectionHeight);
            var opacity = startedSelection_ ? 120 : 100;
            graphDC.DrawRectangle(ColorBrushes.GetTransparentBrush(Colors.Gold, opacity), ColorPens.GetPen(Colors.Black), rect);

            var time = TimeSpan.FromTicks(Math.Abs((selectionEndTime_ - selectionStartTime_).Ticks));
            var textX = startX + selectionWidth / 2;
            var textY = selectionHeight + BottomMarginY / 2 - 3;
            DrawCenteredText(time.AsMillisecondsString(), textX, textY, Brushes.Black, graphDC);
        }
        else if (showPositionLine_) {
            var lineStart = new Point(positionLineX_, 0);
            var lineEnd = new Point(positionLineX_, sampleHeight_ + TopMarginY);
            graphDC.DrawLine(ColorPens.GetBoldPen(Colors.Gold), lineStart, lineEnd);

            var time = PositionToTime(positionLineX_);
            var textY = sampleHeight_ + TopMarginY + BottomMarginY / 2 - 3;
            DrawCenteredText(time.AsMillisecondsString(), positionLineX_, textY, Brushes.Black, graphDC);
            //? text ms
        }
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

    private void DrawTimeBar(DrawingContext graphDC) {
        const double MinTickDistance = 35;
        const double MinSecondTickDistance = 40;
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
            //if (x >= currentSec) {
                var tickRect = new Rect(x - visibleArea_.Left, visibleArea_.Top, 3, 4);
                graphDC.DrawRectangle(Brushes.Black, null, tickRect);
                DrawCenteredText($"{(int)Math.Round(currentSec)}s", tickRect.Left, tickRect.Top + TextMarginY, secTextColor, graphDC);
            //}

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
                    DrawCenteredText($"{time:0.0}", msTickRect.Left, msTickRect.Top + TextMarginY, msTextColor, graphDC);
                }
                else if (subTicks <= 100) {
                    DrawCenteredText($"{time:0.00}", msTickRect.Left, msTickRect.Top + TextMarginY, msTextColor, graphDC);
                }
                else {
                    int digits = (int)Math.Ceiling(Math.Log10(subTicks));
                    var timeStr = String.Format("{0:0." + new string('0', digits) + "}", time);
                    DrawCenteredText(timeStr, msTickRect.Left, msTickRect.Top + TextMarginY, msTextColor, graphDC);
                }

                currentMs += timePerSubTick;
            }

            currentSec += secPerTick;
        }
    }
    
    private void DrawCenteredText(string text, double x, double y, Brush color, DrawingContext dc) {
        var glyphInfo = glyphs_.GetGlyphs(text);
        x = x - glyphInfo.TextWidth / 2;
        y = y + glyphInfo.TextHeight / 2;
        dc.PushTransform(new TranslateTransform(x, y));
        dc.DrawGlyphRun(color, glyphInfo.Glyphs);
        dc.Pop();
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