// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Core.IR;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;
using ProtoBuf;

namespace Client {
    public enum BookmarkSegmentKind
    {
        Bookmark,
        OptimizationRemark,
        AnalysisRemark
    }

    [ProtoContract]
    public class BookmarkSegment : IRSegment {
        [ProtoMember(1)]
        public Bookmark Bookmark { get; set; }
        [ProtoMember(2)]
        public BookmarkSegmentKind Kind { get; set; }
        public Rect Bounds { get; set; }
        public Rect PinButtonBounds { get; set; }
        public Rect RemoveButtonBounds { get; set; }
        public bool IsHovered { get; set; }
        public bool IsNearby { get; set; }

        public bool IsSelected {
            get => Bookmark.IsSelected;
            set => Bookmark.IsSelected = value;
        }

        public bool IsPinned {
            get => Bookmark.IsPinned;
            set => Bookmark.IsPinned = value;
        }

        public bool IsExpanded => IsSelected || IsPinned || IsHovered;
        public bool HasText => !string.IsNullOrWhiteSpace(Bookmark.Text);

        public BookmarkSegment() : base(null) { }

        public BookmarkSegment(Bookmark bookmark, BookmarkSegmentKind kind) : base(bookmark.Element) {
            Bookmark = bookmark;
            Kind = kind;
        }
    }


    [ProtoContract]
    public class DocumentMarginState {
        [ProtoMember(1)]
        public List<ElementGroupState> blockGroups_;
        [ProtoMember(2)]
        public List<BookmarkSegment> bookmarkSegments_;
        [ProtoMember(3)]
        public BookmarkSegment hoveredBookmark_;
        [ProtoMember(4)]
        public BookmarkSegment selectedBookmark_;

        public DocumentMarginState() {
            blockGroups_ = new List<ElementGroupState>();
            bookmarkSegments_ = new List<BookmarkSegment>();
        }

        public bool HasAnnotations => blockGroups_.Count > 0 ||
                                      bookmarkSegments_.Count > 0;

    }


    public class DocumentMargin : AbstractMargin {
        class IconDrawing {
            public ImageSource Icon { get; set; }
            public double Proportion { get; set; }
        }

        private const int NearbyBookmarkDistance = 100;
        private const int NearbyBookmarkExtraWidth = 16;

        private const int ButtonSectionWidth = 32;
        private const int ButtonTextPadding = 8;
        private static readonly int MarginWidth = 16;
        private static readonly int ButtonWidth = 16;
        private static readonly int ButtonIconWidth = 12;

        private static readonly Typeface DefaultFont = new Typeface("Consolas");
        private static readonly HighlightingStyle defaultBookmarkStyle_ =
            new HighlightingStyle(Colors.LemonChiffon, Pens.GetPen(Colors.Silver));

        private static readonly HighlightingStyle selectedBookmarkStyle_ =
            new HighlightingStyle(Colors.PeachPuff, Pens.GetPen(Colors.Black));

        private static readonly HighlightingStyle pinnedBookmarkStyle_ =
            new HighlightingStyle(Colors.PapayaWhip, Pens.GetPen(Colors.Black));

        private static readonly HighlightingStyle pinButtonStyle_ =
            new HighlightingStyle(Colors.Silver, Pens.GetPen(Colors.Black));

        private static readonly HighlightingStyle hoverBookmarkStyle_ =
            new HighlightingStyle(Colors.LightBlue, Pens.GetPen(Colors.Black));

        private static readonly HighlightingStyle nearbyBookmarkStyle_ =
            new HighlightingStyle(Colors.LightBlue, Pens.GetPen(Colors.Silver));

        private IconDrawing bookmarkIcon_;
        private IconDrawing removeIcon_;
        private IconDrawing pinIcon_;
        private IconDrawing optimizationRemarkIcon_;
        private IconDrawing analysisRemarkIcon_;
        private SolidColorBrush backgroundBrush_;

