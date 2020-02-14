// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Windows;
using Core.IR;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Rendering;

namespace Client.Utilities {
    public static class DocumentUtils {
        public static IRElement FindElement(int offset, List<IRElement> list) {
            if (list == null) {
                return null;
            }

            //? TODO: Use binary search
            foreach (var token in list) {
                if (offset >= token.TextLocation.Offset &&
                    offset < (token.TextLocation.Offset + token.TextLength)) {
                    return token;
                }
            }

            return null;
        }

        public static bool FindElement(int offset, List<IRElement> list,
                                       out IRElement result) {
            result = FindElement(offset, list);
            return result != null;
        }

        public static IRElement FindPointedElement(Point position, TextEditor editor, List<IRElement> list) {
            int offset = GetOffsetFromMousePosition(position, editor, out _);

            if (offset != -1) {
                return FindElement(offset, list);
            }

            return null;
        }

        public static int GetOffsetFromMousePosition(Point positionRelativeToTextView,
                                                     TextEditor editor, out int visualColumn) {
            visualColumn = 0;
            var textView = editor.TextArea.TextView;
            Point pos = positionRelativeToTextView;

            if (pos.Y < 0)
                pos.Y = 0;
            if (pos.Y > textView.ActualHeight)
                pos.Y = textView.ActualHeight;
            pos += textView.ScrollOffset;
            if (pos.Y >= textView.DocumentHeight)
                pos.Y = textView.DocumentHeight - 0.01;
            VisualLine line = textView.GetVisualLineFromVisualTop(pos.Y);

            if (line != null) {
                visualColumn = line.GetVisualColumn(pos, false);
                return line.GetRelativeOffset(visualColumn) + line.FirstDocumentLine.Offset;
            }

            return -1;
        }
    }

}
