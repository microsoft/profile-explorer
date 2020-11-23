// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Rendering;

namespace IRExplorerUI {
    public sealed class CurrentLineHighlighter : IBackgroundRenderer {
        private Brush backgroundBrush_;
        private Pen borderPen_;
        private TextEditor editor_;

        public CurrentLineHighlighter(TextEditor editor, Brush backgroundBrush = null, Pen borderPen = null) {
            editor_ = editor;

            if (borderPen != null) {
                borderPen_ = borderPen;
            }
            else {
                borderPen_ = Pens.GetPen(Colors.Gray);
            }

            if (backgroundBrush != null) {
                backgroundBrush_ = backgroundBrush;
            }
            else {
                backgroundBrush_ = Brushes.Transparent;
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
                                                      textView.ActualWidth, rect.Height + 1);
                drawingContext.DrawRectangle(backgroundBrush_, borderPen_, lineRect);
            }
        }
    }
}
