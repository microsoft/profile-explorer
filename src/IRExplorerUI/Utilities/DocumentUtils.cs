// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Rendering;
using IRExplorerCore.Analysis;
using IRExplorerCore.IR;

namespace IRExplorerUI.Utilities {
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
                                                  double emSize, Brush foreground, FontWeight? fontWeight = null) {

            var formattedText = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                                                  typeface, emSize, foreground, null,
                                                  TextOptions.GetTextFormattingMode(element),
                                                  VisualTreeHelper.GetDpi(element).PixelsPerDip);
            if (fontWeight.HasValue) {
                formattedText.SetFontWeight(fontWeight.Value);
            }

            return formattedText;
        }

        public static GlyphRun CreateGlyphRun(string text, Typeface typeface, double emSize, 
                                              Point origin, float pixelsPerDip) {
            GlyphTypeface glyphTypeface;
            if (!typeface.TryGetGlyphTypeface(out glyphTypeface)) {
                throw new InvalidOperationException();
            }

            ushort[] glyphIndexes = new ushort[text.Length];
            double[] advanceWidths = new double[text.Length];
            double totalWidth = 0;

            for (int n = 0; n < text.Length; n++) {
                ushort glyphIndex = glyphTypeface.CharacterToGlyphMap[text[n]];
                glyphIndexes[n] = glyphIndex;

                double width = glyphTypeface.AdvanceWidths[glyphIndex] * emSize;
                advanceWidths[n] = width;

                totalWidth += width;
            }

            return new GlyphRun(glyphTypeface, 0, false, emSize, pixelsPerDip,
                glyphIndexes, origin, advanceWidths, null, null, null, null, null, null);
        }

        public static bool FindVisibleText(TextView textView, out int viewStart, out int viewEnd) {
            textView.EnsureVisualLines();
            var visualLines = textView.VisualLines;

            if (visualLines.Count == 0) {
                viewStart = viewEnd = 0;
                return false;
            }

            viewStart = visualLines[0].FirstDocumentLine.Offset;
            viewEnd = visualLines[^1].LastDocumentLine.EndOffset;
            return true;
        }

        public static ReferenceFinder CreateReferenceFinder(FunctionIR function, ISession session,
                                                            DocumentSettings settings) {
            var irInfo = session.CompilerInfo.IR;
            IReachableReferenceFilter filter = null;

            if (settings.FilterSourceDefinitions ||
                settings.FilterDestinationUses) {
                filter = irInfo.CreateReferenceFilter(function);
                filter.FilterUses = settings.FilterDestinationUses;
                filter.FilterDefinitions = settings.FilterSourceDefinitions;
            }

            return new ReferenceFinder(function, irInfo, filter);
        }
    }
}
