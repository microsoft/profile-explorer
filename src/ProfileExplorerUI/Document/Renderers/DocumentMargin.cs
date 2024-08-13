// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;
using ProfileExplorer.Core.IR;
using ProfileExplorer.UI.Document;
using ProtoBuf;

namespace ProfileExplorer.UI;

public enum BookmarkSegmentKind {
  Bookmark,
  Remark
}

[ProtoContract]
public sealed class BookmarkSegment : IRSegment {
  public BookmarkSegment() : base(null) { }

  public BookmarkSegment(Bookmark bookmark, BookmarkSegmentKind kind, object tag = null) : base(
    bookmark.Element) {
    Bookmark = bookmark;
    Kind = kind;
    Tag = tag;
  }

  [ProtoMember(1)] public Bookmark Bookmark { get; set; }
  [ProtoMember(2)] public BookmarkSegmentKind Kind { get; set; }
  public Rect Bounds { get; set; }
  public Rect PinButtonBounds { get; set; }
  public Rect RemoveButtonBounds { get; set; }
  public bool IsHovered { get; set; }
  public bool IsTextHovered { get; set; }
  public bool IsNearby { get; set; }
  public object Tag { get; set; }

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
}

[ProtoContract]
public class DocumentMarginState {
  [ProtoMember(1)]
  public List<ElementGroupState> BlockGroups;
  [ProtoMember(2)]
  public List<BookmarkSegment> BookmarkSegments;
  [ProtoMember(3)]
  public BookmarkSegment HoveredBookmark;
  [ProtoMember(4)]
  public BookmarkSegment SelectedBookmark;

  public DocumentMarginState() {
    BlockGroups = new List<ElementGroupState>();
    BookmarkSegments = new List<BookmarkSegment>();
  }

  public bool HasAnnotations => BlockGroups.Count > 0 || BookmarkSegments.Count > 0;
}

public class DocumentMargin : AbstractMargin {
  private const int NearbyBookmarkDistance = 100;
  private const int NearbyBookmarkExtraWidth = 16;
  private const double HoveredPinnedBookmarkOpacity = 0.2;
  private const int ButtonSectionWidth = 32;
  private const int ButtonTextPadding = 8;
  private static readonly int MarginWidth = 16;
  private static readonly int ButtonWidth = 16;
  private static readonly int ButtonIconWidth = 12;
  private static readonly Typeface DefaultFont = new("Consolas");
  private static readonly HighlightingStyle defaultBookmarkStyle_ =
    new(Colors.LemonChiffon, ColorPens.GetPen(Colors.Silver));
  private static readonly HighlightingStyle selectedBookmarkStyle_ =
    new(Colors.PeachPuff, ColorPens.GetPen(Colors.Black));
  private static readonly HighlightingStyle pinnedBookmarkStyle_ =
    new(Colors.PapayaWhip, ColorPens.GetPen(Colors.Black));
  private static readonly HighlightingStyle pinButtonStyle_ = new(Colors.Silver, ColorPens.GetPen(Colors.Black));
  private static readonly HighlightingStyle hoverBookmarkStyle_ = new(Colors.LightBlue, ColorPens.GetPen(Colors.Gray));
  private static readonly HighlightingStyle nearbyBookmarkStyle_ =
    new(Colors.LightBlue, ColorPens.GetPen(Colors.Silver));
  private SolidColorBrush backgroundBrush_;
  private HashSet<IRElement> blockElements_;
  private List<HighlightedSegmentGroup> blockGroups_;
  private IconDrawing bookmarkIcon_;
  private TextSegmentCollection<BookmarkSegment> bookmarkSegments_;
  private BookmarkSegment hoveredBookmark_;
  private HashSet<BookmarkSegment> nearbyBookmarks_;
  private IconDrawing pinIcon_;
  private List<IconDrawing> remarkIcons_;
  private IconDrawing removeIcon_;
  private BookmarkSegment selectedBookmark_;

