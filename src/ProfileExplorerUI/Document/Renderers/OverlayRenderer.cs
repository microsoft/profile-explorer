// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using ProfileExplorer.Core.IR;
using ProfileExplorer.Core.IR.Tags;
using ProtoBuf;

namespace ProfileExplorer.UI.Document;

[ProtoContract]
public class ElementOverlayState {
  [ProtoMember(1)]
  public List<Tuple<IRElementReference, List<ElementOverlayBase>>> Overlays;

  public ElementOverlayState() {
    Overlays = new List<Tuple<IRElementReference, List<ElementOverlayBase>>>();
  }
}

public sealed class OverlayRenderer : Canvas, IBackgroundRenderer {
  private TextView textView_;
  private ElementHighlighter highlighter_;
  private ConnectedElement rootConnectedElement_;
  private List<ConnectedElement> connectedElements_;
  private TextSegmentCollection<IRSegment> conntectedSegments_;
  private Dictionary<IRElement, IRSegment> connectedSegmentMap_;
  private Dictionary<IRElement, IROverlaySegment> overlaySegmentMap_;
  private TextSegmentCollection<IROverlaySegment> overlaySegments_;
  private IElementOverlay hoveredOverlay_;
  private IElementOverlay selectedOverlay_;
  private ToolTip hoverTooltip_;
  private IElementOverlay tooltipOverlay_;
  private bool updateSuspended_;

  public OverlayRenderer(ElementHighlighter highlighter) {
    overlaySegments_ = new TextSegmentCollection<IROverlaySegment>();
    overlaySegmentMap_ = new Dictionary<IRElement, IROverlaySegment>();
    SnapsToDevicePixels = true;
    IsHitTestVisible = true;
    Background = ColorBrushes.Transparent; // Needed for mouse events to fire...
    highlighter_ = highlighter;
    ClearConnectedElements();
  }

  public int Version { get; set; }
  public Typeface TextFont { get; set; }
  public KnownLayer Layer => KnownLayer.Background;

