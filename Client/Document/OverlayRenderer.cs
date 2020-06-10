using Core.IR;
using ICSharpCode.AvalonEdit.Rendering;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Client.Document
{
    public class VisualHost : UIElement
    {
        public Visual Visual { get; set; }

        protected override int VisualChildrenCount
        {
            get { return Visual != null ? 1 : 0; }
        }

        protected override Visual GetVisualChild(int index)
        {
            return Visual;
        }
    }

    public class OverlayRenderer : Canvas, IBackgroundRenderer
    {
        public KnownLayer Layer => KnownLayer.Background;
        TextView textView_;
        ElementHighlighter highlighter_;

        public OverlayRenderer(ElementHighlighter highlighter)
        {
            SnapsToDevicePixels = true;
            Background = null;
            highlighter_ = highlighter;
            //MouseMove += OverlayRenderer_MouseMove;
        }

        //private void OverlayRenderer_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        //{
        //    if (!FindVisibleText(out int viewStart, out int viewEnd))
        //    {
        //        return;
        //    }

        //    var point = e.GetPosition(this);

        //    foreach (var group in highlighter_.Groups)
        //    {
        //        foreach(var segment in group.Segments.FindOverlappingSegments(viewStart, viewEnd - viewStart))
        //        {
        //            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView_, segment))
        //            {
        //                // check intersection
        //                // mark as hovered
        //                // redraw
        //            }
        //        }
        //    }
        //}

        public void Reset()
        {
            Children.Clear();
        }

        public void Add(Visual drawingVisual)
        {
            Children.Add(new VisualHost { Visual = drawingVisual });
        }

        public void Add(UIElement element)
        {
            Children.Add(element);
        }

        bool FindVisibleText(out int viewStart, out int viewEnd)
        {
            textView_.EnsureVisualLines();
            var visualLines = textView_.VisualLines;

            if (visualLines.Count == 0)
            {
                viewStart = viewEnd = 0;
                return false;
            }

            viewStart = visualLines[0].FirstDocumentLine.Offset;
            viewEnd = visualLines[visualLines.Count - 1].LastDocumentLine.EndOffset;
            return true;
        }

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            Width = textView.RenderSize.Width;
            Height = textView.RenderSize.Height;
            Reset();

            if (textView.Document == null)
            {
                return;
            }

            if (textView.Document.TextLength == 0)
            {
                return;
            }

            // Find start/end index of visible lines.
            textView.EnsureVisualLines();
            var visualLines = textView.VisualLines;

            if (visualLines.Count == 0)
            {
                return;
            }

            //? This doesn't consider elements not in the view,
            //? lazily create the visual 
            if(highlighter_.Groups.Count == 0)
            {
                return;
            }

            int viewStart = visualLines[0].FirstDocumentLine.Offset;
            int viewEnd = visualLines[visualLines.Count - 1].LastDocumentLine.EndOffset;

            var visual = new DrawingVisual();
            var overlayDC = visual.RenderOpen();

            // Query and draw visible segments from each group.
            foreach (var group in highlighter_.Groups)
            {
                DrawGroup(group, textView, overlayDC, viewStart, viewEnd);
            }

            overlayDC.Close();
            Add(visual);
        }

        private void DrawGroup(HighlightedSegmentGroup group,
                                      TextView textView, DrawingContext drawingContext,
                                      int viewStart, int viewEnd) {
            IRElement element = null;

            foreach (var segment in group.Segments.FindOverlappingSegments(viewStart, viewEnd - viewStart))
            {
                element = segment.Element;

                foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
                {
                    var notesTag = element.GetTag<Core.IR.Tags.NotesTag>();

                    if (notesTag != null)
                    {
                        var label = notesTag.Title;

                        //? TODO: Show only on hover
                        //if(notesTag.Notes.Count > 0)
                        //{
                        //    label += $", {notesTag.Notes[0]}";
                        //}

                        var text = CreateFormattedText(textView, label,
                                                    DefaultFont, 12, Brushes.Black);
                        var pen = group.Border ?? Pens.GetPen(Colors.Gray);
                        drawingContext.DrawRectangle(group.BackColor, pen,
                                                     new Rect(rect.X + rect.Width + 8, rect.Y, text.Width + 10, text.Height + 1));
                        drawingContext.DrawText(text, new Point(rect.X + rect.Width + 12, rect.Y + 1));
                    }
                }
            }
        }

        private static readonly Typeface DefaultFont = new Typeface("Consolas");

        private FormattedText CreateFormattedText(FrameworkElement element, string text,
                                                  Typeface typeface, double? emSize, Brush foreground)
        {

            if (element == null)
                throw new ArgumentNullException("element");
            if (text == null)
                throw new ArgumentNullException("text");
            if (typeface == null)
                typeface = DefaultFont;
            if (emSize == null)
                emSize = TextBlock.GetFontSize(element);
            if (foreground == null)
                foreground = TextBlock.GetForeground(element);
            var formattedText = new FormattedText(
                text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                emSize.Value,
                foreground,
                null,
                TextOptions.GetTextFormattingMode(element),
                VisualTreeHelper.GetDpi(element).PixelsPerDip
            );

            formattedText.SetFontWeight(FontWeights.DemiBold);
            return formattedText;
        }
    }
}