  public DocumentMargin(Color backgroundColor) {
    BackgroundColor = backgroundColor;
    blockGroups_ = new List<HighlightedSegmentGroup>();
    blockElements_ = new HashSet<IRElement>();
    bookmarkSegments_ = new TextSegmentCollection<BookmarkSegment>();
    nearbyBookmarks_ = new HashSet<BookmarkSegment>();
    bookmarkIcon_ = IconDrawing.FromIconResource("MarkerIcon");
    removeIcon_ = IconDrawing.FromIconResource("RemoveIcon");
    pinIcon_ = IconDrawing.FromIconResource("PinIcon");
    remarkIcons_ = new List<IconDrawing>();
    remarkIcons_.Add(IconDrawing.FromIconResource("DotIcon"));
    remarkIcons_.Add(IconDrawing.FromIconResource("ZapIcon"));
    remarkIcons_.Add(IconDrawing.FromIconResource("StarIcon"));
    remarkIcons_.Add(IconDrawing.FromIconResource("TagIcon"));
    remarkIcons_.Add(IconDrawing.FromIconResource("WarningIcon"));
    Version = 1;
  }

  public event EventHandler<Bookmark> BookmarkRemoved;
  public event EventHandler<Bookmark> BookmarkChanged;

  private enum BookmarkSegmentElement {
    Bookmark,
    PinButton,
    RemoveButton
  }

  public int Version { get; set; }
  public bool DisableBlockRemoval { get; set; }
  public Bookmark SelectedBookmark => selectedBookmark_?.Bookmark;
  public List<HighlightedSegmentGroup> BlockGroups => blockGroups_;

  public Color BackgroundColor {
    get => backgroundBrush_.Color;
    set {
      backgroundBrush_ = ColorBrushes.GetBrush(value);
      InvalidateVisual();
    }
  }

  public DocumentMarginState SaveState() {
    var marginState = new DocumentMarginState();
    marginState.BlockGroups = StateSerializer.SaveElementGroupState(blockGroups_);
    marginState.BookmarkSegments = bookmarkSegments_.ToList();
    marginState.HoveredBookmark = hoveredBookmark_;
    marginState.SelectedBookmark = selectedBookmark_;
    return marginState;
  }

  public void LoadState(DocumentMarginState state) {
    if (state == null) {
      return; // Most likely a file from an older version of the app.
    }

    blockGroups_ = StateSerializer.LoadElementGroupState(state.BlockGroups);
    hoveredBookmark_ = state.HoveredBookmark;
    selectedBookmark_ = state.SelectedBookmark;
    bookmarkSegments_ = new TextSegmentCollection<BookmarkSegment>();

    state.BookmarkSegments.ForEach(item => {
      if (item.Kind == BookmarkSegmentKind.Bookmark) {
        AddBookmark(item.Bookmark);
      }
    });
  }

