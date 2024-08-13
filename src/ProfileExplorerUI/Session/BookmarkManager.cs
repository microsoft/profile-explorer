// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using ProfileExplorer.Core.IR;
using ProtoBuf;

namespace ProfileExplorer.UI;

[ProtoContract]
public class BookmarkManagerState {
  [ProtoMember(1)]
  public List<Bookmark> Bookmarks;
  [ProtoMember(2)]
  public List<Tuple<IRElementReference, Bookmark>> ElementBookmarkMap;
  [ProtoMember(3)]
  public int NextIndex;
  [ProtoMember(4)]
  public int SelectedIndex;
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
    var bookmarkState = new BookmarkManagerState {
      Bookmarks = bookmarks_.CloneList(),
      ElementBookmarkMap = elementBookmarkMap_?.ToList<IRElement, IRElementReference, Bookmark>(),
      NextIndex = nextIndex_,
      SelectedIndex = selectedIndex_
    };

    return StateSerializer.Serialize(bookmarkState, function);
  }

  public void LoadState(byte[] data, FunctionIR function) {
    var bookmarkState = StateSerializer.Deserialize<BookmarkManagerState>(data, function);
    bookmarks_ = bookmarkState.Bookmarks ?? new List<Bookmark>();
    elementBookmarkMap_ =
      bookmarkState.ElementBookmarkMap?.ToDictionary<IRElementReference, IRElement, Bookmark>() ??
      new Dictionary<IRElement, Bookmark>();

    nextIndex_ = bookmarkState.NextIndex;
    selectedIndex_ = bookmarkState.SelectedIndex;
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