// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Windows;
using System.Windows.Media;
using ProfileExplorer.Core.IR;
using ProtoBuf;

namespace ProfileExplorer.UI.Document;

[ProtoContract(SkipConstructor = true)]
public sealed class IconElementOverlay : ElementOverlayBase {
  public IconElementOverlay() {
    // Used by deserialization.
  }

  public IconElementOverlay(IconDrawing icon, double width, double height,
                            string label, string tooltip,
                            HorizontalAlignment alignmentX, VerticalAlignment alignmentY,
                            double marginX, double marginY) :
    base(width, height, marginX, marginY, alignmentX, alignmentY, label, tooltip) {
    Icon = icon;
  }

  [ProtoMember(1)]
  public IconDrawing Icon { get; set; }

  public static IconElementOverlay
    CreateDefault(IconDrawing icon, double width, double height,
                  Brush backColor, Brush selectedBackColor, Pen border,
                  string label = "", string tooltip = "",
                  HorizontalAlignment alignmentX = HorizontalAlignment.Right,
                  VerticalAlignment alignmentY = VerticalAlignment.Center,
                  double marginX = 4, double marginY = 4, double padding = 2) {
    return new IconElementOverlay(icon, width, height,
                                  label, tooltip,
                                  alignmentX, alignmentY,
                                  marginX, marginY) {
      Background = backColor,
      SelectedBackground = selectedBackColor,
      Border = border,
      ShowBackgroundOnMouseOverOnly = true,
      ShowBorderOnMouseOverOnly = true,
      ShowLabelOnMouseOverOnly = true,
      UseLabelBackground = true,
      Padding = padding,
      AllowLabelEditing = true
    };
  }

  public override void Draw(Rect elementRect, IRElement element, Typeface font,
                            IElementOverlay previousOverlay, double horizontalOffset,
                            DrawingContext drawingContext) {
    double x = ComputePositionX(elementRect, previousOverlay, horizontalOffset);
    double y = ComputePositionY(elementRect, previousOverlay);
    double opacity = ActiveOpacity;
    Bounds = Utils.SnapRectToPixels(x, y, ActualWidth, ComputeHeight(elementRect));
    double iconHeight = Bounds.Height;

    if (ShowLabel) {
      if (Icon == null) {
        Bounds = Utils.SnapRectToPixels(Bounds.X + Bounds.Width, Bounds.Y, 0, Bounds.Height);
      }

      Bounds = DrawLabel(Bounds, font, opacity, drawingContext);
    }

    if (Icon != null) {
      DrawBackground(Bounds, opacity, drawingContext);
      Icon.Draw(x + 1, y - 1, Width, Width, iconHeight, opacity, drawingContext);
    }

    // For debugging, border around whole element.
    // drawingContext.DrawRectangle(ColorBrushes.Transparent, ColorPens.GetPen(Colors.Red), elementRect);
  }
}