        private List<HighlightedSegmentGroup> blockGroups_;
        private HashSet<IRElement> blockElements_;
        private TextSegmentCollection<BookmarkSegment> bookmarkSegments_;
        private BookmarkSegment hoveredBookmark_;
        private BookmarkSegment selectedBookmark_;
        private HashSet<BookmarkSegment> nearbyBookmarks_;

        public DocumentMargin(Color backgroundColor) {
            BackgroundColor = backgroundColor;
            blockGroups_ = new List<HighlightedSegmentGroup>();
            blockElements_ = new HashSet<IRElement>();
            bookmarkSegments_ = new TextSegmentCollection<BookmarkSegment>();
            nearbyBookmarks_ = new HashSet<BookmarkSegment>();

            bookmarkIcon_ = LoadIconResource("MarkerIcon");
            removeIcon_ = LoadIconResource("RemoveIcon");
            pinIcon_ = LoadIconResource("PinIcon");
            optimizationRemarkIcon_ = LoadIconResource("ZapIcon");
            analysisRemarkIcon_ = LoadIconResource("DotIcon");
            Version = 1;
        }

        public DocumentMarginState SaveState() {
            var marginState = new DocumentMarginState();
            marginState.blockGroups_ = StateSerializer.SaveElementGroupState(blockGroups_);
            marginState.bookmarkSegments_ = bookmarkSegments_.ToList();
            marginState.hoveredBookmark_ = hoveredBookmark_;
            marginState.selectedBookmark_ = selectedBookmark_;
            return marginState;
        }

        public void LoadState(DocumentMarginState state) {
            blockGroups_ = StateSerializer.LoadElementGroupState(state.blockGroups_);
            hoveredBookmark_ = state.hoveredBookmark_;
            selectedBookmark_ = state.selectedBookmark_;

            bookmarkSegments_ = new TextSegmentCollection<BookmarkSegment>();
            state.bookmarkSegments_.ForEach((item) =>
            {
                if (item.Kind == BookmarkSegmentKind.Bookmark)
                {
                    AddBookmark(item.Bookmark);
                }
                else
                {
                    AddRemarkBookmark(item.Bookmark, item.Kind);
                }
            });
        }

        public int Version { get; set; }
        public bool DisableBlockRemoval { get; set; }

        public Bookmark SelectedBookmark => selectedBookmark_ != null ? selectedBookmark_.Bookmark : null;
        public List<HighlightedSegmentGroup> BlockGroups => blockGroups_;

        public Color BackgroundColor {
            get => backgroundBrush_.Color;
            set {
                backgroundBrush_ = ColorBrushes.GetBrush(value);
                InvalidateVisual();
            }
        }

        public event EventHandler<Bookmark> BookmarkRemoved;
        public event EventHandler<Bookmark> BookmarkChanged;

        private IconDrawing LoadIconResource(string name) {
            var icon = Application.Current.Resources[name] as ImageSource;

            return new IconDrawing() {
                Icon = icon,
                Proportion = icon.Width / icon.Height
            };
        }

        public void AddBlock(HighlightedGroup group, bool saveToFile = true) {
            foreach (var block in group.Elements) {
                RemoveBlock(block);
                blockElements_.Add(block);
            }

            blockGroups_.Add(new HighlightedSegmentGroup(group, saveToFile));
            Version++;
        }

        public void AddBookmark(Bookmark bookmark) {
            bookmarkSegments_.Add(new BookmarkSegment(bookmark, BookmarkSegmentKind.Bookmark));
            Version++;
        }

        public void AddRemarkBookmark(Bookmark bookmark, RemarkKind kind)
        {
            var segmentKind = kind switch
            {
                RemarkKind.Optimization => BookmarkSegmentKind.OptimizationRemark,
                RemarkKind.Analysis => BookmarkSegmentKind.AnalysisRemark
            };

            AddRemarkBookmark(bookmark, segmentKind);
        }

        void AddRemarkBookmark(Bookmark bookmark, BookmarkSegmentKind segmentKind)
        {
            bookmarkSegments_.Add(new BookmarkSegment(bookmark, segmentKind));
            Version++;
        }

