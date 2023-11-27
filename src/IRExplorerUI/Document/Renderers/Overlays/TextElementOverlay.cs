// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System.Windows;
using System.Windows.Media;
using IRExplorerCore.IR;

namespace IRExplorerUI.Document;

public sealed class TextElementOverlay : ElementOverlayBase {
  public TextElementOverlay(string text, double width, double height,
                            double marginX = 2, double marginY = 2,
                            HorizontalAlignment alignmentX = HorizontalAlignment.Right,
                            VerticalAlignment alignmentY = VerticalAlignment.Center,
                            string toolTip = "") :
    base(width, height, marginX, marginY, alignmentX, alignmentY, text, toolTip) {
  }

  public override void Draw(Rect elementRect, IRElement element,
                            IElementOverlay previousOverlay, DrawingContext drawingContext) {
  }
}