  public void AddBlock(HighlightedElementGroup group, bool saveToFile = true) {
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

  public void AddRemarkBookmark(Bookmark bookmark, RemarkLineGroup remarkGroup) {
    bookmarkSegments_.Add(new BookmarkSegment(bookmark, BookmarkSegmentKind.Remark, remarkGroup));
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

    int index = blockGroups_.FindIndex(e => e.Group.Elements.Contains(block));

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
    bookmarkSegments_ = new TextSegmentCollection<BookmarkSegment>();
    Version++;
  }

  public void Reset() {
    ClearMarkers();
    ClearBookmarks();
    blockElements_.Clear();
    blockGroups_.Clear();
    nearbyBookmarks_.Clear();
    selectedBookmark_ = null;
    hoveredBookmark_ = null;
  }

  public void ForEachBlockElement(Action<IRElement> action) {
    blockGroups_.ForEach(group => { group.Group.Elements.ForEach(action); });
  }

  public void ForEachBookmark(Action<Bookmark> action) {
    foreach (var segment in bookmarkSegments_) {
      action(segment.Bookmark);
    }
  }

  public void MouseMoved(MouseEventArgs e) {
    HandleMouseMoved(e.GetPosition(this));
  }

  public bool HasHoveredBookmark() {
    return HitTestBookmarks() != null;
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

  public void RemoveRemarkBookmarks() {
    var bookmarkList = new List<BookmarkSegment>();

    foreach (var segment in bookmarkSegments_) {
      if (segment.Kind == BookmarkSegmentKind.Bookmark) {
        bookmarkList.Add(segment);
      }
    }

    // Clear current segments and add back just the bookmarks.
    if (bookmarkList.Count < bookmarkSegments_.Count) {
      bookmarkSegments_.Clear();

      foreach (var segment in bookmarkList) {
        bookmarkSegments_.Add(segment);
      }
    }
  }

  protected override HitTestResult HitTestCore(PointHitTestParameters hitParams) {
    HandleMouseMoved(Mouse.GetPosition(this));
    return new PointHitTestResult(this, hitParams.HitPoint);
  }

  protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e) {
    var result = HitTestBookmarks();

    if (result != null) {
      var segment = result.Item1;
      var element = result.Item2;

      switch (element) {
        case BookmarkSegmentElement.Bookmark:
          UnselectBookmark();
          SelectBookmark(segment);
          InvalidateVisual();
          BookmarkChanged?.Invoke(this, segment.Bookmark);
          e.Handled = true;
          return;
        case BookmarkSegmentElement.PinButton:
          UnselectBookmark();
          SelectBookmark(segment);
          segment.IsPinned = !segment.IsPinned;
          segment.IsHovered = !segment.IsPinned;
          InvalidateVisual();
          BookmarkChanged?.Invoke(this, segment.Bookmark);
          e.Handled = true;
          return;
        case BookmarkSegmentElement.RemoveButton:
          BookmarkRemoved?.Invoke(this, segment.Bookmark);
          break;
      }
    }

    UnselectBookmark();
    base.OnPreviewMouseLeftButtonDown(e);
  }

  protected override Size MeasureOverride(Size availableSize) {
    return new Size(MarginWidth, 0);
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

  protected override void OnRender(DrawingContext drawingContext) {
    // Draw margin background.
    drawingContext.DrawRectangle(backgroundBrush_, null,
                                 new Rect(0, 0, RenderSize.Width, RenderSize.Height));

    // Draw highlighted blocks.
    if (!DocumentUtils.FindVisibleTextOffsets(TextView, out int viewStart, out int viewEnd)) {
      return;
    }

    foreach (var group in blockGroups_) {
      DrawGroup(group, TextView, drawingContext, viewStart, viewEnd);
    }

    // Draw bookmarks in two steps, first the unselected ones, then the selected ones.
    // This is done so that the borders of the selected ones are not drawn over by other
    // segments with a different border color.
    double lineHeight = Math.Ceiling(TextView.DefaultLineHeight);
    var segments = bookmarkSegments_.FindOverlappingSegments(TextView);

    foreach (var segment in segments) {
      if (!(segment.IsSelected || segment.IsHovered || segment.IsPinned)) {
        DrawSegment(segment, lineHeight, drawingContext);
      }
    }

    foreach (var segment in segments) {
      if (segment.IsSelected || segment.IsHovered || segment.IsPinned) {
        DrawSegment(segment, lineHeight, drawingContext);
      }
    }
  }

  private void HandleMouseMoved(Point point) {
    // Test only with visible bookmarks.
    BookmarkSegment newHoveredBookmark = null;
    bool newTextHoveredBookmark = false;
    bool needsRedrawing = false;

    foreach (var segment in bookmarkSegments_.FindOverlappingSegments(TextView)) {
      if (newHoveredBookmark == null) {
        if (segment.PinButtonBounds.Contains(point) ||
            segment.RemoveButtonBounds.Contains(point)) {
          newHoveredBookmark = segment;
          newTextHoveredBookmark = false;
          continue;
        }

        if (segment.Bounds.Contains(point)) {
          newHoveredBookmark = segment;
          newTextHoveredBookmark = true;
          continue;
        }
      }

      if (!segment.IsExpanded) {
        double distance = (segment.Bounds.TopLeft - point).Length;
        bool onSameLine = point.Y >= segment.Bounds.Top && point.Y <= segment.Bounds.Bottom;

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

    if (newHoveredBookmark != null) {
      if (newHoveredBookmark != hoveredBookmark_ ||
          hoveredBookmark_.IsTextHovered != newTextHoveredBookmark) {
        // Replace hovered bookmark.
        if (hoveredBookmark_ != null) {
          UnselectHoveredBookmark();
        }

        newHoveredBookmark.IsHovered = true;
        newHoveredBookmark.IsTextHovered = newTextHoveredBookmark;
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
    else if (hoveredBookmark_ != null) {
      UnselectHoveredBookmark();
      needsRedrawing = true;
    }

    if (needsRedrawing) {
      InvalidateVisual();
    }
  }

  private Tuple<BookmarkSegment, BookmarkSegmentElement> HitTestBookmarks() {
    var point = Mouse.GetPosition(this);

    foreach (var segment in bookmarkSegments_.FindOverlappingSegments(TextView)) {
      if (segment.PinButtonBounds.Contains(point)) {
        return new Tuple<BookmarkSegment, BookmarkSegmentElement>(
          segment, BookmarkSegmentElement.PinButton);
      }

      if (segment.RemoveButtonBounds.Contains(point)) {
        return new Tuple<BookmarkSegment, BookmarkSegmentElement>(
          segment, BookmarkSegmentElement.RemoveButton);
      }

      if (segment.Bounds.Contains(point)) {
        return new Tuple<BookmarkSegment, BookmarkSegmentElement>(
          segment, BookmarkSegmentElement.Bookmark);
      }
    }

    return null;
  }

  private void UnselectHoveredBookmark() {
    if (hoveredBookmark_ != null) {
      hoveredBookmark_.IsHovered = false;
      hoveredBookmark_.IsTextHovered = false;
      hoveredBookmark_ = null;
    }
  }

  private void SelectBookmark(BookmarkSegment segment) {
    UnselectHoveredBookmark();
    selectedBookmark_ = segment;
    selectedBookmark_.IsSelected = true;
  }

  private void VisualLinesChanged(object sender, EventArgs e) {
    InvalidateVisual();
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
    if (segment.Bookmark.Style != null) {
      //if (style == defaultBookmarkStyle_ &&
      //    segment.Kind != BookmarkSegmentKind.Bookmark)
      //{
      //    style = new HighlightingStyle(ColorBrushes.Transparent, null);
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

  private void DrawGroup(HighlightedSegmentGroup group, TextView textView,
                         DrawingContext drawingContext, int viewStart, int viewEnd) {
    var renderSize = RenderSize;

    foreach (var result in group.Segments.FindOverlappingSegments(viewStart, viewEnd - viewStart)) {
      foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, result)) {
        drawingContext.DrawRectangle(group.BackColor, null,
                                     Utils.SnapRectToPixels(renderSize.Width - MarginWidth,
                                                            rect.Top, MarginWidth, rect.Height + 1));
      }
    }
  }

  private void DrawSegment(BookmarkSegment segment, double lineHeight, DrawingContext drawingContext) {
    foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(TextView, segment)) {
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
      bool popOpacity = false;

      if (segment.HasText && segment.IsExpanded) {
        double baseWidth = RenderSize.Width + ButtonSectionWidth;
        double fontSize = App.Settings.DocumentSettings.FontSize;

        //? TODO: Add option to control little opacity on hover
        if (segment.IsPinned && segment.IsTextHovered) {
          drawingContext.PushOpacity(HoveredPinnedBookmarkOpacity);
          popOpacity = true;
        }

        if (segment.Tag is RemarkLineGroup remarkGroup) {
          double maxTextWidth = 0;
          var textSegments = new List<FormattedText>(remarkGroup.Remarks.Count);
          int index = 0;
          int leaderIndex = 0;

          foreach (var remark in remarkGroup.Remarks) {
            var fontWeight = FontWeights.Normal;

            if (remark == remarkGroup.LeaderRemark) {
              leaderIndex = index;
              fontWeight = FontWeights.Medium;
            }

            var text = DocumentUtils.CreateFormattedText(this, remark.RemarkText, DefaultFont, fontSize,
                                                         Brushes.Black, fontWeight);
            textSegments.Add(text);
            maxTextWidth = Math.Max(maxTextWidth, text.Width);
            index++;
          }

          index = 0;

          foreach (var remark in remarkGroup.Remarks) {
            var remarkColor = remark.Category.MarkColor == Colors.Black ||
                              remark.Category.MarkColor == Colors.Transparent
              ? backgroundBrush_.Color
              : remark.Category.MarkColor;
            var remarkBrush = ColorBrushes.GetBrush(remarkColor);
            var text = textSegments[index];
            double offsetY = y + (index - leaderIndex) * lineHeight;

            var remarkBounds = Utils.SnapRectToPixels(0, offsetY - 1,
                                                      maxTextWidth + baseWidth + 2 * ButtonTextPadding, lineHeight);
            drawingContext.DrawRectangle(remarkBrush, style.Border, remarkBounds);
            drawingContext.DrawText(text, new Point(baseWidth + ButtonTextPadding, offsetY - 1));

            if (index == leaderIndex) {
              bounds = remarkBounds;
            }

            index++;
          }
        }
        else {
          var text = DocumentUtils.CreateFormattedText(this, segment.Bookmark.Text, DefaultFont, fontSize,
                                                       Brushes.Black);
          bounds = Utils.SnapRectToPixels(0, y - 1, text.Width + baseWidth + 2 * ButtonTextPadding, lineHeight);
          drawingContext.DrawRectangle(style.BackColor, style.Border, bounds);
          drawingContext.DrawText(text, new Point(baseWidth + ButtonTextPadding, y));
        }
      }
      else {
        double nearbyExtraWidth = segment.IsNearby || segment.IsExpanded ? NearbyBookmarkExtraWidth : 0;
        bounds = Utils.SnapRectToPixels(0, y - 1, RenderSize.Width + nearbyExtraWidth, lineHeight);
        drawingContext.DrawRectangle(style.BackColor, style.Border, bounds);
      }

      var icon = SelectBookmarkIcon(segment);

      if (icon != null) {
        icon.Draw(0, y - 1, lineHeight - 1,
                  RenderSize.Width, lineHeight, drawingContext);
      }

      if (segment.IsExpanded) {
        var buttonBounds = new Rect(MarginWidth, y - 1, ButtonSectionWidth, lineHeight);
        removeBounds = DrawBookmarkButton(removeIcon_, buttonBounds, style, drawingContext);
        var pinStyle = GetPinButtonStyle(segment);

        pinBounds = DrawBookmarkButton(pinIcon_, buttonBounds, pinStyle, drawingContext,
                                       removeBounds.Width);
      }

      if (popOpacity) {
        drawingContext.Pop();
      }

      segment.Bounds = bounds; // Update bounds for later hit testing.
      segment.PinButtonBounds = pinBounds;
      segment.RemoveButtonBounds = removeBounds;
      return; // Stop after drawing it once.
    }
  }

  private IconDrawing SelectBookmarkIcon(BookmarkSegment segment) {
    switch (segment.Kind) {
      case BookmarkSegmentKind.Bookmark: {
        return bookmarkIcon_;
      }
      case BookmarkSegmentKind.Remark: {
        if (segment.Tag == null) {
          return null;
        }

        var remarkGroup = (RemarkLineGroup)segment.Tag;
        var remark = remarkGroup.LeaderRemark;

        if (remark.Category.MarkIconIndex >= 0 &&
            remark.Category.MarkIconIndex < remarkIcons_.Count) {
          return remarkIcons_[remark.Category.MarkIconIndex];
        }

        return null;
      }
      default:
        throw new InvalidOperationException("Unknown segment kind!");
    }
  }

  private Rect DrawBookmarkButton(IconDrawing icon, Rect startBounds, HighlightingStyle pinStyle,
                                  DrawingContext drawingContext, double extraLeftSpace = 0) {
    var bounds = Utils.SnapRectToPixels(startBounds.Left + extraLeftSpace, startBounds.Top, ButtonWidth,
                                        startBounds.Height);
    drawingContext.DrawRectangle(pinStyle.BackColor, pinStyle.Border, bounds);

    icon.Draw(bounds.Left + 1, bounds.Top, ButtonIconWidth,
              bounds.Width, bounds.Height, drawingContext);
    return bounds;
  }
}