        public void RemoveBookmark(Bookmark bookmark) {
            BookmarkSegment segment = null;

            foreach (var value in bookmarkSegments_) {
                if (value.Bookmark == bookmark) {
                    segment = value;
                    break;
                }
            }

            if (segment != null) {
                bookmarkSegments_.Remove(segment);
                Version++;

            }
        }

        public void RemoveBlock(IRElement block) {
            if (DisableBlockRemoval) {
                return;
            }

            if (!blockElements_.Contains(block)) {
                return;
            }

            int index = blockGroups_.FindIndex((e) => e.Group.Elements.Contains(block));

            if (index != -1) {
                if (blockGroups_[index].Segments.Count == 1) {
                    blockGroups_.RemoveAt(index);
                }
                else {
                    var segments = blockGroups_[index].Segments;
                    IRSegment blockSegment = null;

                    foreach (var segment in segments) {
                        if (segment.Element == block) {
                            blockSegment = segment;
                            break;
                        }
                    }

                    if (blockSegment != null) {
                        segments.Remove(blockSegment);
                        Version++;
                    }
                }
            }
        }

        public void CopyFrom(DocumentMargin other) {
            foreach (var item in other.blockGroups_) {
                blockGroups_.Add(new HighlightedSegmentGroup(item.Group));
            }

            foreach (var item in other.bookmarkSegments_) {
                bookmarkSegments_.Add(new BookmarkSegment(item.Bookmark, item.Kind));
            }
        }

        public void ClearMarkers() {
            blockGroups_.Clear();
            Version++;
        }

        public void ClearBookmarks() {
            bookmarkSegments_.Clear();
            Version++;
        }

        public void ForEachBlockElement(Action<IRElement> action) {
            blockGroups_.ForEach((group) => {
                group.Group.Elements.ForEach(action);
            });
        }

        public void ForEachBookmark(Action<Bookmark> action)
        {
            foreach(var segment in bookmarkSegments_)
            {
                action(segment.Bookmark);
            }
        }

        public void MouseMoved() {
            HandleMouseMoved(Mouse.GetPosition(this));
        }

        private void HandleMouseMoved(Point point) {
            // Test only with visible bookmarks.
            BookmarkSegment newHoveredBookmark = null;
            bool needsRedrawing = false;

            if (FindVisibleText(out int viewStart, out int viewEnd)) {
                foreach (var segment in bookmarkSegments_.FindOverlappingSegments(viewStart, viewEnd - viewStart)) {
                    if (newHoveredBookmark == null) {
                        if (segment.Bounds.Contains(point) ||
                            segment.PinButtonBounds.Contains(point) ||
                            segment.RemoveButtonBounds.Contains(point)) {
                            newHoveredBookmark = segment;
                            continue;
                        }
                    }

                    if (!segment.IsExpanded) {
                        var distance = (segment.Bounds.TopLeft - point).Length;
                        var onSameLine = point.Y >= segment.Bounds.Top &&
                                         point.Y <= segment.Bounds.Bottom;

                        if (distance < NearbyBookmarkDistance && onSameLine) {
                            if (!nearbyBookmarks_.Contains(segment)) {
                                nearbyBookmarks_.Add(segment);
                                segment.IsNearby = true;
                                needsRedrawing = true;
                            }
                        }
                        else if (nearbyBookmarks_.Contains(segment)) {
                            segment.IsNearby = false;
                            nearbyBookmarks_.Remove(segment);
                            needsRedrawing = true;
                        }
                    }
                }
            }

            if (newHoveredBookmark != null) {
                if (newHoveredBookmark != hoveredBookmark_) {
                    // Replace hovered bookmark.
                    if (hoveredBookmark_ != null) {
                        UnselectHoveredBookmark();
                    }

                    newHoveredBookmark.IsHovered = true;
                    hoveredBookmark_ = newHoveredBookmark;
                    needsRedrawing = true;
                }

                if (nearbyBookmarks_.Contains(newHoveredBookmark)) {
                    // Remove from nearby bookmarks.
                    newHoveredBookmark.IsNearby = false;
                    nearbyBookmarks_.Remove(newHoveredBookmark);
                    needsRedrawing = true;
                }
            }
            else if (hoveredBookmark_ != null &&
                     !(hoveredBookmark_.IsSelected || hoveredBookmark_.IsPinned)) {
                UnselectHoveredBookmark();
                needsRedrawing = true;
            }

            if (needsRedrawing) {
                InvalidateVisual();
            }
        }