  public void Draw(TextView textView, DrawingContext drawingContext) {
    if (updateSuspended_) {
      return;
    }

    textView_ = textView;
    Width = textView.RenderSize.Width;
    Height = textView.RenderSize.Height;
    Children.Clear();

    if (textView.Document == null || textView.Document.TextLength == 0) {
      return;
    }

    var visual = new DrawingVisual();
    var overlayDC = visual.RenderOpen();

    // Query and draw visible segments from each group.
    foreach (var group in highlighter_.Groups) {
      DrawGroup(group, textView, overlayDC);
    }

    Tuple<IElementOverlay, IRElement, Rect> hoverSegment = null;
    Tuple<IElementOverlay, IRElement, Rect> selectedSegment = null;
    IElementOverlay hoverPrevOverlay = null;
    IElementOverlay selectedPrevOverlay = null;

    foreach (var segment in overlaySegments_.FindOverlappingSegments(textView_)) {
      bool isBlockElement = segment.Element is BlockIR;
      IElementOverlay prevOverlay = null;

      foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment)) {
        foreach (var overlay in segment.Overlays) {
          // Draw hover/selected overlay last so that it shows up on top
          // in case there is some overlap between overlays.
          if (overlay == hoveredOverlay_) {
            hoverSegment = new Tuple<IElementOverlay, IRElement, Rect>(overlay, segment.Element, rect);
            hoverPrevOverlay = prevOverlay;
            prevOverlay = overlay;
            continue;
          }

          if (overlay == selectedOverlay_) {
            selectedSegment = new Tuple<IElementOverlay, IRElement, Rect>(overlay, segment.Element, rect);
            selectedPrevOverlay = prevOverlay;
            prevOverlay = overlay;
            continue;
          }

          overlay.Draw(rect, segment.Element, TextFont, prevOverlay,
                       textView.HorizontalOffset, overlayDC);
          prevOverlay = overlay;
        }

        // For blocks, consider only the first line, otherwise the overlay
        // would be applied to each each tuple in the block.
        if (isBlockElement) {
          break;
        }
      }
    }

    if (selectedSegment != null) {
      selectedSegment.Item1.Draw(selectedSegment.Item3, selectedSegment.Item2, TextFont,
                                 selectedPrevOverlay, textView.HorizontalOffset, overlayDC);
    }

    if (hoverSegment != null) {
      hoverSegment.Item1.Draw(hoverSegment.Item3, hoverSegment.Item2, TextFont,
                              hoverPrevOverlay, textView.HorizontalOffset, overlayDC);
    }

    double dotSize = 3;
    //var dotBackground = ColorBrushes.GetTransparentBrush(Colors.DarkRed, 255);
    //var dotPen = ColorPens.GetTransparentPen(Colors.DarkRed, 255);
    bool first = true;

    //? TODO: Disabled, used from IRDocument
    // Draw extra annotations for remarks in the same context.
    foreach (var connectedElement in connectedElements_) {
      var prevSegmentRect = GetRemarkSegmentRect(rootConnectedElement_.Element, textView);
      var segmentRect = GetRemarkSegmentRect(connectedElement.Element, textView);

      double horizontalOffset = 4;
      double verticalOffset = prevSegmentRect.Height / 2;

      var startPoint =
        Utils.SnapPointToPixels(prevSegmentRect.Right + horizontalOffset, prevSegmentRect.Top + verticalOffset);
      var endPoint = Utils.SnapPointToPixels(segmentRect.Right + horizontalOffset, segmentRect.Top + verticalOffset);

      var edgeStartPoint = startPoint;
      var edgeEndPoint = endPoint;

      double dx = endPoint.X - startPoint.X;
      double dy = endPoint.Y - startPoint.Y;
      var vect = new Vector(dy, -dx);
      var middlePoint = new Point(startPoint.X + dx / 2, startPoint.Y + dy / 2);

      double factor = FindBezierControlPointFactor(startPoint, endPoint);
      var controlPoint = middlePoint + -factor * vect;

      // Keep the control point in the horizontal bounds of the document.
      if (controlPoint.X < 0 || controlPoint.X > Width) {
        controlPoint = new Point(Math.Clamp(controlPoint.X, 0, Width), controlPoint.Y);
      }

      //overlayDC.DrawLine(ColorPens.GetPen(Colors.Green, 2), startPoint, middlePoint);
      //overlayDC.DrawLine(ColorPens.GetPen(Colors.Green, 2), middlePoint, endPoint);

      var startOrientation = FindArrowOrientation(new[] {edgeEndPoint, edgeStartPoint, controlPoint}, out var _);
      var orientation = FindArrowOrientation(new[] {edgeStartPoint, controlPoint, edgeEndPoint}, out var _);

      edgeStartPoint = edgeStartPoint + startOrientation * (dotSize - 1);
      edgeEndPoint = edgeEndPoint - orientation * dotSize * 2;

      var edgeGeometry = new StreamGeometry();
      var edgeSC = edgeGeometry.Open();
      edgeSC.BeginFigure(edgeStartPoint, false, false);
      edgeSC.BezierTo(edgeStartPoint, controlPoint, edgeEndPoint, true, false);
      DrawEdgeArrow(new[] {edgeStartPoint, controlPoint, edgeEndPoint}, edgeSC);

      //edgeSC.BeginFigure(edgeStartPoint, false, false);
      //edgeSC.LineTo(edgeEndPoint, true, false);

      //edgeSC.BeginFigure(startPoint, false, false);
      //edgeSC.LineTo(endPoint, true, false);

      // overlayDC.DrawLine(ColorPens.GetPen(Colors.Red, 2), startPoint, endPoint);
      //overlayDC.DrawLine(ColorPens.GetPen(Colors.Red, 2), point, point2);

      // overlayDC.DrawEllipse(connectedElement.Style.BackColor, connectedElement.Style.Border, endPoint, dotSize, dotSize);

      if (first) {
        overlayDC.DrawEllipse(rootConnectedElement_.Style.BackColor, rootConnectedElement_.Style.Border, startPoint,
                              dotSize, dotSize);
        first = false;
      }

      edgeSC.Close();
      edgeGeometry.Freeze();
      overlayDC.DrawGeometry(connectedElement.Style.BackColor, connectedElement.Style.Border, edgeGeometry);

      //var text = DocumentUtils.CreateFormattedText(textView, i.ToString(), DefaultFont, 11, Brushes.Black);
      //overlayDC.DrawText(text, Utils.SnapPointToPixels(startPoint.X - text.Width / 2, startPoint.Y - text.Height / 2));
    }

    overlayDC.Close();
    Add(visual);

    if (hoverSegment != null) {
      ShowTooltip(hoverSegment.Item1);
    }
  }

  public void AddElementOverlay(IRElement element, IElementOverlay overlay,
                                bool prepend = false) {
    overlay.Element = element;
    Version++;

    if (overlaySegmentMap_.TryGetValue(element, out var segment)) {
      if (prepend) {
        segment.Overlays.Insert(0, overlay);
      }
      else {
        segment.Overlays.Add(overlay);
      }
    }
    else {
      segment = new IROverlaySegment(element, overlay);
      overlaySegments_.Add(segment);
      overlaySegmentMap_[element] = segment;
    }
  }

  public bool RemoveAllElementOverlays(IRElement element, object onlyWithTag = null) {
    if (!overlaySegmentMap_.TryGetValue(element, out var segment)) return false;

    if (onlyWithTag != null) {
      segment.Overlays.RemoveAll(overlay => overlay.Tag == onlyWithTag);
    }
    else {
      segment.Overlays.Clear();
    }

    if (segment.Overlays.Count == 0) {
      overlaySegmentMap_.Remove(element);
      overlaySegments_.Remove(segment);
    }

    Version++;
    return true;
  }

  public bool RemoveElementOverlay(IElementOverlay overlay) {
    if (!overlaySegmentMap_.TryGetValue(overlay.Element, out var segment)) return false;

    if (segment.Overlays.Remove(overlay)) {
      if (segment.Overlays.Count == 0) {
        overlaySegmentMap_.Remove(overlay.Element);
        overlaySegments_.Remove(segment);
      }

      Version++;
      return true;
    }

    return false;
  }

  public void ForEachElementOverlay(Action<IRElement, IROverlaySegment> action) {
    foreach (var pair in overlaySegmentMap_) {
      action(pair.Key, pair.Value);
    }
  }

  public void SetRootElement(IRElement element, HighlightingStyle style) {
    ClearConnectedElements();
    rootConnectedElement_ = new ConnectedElement(element, style);
    var segment = new IRSegment(element);
    conntectedSegments_.Add(segment);
    connectedSegmentMap_[element] = segment;
  }

  public void AddConnectedElement(IRElement element, HighlightingStyle style) {
    connectedElements_.Add(new ConnectedElement(element, style));
    var segment = new IRSegment(element);
    conntectedSegments_.Add(segment);
    connectedSegmentMap_[element] = segment;
  }

  public void ClearConnectedElements() {
    rootConnectedElement_ = null;
    connectedElements_ = new List<ConnectedElement>();
    conntectedSegments_ = new TextSegmentCollection<IRSegment>();
    connectedSegmentMap_ = new Dictionary<IRElement, IRSegment>();
  }

  public bool MouseClick(MouseEventArgs e) {
    return HandleMouseClicked(e.GetPosition(this), e);
  }

  public void MouseMoved(MouseEventArgs e) {
    HandleMouseMoved(e.GetPosition(this), e);
  }

  public new void MouseLeave() {
    HideTooltip();

    if (hoveredOverlay_ != null) {
      hoveredOverlay_.IsMouseOver = false;
      hoveredOverlay_ = null;
      textView_.Redraw();
    }
  }

  public bool KeyPressed(KeyEventArgs e) {
    if (selectedOverlay_ == null) return false;

    if (selectedOverlay_.KeyPressed(e)) {
      textView_.Redraw(); // Force refresh
      return true;
    }

    return false;
  }

  public void Clear() {
    Children.Clear();
    ClearConnectedElements();
    ClearElementOverlays();
    Version++;
  }

  public void ClearElementOverlays() {
    overlaySegments_ = new TextSegmentCollection<IROverlaySegment>();
    overlaySegmentMap_ = new Dictionary<IRElement, IROverlaySegment>();
  }

  public void Add(Visual drawingVisual) {
    // Hit testing and focus must be disabled to allow events to propagate properly.
    var visualHost = new VisualHost {Visual = drawingVisual};
    visualHost.IsHitTestVisible = false;
    visualHost.Focusable = false;
    Children.Add(visualHost);
  }

  public void Add(UIElement element) {
    Children.Add(element);
  }

  public ElementOverlayState SaveState(FunctionIR function) {
    var state = new ElementOverlayState();

    foreach (var pair in overlaySegmentMap_) {
      //? TODO: Casting to ElementOverlayBase is done to avoid issues when deserializing
      //? the IElementOverlay objects with protobuf-net.
      var savedOverlays = new List<ElementOverlayBase>();

      foreach (var overlay in pair.Value.Overlays) {
        if (overlay.SaveStateToFile) {
          savedOverlays.Add((ElementOverlayBase)overlay);
        }
      }

      if (savedOverlays.Count > 0) {
        state.Overlays.Add(new Tuple<IRElementReference, List<ElementOverlayBase>>(pair.Key, savedOverlays));
      }
    }

    return state;
  }

  public void LoadState(ElementOverlayState state, FunctionIR function,
                        Action<IElementOverlay> registerAction) {
    if (state == null) {
      return; // Most likely a file from an older version of the app.
    }

    foreach (var item in state.Overlays) {
      foreach (var overlay in item.Item2) {
        registerAction(overlay);
        AddElementOverlay(item.Item1, overlay);
      }
    }

    Version++;
  }

  public void SuspendUpdate() {
    updateSuspended_ = true;
  }

  public void ResumeUpdate() {
    updateSuspended_ = false;
  }

  private Rect GetRemarkSegmentRect(IRElement element, TextView textView) {
    var segment = connectedSegmentMap_[element];

    foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment)) {
      return rect;
    }

    // The segment is outside the visible document.
    // A visual line must be created for it, then extract the location.
    var line = textView.Document.GetLineByOffset(segment.StartOffset);
    var visualLine = textView.GetOrConstructVisualLine(line);
    var start = new TextViewPosition(textView.Document.GetLocation(segment.StartOffset));
    var end = new TextViewPosition(textView.Document.GetLocation(segment.StartOffset + segment.Length));
    int startColumn = visualLine.ValidateVisualColumn(start, false);
    int endColumn = visualLine.ValidateVisualColumn(end, false);

    foreach (var rect in BackgroundGeometryBuilder.
      GetRectsFromVisualSegment(textView, visualLine,
                                startColumn, endColumn)) {
      return rect;
    }

    return Rect.Empty;
  }

  private double FindBezierControlPointFactor(Point a, Point b) {
    const double startLength = 20;
    const double startFactor = 0.5;
    const double endLength = 1000;
    const double endFactor = 0.2;
    const double slope = (endFactor - startFactor) / (endLength - startLength);
    const double intercept = slope * startLength - startFactor;
    double distance = (a - b).Length;
    return Math.Clamp(distance * slope + intercept, endFactor, startFactor);
  }

  private void ShowTooltip(IElementOverlay overlay) {
    if (!(overlay is ElementOverlayBase {HasToolTip: true} elementOverlay)) {
      return;
    }

    if (hoverTooltip_ != null && tooltipOverlay_ == elementOverlay) {
      return; // Already showing the right tooltip.
    }

    tooltipOverlay_ = elementOverlay;
    hoverTooltip_ ??= new ToolTip();

    // Showing the tooltip from the Dispatcher somehow prevents
    // it from temporarily showing in the top-left screen corner
    // with an annoying flicker...
    Dispatcher.BeginInvoke(() => {
      hoverTooltip_.Placement = PlacementMode.Mouse;
      hoverTooltip_.PlacementTarget = textView_;
      hoverTooltip_.Content = elementOverlay.ToolTip;
      hoverTooltip_.IsOpen = true;
    });
  }

  private void HideTooltip() {
    if (hoverTooltip_ != null) {
      hoverTooltip_.IsOpen = false;
    }
  }

  private void DrawEdgeArrow(Point[] tempPoints, StreamGeometryContext sc) {
    // Draw arrow head with a slope matching the line,
    // this uses the last two points to find the angle.
    Point start;
    var v = FindArrowOrientation(tempPoints, out start);

    sc.BeginFigure(start + v * 5, true, true);
    double t = v.X;
    v.X = v.Y;
    v.Y = -t; // Rotate 90
    sc.LineTo(start + v * 5, true, true);
    sc.LineTo(start + v * -5, true, true);
  }

  private Vector FindArrowOrientation(Point[] tempPoints, out Point start) {
    for (int i = tempPoints.Length - 1; i > 0; i--) {
      start = tempPoints[i];
      var v = start - tempPoints[i - 1];

      if (v.LengthSquared != 0) {
        v.Normalize();
        return v;
      }
    }

    return new Vector(0, 0);
  }

  private void HandleMouseMoved(Point point, MouseEventArgs e) {
    if (overlaySegments_.Count == 0 || textView_ == null) {
      return;
    }

    IElementOverlay hoverOverlay = null;

    foreach (var segment in overlaySegments_.FindOverlappingSegments(textView_)) {
      if (hoverOverlay != null) {
        break;
      }

      foreach (var overlay in segment.Overlays) {
        if (overlay.CheckIsMouseOver(point)) {
          hoverOverlay = overlay;
          break;
        }
      }
    }

    if (hoverOverlay != hoveredOverlay_) {
      if (hoveredOverlay_ != null) {
        // Deselect previous overlay.
        hoveredOverlay_.IsMouseOver = false;
        hoveredOverlay_.HoveredEnded(e);
        hoveredOverlay_ = null;
      }

      if (hoverOverlay != null) {
        hoverOverlay.IsMouseOver = true;
        hoveredOverlay_ = hoverOverlay;
        hoveredOverlay_.Hovered(e);
      }

      HideTooltip();
      textView_.Redraw();
    }
  }

  private bool HandleMouseClicked(Point point, MouseEventArgs e) {
    if (overlaySegments_.Count == 0 || textView_ == null) {
      return false;
    }

    IElementOverlay hoverOverlay = null;
    bool redraw = false;

    foreach (var segment in overlaySegments_.FindOverlappingSegments(textView_)) {
      if (hoverOverlay != null) {
        break;
      }

      foreach (var overlay in segment.Overlays) {
        if (overlay.CheckIsMouseOver(point)) {
          hoverOverlay = overlay;
          break;
        }
      }
    }

    if (hoverOverlay != selectedOverlay_) {
      if (selectedOverlay_ != null) {
        // Deselect previous overlay.
        selectedOverlay_.IsSelected = false;
        selectedOverlay_ = null;
        redraw = true;
      }

      if (hoverOverlay != null) {
        selectedOverlay_ = hoverOverlay;
        selectedOverlay_.IsSelected = true;
        redraw = true;
      }
    }

    // Send click event to selected overlay.
    if (selectedOverlay_ != null) {
      if (selectedOverlay_.MouseClicked(e)) {
        redraw = true;
      }
    }

    if (redraw) {
      textView_.Redraw();
    }

    return hoverOverlay != null;
  }

  private void DrawGroup(HighlightedSegmentGroup group, TextView textView,
                         DrawingContext drawingContext) {
    IRElement element = null;
    double fontSize = App.Settings.DocumentSettings.FontSize;

    foreach (var segment in group.Segments.FindOverlappingSegments(textView_)) {
      element = segment.Element;

      foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment)) {
        var notesTag = element.GetTag<NotesTag>();

        if (notesTag != null) {
          string label = notesTag.Title;

          //? TODO: Show only on hover
          //if(notesTag.Notes.Count > 0)
          //{
          //    label += $", {notesTag.Notes[0]}";
          //}

          var pen = group.Border ?? ColorPens.GetPen(Colors.Gray);
          var text = DocumentUtils.CreateFormattedText(textView, label, TextFont,
                                                       fontSize, Brushes.Black);
          drawingContext.DrawRectangle(group.BackColor, pen,
                                       Utils.SnapRectToPixels(rect.X + rect.Width + 8, rect.Y,
                                                              text.Width + 10,
                                                              textView.DefaultLineHeight + 1));
          drawingContext.DrawText(text, Utils.SnapPointToPixels(rect.X + rect.Width + 12, rect.Y + 1));
        }
      }
    }
  }

  public sealed class IROverlaySegment : IRSegment {
    public IROverlaySegment(IRElement element, IElementOverlay overlay) : base(element) {
      Overlays = new List<IElementOverlay>();
      Overlays.Add(overlay);
    }

    public List<IElementOverlay> Overlays { get; set; }
  }

  private class VisualHost : FrameworkElement {
    public Visual Visual { get; set; }
    protected override int VisualChildrenCount => Visual != null ? 1 : 0;

    protected override Visual GetVisualChild(int index) {
      return Visual;
    }
  }

  private class ConnectedElement {
    public ConnectedElement(IRElement element, HighlightingStyle style) {
      Element = element;
      Style = style;
    }

    public IRElement Element { get; set; }
    public HighlightingStyle Style { get; set; }
  }
}