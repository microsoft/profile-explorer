// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Windows.Media;
using IRExplorerCore.IR;
using ProtoBuf;

namespace IRExplorer {
    [ProtoContract]
    public class Bookmark {
        [ProtoMember(1)]
        private IRElementReference elementRef_;
        public Bookmark() { }

        public Bookmark(int index, IRElement element, string text, HighlightingStyle style) {
            Index = index;
            Element = element;
            Text = text;
            Style = style;
        }

        public IRElement Element {
            get => elementRef_;
            set => elementRef_ = value;
        }

        [ProtoMember(2)] public int Index { get; set; }

        [ProtoMember(3)] public string Text { get; set; }

        [ProtoMember(4)] public bool IsSelected { get; set; }

        [ProtoMember(5)] public bool IsPinned { get; set; }

        [ProtoMember(6)] public HighlightingStyle Style { get; set; }

        public bool HasStyle => Style != null;
        public Brush StyleBackColor => Style?.BackColor;
        public int Line => Element.TextLocation.Line;
        public string Block => Utils.MakeBlockDescription(Element.ParentBlock);
    }

    [ProtoContract]
    public class BookmarkManagerState {
        [ProtoMember(1)]
        public List<Bookmark> bookmarks_;
        [ProtoMember(2)]
        public List<Tuple<IRElementReference, Bookmark>> elementBookmarkMap_;
        [ProtoMember(3)]
        public int nextIndex_;
        [ProtoMember(4)]
        public int selectedIndex_;
    }

    public class BookmarkManager {
        private List<Bookmark> bookmarks_;
        private Dictionary<IRElement, Bookmark> elementBookmarkMap_;
        private int nextIndex_;
        private int selectedIndex_;

        public BookmarkManager() {
            bookmarks_ = new List<Bookmark>();
            elementBookmarkMap_ = new Dictionary<IRElement, Bookmark>();
            nextIndex_ = 1;
            selectedIndex_ = -1;
            Version = 1;
        }

        public HighlightingStyle DefaultStyle { get; set; }
        public List<Bookmark> Bookmarks => bookmarks_;
        public int SelectedIndex => selectedIndex_;
        public int Version { get; set; }

        public byte[] SaveState(FunctionIR function) {
            var bookmarkState = new BookmarkManagerState();
            bookmarkState.bookmarks_ = bookmarks_.CloneList();

            bookmarkState.elementBookmarkMap_ =
                elementBookmarkMap_?.ToList<IRElement, IRElementReference, Bookmark>();

            bookmarkState.nextIndex_ = nextIndex_;
            bookmarkState.selectedIndex_ = selectedIndex_;
            return StateSerializer.Serialize(bookmarkState, function);
        }

        public void LoadState(byte[] data, FunctionIR function) {
            var bookmarkState = StateSerializer.Deserialize<BookmarkManagerState>(data, function);
            bookmarks_ = bookmarkState.bookmarks_ ?? new List<Bookmark>();

            elementBookmarkMap_ =
                bookmarkState.elementBookmarkMap_?.ToDictionary<IRElementReference, IRElement, Bookmark>() ??
                new Dictionary<IRElement, Bookmark>();

            nextIndex_ = bookmarkState.nextIndex_;
            selectedIndex_ = bookmarkState.selectedIndex_;
            Version++;
        }

        public void CopyFrom(BookmarkManager other) {
            bookmarks_ = other.bookmarks_;
            elementBookmarkMap_ = other.elementBookmarkMap_;
            nextIndex_ = other.nextIndex_;
            selectedIndex_ = other.selectedIndex_;
            Version++;
        }

        public Bookmark AddBookmark(IRElement element, string text = "") {
            // Remove any previously associated bookmark.
            RemoveBookmark(element);
            var bookmark = new Bookmark(nextIndex_, element, text, DefaultStyle);
            bookmarks_.Add(bookmark);
            elementBookmarkMap_.Add(element, bookmark);
            nextIndex_++;
            Version++;
            return bookmark;
        }

        public Bookmark FindBookmark(IRElement element) {
            return elementBookmarkMap_.TryGetValue(element, out var value) ? value : null;
        }

        public void RemoveBookmark(Bookmark bookmark) {
            RemoveBookmark(bookmark.Element);
        }

        public Bookmark RemoveBookmark(IRElement element) {
            if (elementBookmarkMap_.TryGetValue(element, out var bookmark)) {
                bookmarks_.Remove(bookmark);
                elementBookmarkMap_.Remove(element);
                selectedIndex_ = Math.Min(selectedIndex_, bookmarks_.Count - 1);
                Version++;
                return bookmark;
            }

            return null;
        }

        public void Clear() {
            elementBookmarkMap_.Clear();
            bookmarks_.Clear();
            nextIndex_ = 1;
            selectedIndex_ = -1;
            Version++;
        }

        public Bookmark JumpToFirstBookmark() {
            if (bookmarks_.Count > 0) {
                selectedIndex_ = 0;
                return bookmarks_[selectedIndex_];
            }

            return null;
        }

        public Bookmark JumpToLastBookmark() {
            if (bookmarks_.Count > 0) {
                selectedIndex_ = bookmarks_.Count - 1;
                return bookmarks_[selectedIndex_];
            }

            return null;
        }

        public Bookmark GetNext() {
            if (selectedIndex_ < bookmarks_.Count - 1) {
                selectedIndex_++;
                return bookmarks_[selectedIndex_];
            }

            return null;
        }

        public Bookmark GetPrevious() {
            if (selectedIndex_ > 0) {
                selectedIndex_--;
                return bookmarks_[selectedIndex_];
            }

            return null;
        }

        public Bookmark GetNextAfter(Bookmark other) {
            return null;
        }
    }
}