        protected override HitTestResult HitTestCore(PointHitTestParameters hitParams) {
            HandleMouseMoved(Mouse.GetPosition(this));
            return new PointHitTestResult(this, hitParams.HitPoint);
        }

        enum BookmarkSegmentElement {
            Bookmark,
            PinButton,
            RemoveButton
        }

        private Tuple<BookmarkSegment, BookmarkSegmentElement> HitTestBookmarks() {
            if (!FindVisibleText(out int viewStart, out int viewEnd)) {
                return null;
            }

            var point = Mouse.GetPosition(this);

            foreach (var segment in bookmarkSegments_.FindOverlappingSegments(viewStart, viewEnd - viewStart)) {
                if (segment.PinButtonBounds.Contains(point)) {
                    return new Tuple<BookmarkSegment, BookmarkSegmentElement>(segment, BookmarkSegmentElement.PinButton);
                }
                else if (segment.RemoveButtonBounds.Contains(point)) {
                    return new Tuple<BookmarkSegment, BookmarkSegmentElement>(segment, BookmarkSegmentElement.RemoveButton);
                }
                else if (segment.Bounds.Contains(point))
                {
                    return new Tuple<BookmarkSegment, BookmarkSegmentElement>(segment, BookmarkSegmentElement.Bookmark);
                }
            }

            return null;
        }

        public bool HasHoveredBookmark() {
            return HitTestBookmarks() != null;
        }

        protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e) {
            var result = HitTestBookmarks();

            if (result != null) {
                var segment = result.Item1;
                var element = result.Item2;

                if (element == BookmarkSegmentElement.Bookmark) {
                    UnselectBookmark();
                    SelectBookmark(segment);
                    InvalidateVisual();

                    BookmarkChanged?.Invoke(this, segment.Bookmark);
                    e.Handled = true;
                    return;
                }
                else if (element == BookmarkSegmentElement.PinButton) {
                    UnselectBookmark();
                    SelectBookmark(segment);
                    segment.IsPinned = !segment.IsPinned;
                    segment.IsHovered = !segment.IsPinned;
                    InvalidateVisual();

                    BookmarkChanged?.Invoke(this, segment.Bookmark);
                    e.Handled = true;
                    return;
                }
                else if (element == BookmarkSegmentElement.RemoveButton) {
                    BookmarkRemoved?.Invoke(this, segment.Bookmark);
                }
            }

            UnselectBookmark();
            base.OnPreviewMouseLeftButtonDown(e);
        }

        private void UnselectHoveredBookmark() {
            if (hoveredBookmark_ != null) {
                hoveredBookmark_.IsHovered = false;
                hoveredBookmark_ = null;
            }
        }

        private void SelectBookmark(BookmarkSegment segment) {
            UnselectHoveredBookmark();
            selectedBookmark_ = segment;
            selectedBookmark_.IsSelected = true;
        }

        public void SelectBookmark(Bookmark bookmark) {
            foreach (var segment in bookmarkSegments_) {
                if (segment.Bookmark == bookmark) {
                    UnselectBookmark();
                    SelectBookmark(segment);
                    break;
                }
            }
        }

        public void UnselectBookmark() {
            UnselectHoveredBookmark();

            if (selectedBookmark_ != null) {
                selectedBookmark_.IsSelected = false;
                selectedBookmark_ = null;
                InvalidateVisual();
            }
        }

        public void SelectedBookmarkChanged() {
            InvalidateVisual();
        }

        protected override Size MeasureOverride(Size availableSize) {
            return new Size(MarginWidth, 0);
        }

        private FormattedText CreateFormattedText(FrameworkElement element, string text, 
                                                  Typeface typeface, double? emSize, Brush foreground) {
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

            //formattedText.SetFontWeight(FontWeights.DemiBold);
            return formattedText;
        }

