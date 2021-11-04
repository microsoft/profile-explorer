// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Rendering;

namespace IRExplorerUI {
    public sealed class CurrentLineHighlighter : IBackgroundRenderer {
        private Brush backgroundBrush_;
        private TextEditor editor_;

        public CurrentLineHighlighter(TextEditor editor, Color backColor) {
            editor_ = editor;

            //? TODO: Right now it appears on top of the selected element colors,
            //? should be rendered before and only the border on top
            if (backColor != Colors.Transparent) {
                backgroundBrush_ = backColor.AsBrush();
            }
            else {
                backgroundBrush_ = ColorBrushes.GetBrush(Colors.LightGray);
            }
        }

        public KnownLayer Layer => KnownLayer.Background;

        public void Draw(TextView textView, DrawingContext drawingContext) {
            if (textView.Document == null || textView.Document.TextLength == 0) {
                return;
            }

            // Draw a border around the current line.
            textView.EnsureVisualLines();
            var currentLine = textView.Document.GetLineByOffset(editor_.CaretOffset);

            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, currentLine)) {
                var lineRect = Utils.SnapRectToPixels(rect.X, rect.Y,
                                                      textView.ActualWidth, rect.Height);
                drawingContext.DrawRectangle(backgroundBrush_, null, lineRect);
            }
        }
    }
}
