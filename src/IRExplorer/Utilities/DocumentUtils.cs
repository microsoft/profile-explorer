// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using CoreLib.IR;
using ICSharpCode.AvalonEdit;

namespace Client.Utilities {
    public static class DocumentUtils {
        public static IRElement FindElement(int offset, List<IRElement> list) {
            if (list == null) {
                return null;
            }

            //? TODO: Use binary search
            foreach (var token in list) {
                if (offset >= token.TextLocation.Offset &&
                    offset < token.TextLocation.Offset + token.TextLength) {
                    return token;
                }
            }

            return null;
        }

        public static bool FindElement(int offset, List<IRElement> list, out IRElement result) {
            result = FindElement(offset, list);
            return result != null;
        }

        public static IRElement FindPointedElement(Point position, TextEditor editor, List<IRElement> list) {
            int offset = GetOffsetFromMousePosition(position, editor, out _);
            return offset != -1 ? FindElement(offset, list) : null;
        }

        public static int GetOffsetFromMousePosition(Point positionRelativeToTextView, TextEditor editor,
                                                     out int visualColumn) {
            visualColumn = 0;
            var textView = editor.TextArea.TextView;
            var pos = positionRelativeToTextView;

            if (pos.Y < 0) {
                pos.Y = 0;
            }

            if (pos.Y > textView.ActualHeight) {
                pos.Y = textView.ActualHeight;
            }

            pos += textView.ScrollOffset;

            if (pos.Y >= textView.DocumentHeight) {
                pos.Y = textView.DocumentHeight - 0.01;
            }

            var line = textView.GetVisualLineFromVisualTop(pos.Y);

            if (line != null) {
                visualColumn = line.GetVisualColumn(pos, false);
                return line.GetRelativeOffset(visualColumn) + line.FirstDocumentLine.Offset;
            }

            return -1;
        }

        public static FormattedText CreateFormattedText(FrameworkElement element, string text, Typeface typeface,
                                                  double? emSize, Brush foreground, FontWeight? fontWeight = null) {

            var formattedText = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                                                  typeface, emSize.Value, foreground, null,
                                                  TextOptions.GetTextFormattingMode(element),
                                                  VisualTreeHelper.GetDpi(element).PixelsPerDip);
            if (fontWeight.HasValue)
            {
                formattedText.SetFontWeight(fontWeight.Value);
            }

            return formattedText;
        }
    }
}