        protected override void OnTextViewChanged(TextView oldTextView, TextView newTextView) {
            // Register to get notified when the visual lines are changing
            // so that the margin can be redrawn.
            if (oldTextView != null) {
                oldTextView.VisualLinesChanged -= VisualLinesChanged;
            }

            if (newTextView != null) {
                newTextView.VisualLinesChanged += VisualLinesChanged;
            }

            base.OnTextViewChanged(oldTextView, newTextView);
            InvalidateVisual();
        }

        private void VisualLinesChanged(object sender, EventArgs e) {
            InvalidateVisual();
        }

        private void DrawIcon(IconDrawing icon, double x, double y, double size,
                              double availableSize, DrawingContext drawingContext) {
            double height = size;
            double width = height * icon.Proportion;
            drawingContext.DrawImage(icon.Icon,
                            new Rect(x + availableSize - width - (availableSize - width) / 2,
                                     y, width, height));
        }

        private HighlightingStyle GetBookmarkStyle(BookmarkSegment segment) {
            var style = defaultBookmarkStyle_;

            if (segment.Bookmark.IsSelected) {
                style = selectedBookmarkStyle_;
            }
            else if (segment.Bookmark.IsPinned) {
                style = pinnedBookmarkStyle_;
            }
            else if (segment.IsHovered) {
                style = hoverBookmarkStyle_;
            }
            else if (segment.IsNearby) {
                style = nearbyBookmarkStyle_;
            }

            // Combine user style background with default pen.
            if (segment.Bookmark.Style != null)
            {
                //if (style == defaultBookmarkStyle_ &&
                //    segment.Kind != BookmarkSegmentKind.Bookmark)
                //{
                //    style = new HighlightingStyle(Brushes.Transparent, null);
                //}
                //else
                //{
                //    style = new HighlightingStyle(segment.Bookmark.Style.BackColor, style.Border);
                //}

                style = new HighlightingStyle(segment.Bookmark.Style.BackColor, style.Border);
            }

            return style;
        }

        private HighlightingStyle GetPinButtonStyle(BookmarkSegment segment) {
            if (segment.IsPinned) {
                return pinButtonStyle_;
            }

            return GetBookmarkStyle(segment);
        }

        bool FindVisibleText(out int viewStart, out int viewEnd) {
            TextView.EnsureVisualLines();
            var visualLines = TextView.VisualLines;

            if (visualLines.Count == 0) {
                viewStart = viewEnd = 0;
                return false;
            }

            viewStart = visualLines[0].FirstDocumentLine.Offset;
            viewEnd = visualLines[visualLines.Count - 1].LastDocumentLine.EndOffset;
            return true;
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            // Draw margin background.
            drawingContext.DrawRectangle(backgroundBrush_, null,
                                            new Rect(0, 0, RenderSize.Width, RenderSize.Height));

            // Draw highlighted blocks.
            if (!FindVisibleText(out int viewStart, out int viewEnd))
            {
                return;
            }

            foreach (var group in blockGroups_)
            {
                DrawGroup(group, TextView, drawingContext, viewStart, viewEnd);
            }

            // Draw bookmarks in two steps, first the unselected ones, then the selected ones.
            // This is done so that the borders of the selected ones are not drawn over by other 
            // segments with a different border color.
            var lineHeight = TextView.DefaultLineHeight;
            var segments = bookmarkSegments_.FindOverlappingSegments(viewStart, viewEnd - viewStart);

            foreach (var segment in segments) {
                if (!(segment.IsSelected || segment.IsHovered || segment.IsPinned))
                {
                    DrawSegment(segment, lineHeight, drawingContext);
                }
            }

            foreach (var segment in segments) {
                if (segment.IsSelected || segment.IsHovered || segment.IsPinned)
                {
                    DrawSegment(segment, lineHeight, drawingContext);
                }
            }
        }

