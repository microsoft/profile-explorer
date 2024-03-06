// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Rendering;

namespace IRExplorerUI;

public sealed class CurrentLineHighlighter : IBackgroundRenderer {
  private TextEditor editor_;
  private Pen borderPen_;

  public CurrentLineHighlighter(TextEditor editor, Color borderColor) {
    editor_ = editor;
    borderPen_ = ColorPens.GetPen(borderColor, 1.5);
  }

  public KnownLayer Layer => KnownLayer.Background;

  public void Draw(TextView textView, DrawingContext drawingContext) {
    if (textView.Document == null || textView.Document.TextLength == 0) {
      return;
    }

    // Draw a border around the current line.
    textView.EnsureVisualLines();
    var currentLine = textView.Document.GetLineByOffset(editor_.CaretOffset);
    var width = textView.ActualWidth + textView.HorizontalOffset;

    foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, currentLine)) {
      var lineRect = Utils.SnapRectToPixels(rect.X, rect.Y, width, rect.Height);
      lineRect.Inflate(1, 1);
      drawingContext.DrawRectangle(null, borderPen_, lineRect);
    }
  }
}