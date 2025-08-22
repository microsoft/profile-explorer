// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Windows;
using System.Windows.Media;
using ProfileExplorerCore2.IR;

namespace ProfileExplorer.UI.Document;

public sealed class TextElementOverlay : ElementOverlayBase {
  public TextElementOverlay(string text, double width, double height,
                            double marginX = 2, double marginY = 2,
                            HorizontalAlignment alignmentX = HorizontalAlignment.Right,
                            VerticalAlignment alignmentY = VerticalAlignment.Center,
                            string toolTip = "") :
    base(width, height, marginX, marginY, alignmentX, alignmentY, text, toolTip) {
  }

  public override void Draw(Rect elementRect, IRElement element, Typeface font,
                            IElementOverlay previousOverlay, double horizontalOffset,
                            DrawingContext drawingContext) {
  }
}