        void DrawGroup(HighlightedSegmentGroup group, TextView textView,
                        DrawingContext drawingContext, int viewStart, int viewEnd)
        {
            Size renderSize = this.RenderSize;

            foreach (var result in group.Segments.FindOverlappingSegments(viewStart, viewEnd - viewStart))
            {
                foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, result))
                {
                    drawingContext.DrawRectangle(group.BackColor, null,
                        new Rect(renderSize.Width - MarginWidth, Math.Floor(rect.Top),
                                    MarginWidth, Math.Ceiling(rect.Height)));
                }
            }
        }

        private void DrawSegment(BookmarkSegment segment, double lineHeight, DrawingContext drawingContext)
        {
            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(TextView, segment))
            {
                double y = rect.Top;
                var style = GetBookmarkStyle(segment);
                Rect bounds;
                Rect pinBounds;
                Rect removeBounds;

                //if (segment.Kind == BookmarkSegmentKind.AnalysisRemark)
                //{
                //    var vn = UTCRemarkParser.ExtractVN(segment.Bookmark.Element);
                //    if (vn != null)
                //    {
                //        //? Click on a VN would mark all other identical VNs
                //        var text = CreateFormattedText(this, $"VN {vn}", DefaultFont, 12, Brushes.Black);
                //        drawingContext.DrawText(text, new Point(4, y));
                //    }
                //}

                if (segment.HasText && segment.IsExpanded)
                {
                    var text = CreateFormattedText(this, segment.Bookmark.Text,
                                                    DefaultFont, 12, Brushes.Black);
                    var baseWidth = RenderSize.Width + ButtonSectionWidth;
                    bounds = new Rect(0, y - 1, text.Width + baseWidth + 2 * ButtonTextPadding, lineHeight);
                    drawingContext.DrawRectangle(style.BackColor, style.Border, bounds);
                    drawingContext.DrawText(text, new Point(baseWidth + ButtonTextPadding, y));
                }
                else
                {
                    double nearbyExtraWidth = segment.IsNearby || segment.IsExpanded ? DocumentMargin.NearbyBookmarkExtraWidth : 0;
                    bounds = new Rect(0, y - 1, RenderSize.Width + nearbyExtraWidth, lineHeight);
                    drawingContext.DrawRectangle(style.BackColor, style.Border, bounds);
                }


                var icon = SelectBookmarkIcon(segment);

                DrawIcon(icon, 0, y, lineHeight - 2, RenderSize.Width, drawingContext);

                if (segment.IsExpanded)
                {
                    var buttonBounds = new Rect(MarginWidth, y - 1, ButtonSectionWidth, lineHeight);
                    removeBounds = DrawBookmarkButton(removeIcon_, buttonBounds, style, drawingContext);
                    var pinStyle = GetPinButtonStyle(segment);
                    pinBounds = DrawBookmarkButton(pinIcon_, buttonBounds, pinStyle, drawingContext, removeBounds.Width);
                }

                segment.Bounds = bounds; // Update bounds for later hit testing.
                segment.PinButtonBounds = pinBounds;
                segment.RemoveButtonBounds = removeBounds;
                return; // Stop after drawing it once.
            }
        }

        private IconDrawing SelectBookmarkIcon(BookmarkSegment segment)
        {
            return segment.Kind switch
            {
                BookmarkSegmentKind.Bookmark => bookmarkIcon_,
                BookmarkSegmentKind.OptimizationRemark => optimizationRemarkIcon_,
                BookmarkSegmentKind.AnalysisRemark => analysisRemarkIcon_
            };
        }

        private Rect DrawBookmarkButton(IconDrawing icon, Rect startBounds,
                                        HighlightingStyle pinStyle, DrawingContext drawingContext,
                                        double extraLeftSpace = 0) {
            Rect bounds = new Rect(startBounds.Left + extraLeftSpace, startBounds.Top,
                                   ButtonWidth, startBounds.Height);
            drawingContext.DrawRectangle(pinStyle.BackColor, pinStyle.Border, bounds);
            DrawIcon(icon, bounds.Left + 1, bounds.Top + 2, ButtonIconWidth,
                     ButtonIconWidth, drawingContext);
            return bounds;
        }
    }
}
