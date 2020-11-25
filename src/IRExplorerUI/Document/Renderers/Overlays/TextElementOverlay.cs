// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Windows;
using System.Windows.Media;
using IRExplorerCore.IR;
using System;

namespace IRExplorerUI.Document {
    public class TextElementOverlay : ElementOverlayBase {
        public TextElementOverlay(string text, double width, double height,
                                  double marginX = 2, double marginY = 2, 
                                  HorizontalAlignment alignmentX = HorizontalAlignment.Right,
                                  VerticalAlignment alignmentY = VerticalAlignment.Center,
                                  string toolTip = "") :
            base(width, height, marginX, marginY, alignmentX, alignmentY, toolTip) {
            Text = text;
        }

        public string Text { get; set; }

        public override void Draw(Rect elementRect, IRElement element, DrawingContext drawingContext) {
            
        }
    }
}
