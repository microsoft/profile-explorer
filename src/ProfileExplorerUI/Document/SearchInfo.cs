// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.ComponentModel;
using ProtoBuf;

namespace ProfileExplorer.UI.Document;

[ProtoContract]
public class SearchInfo : INotifyPropertyChanged {
  [ProtoMember(1)]
  private int currentResult_;
  [ProtoMember(2)]
  private TextSearchKind kind_;
  [ProtoMember(3)]
  private int resultCount_;
  [ProtoMember(4)]
  private bool searchAllEnabled_;
  [ProtoMember(5)]
  private bool searchAll_;
  [ProtoMember(6)]
  private string searchedText_;
  [ProtoMember(7)]
  private bool showSearchAllButton_;
  [ProtoMember(8)]
  private bool showNavigationSection_;

  public SearchInfo() {
    searchedText_ = string.Empty;
    kind_ = TextSearchKind.CaseInsensitive;
    showSearchAllButton_ = true;
    searchAllEnabled_ = true;
    showNavigationSection_ = true;
  }

  public TextSearchKind SearchKind {
    get => kind_;
    set => kind_ = value;
  }

  public string SearchedText {
    get => searchedText_;
    set {
      if (value != searchedText_) {
        searchedText_ = value;
        OnPropertyChange(nameof(SearchedText));
      }
    }
  }

  public bool HasSearchedText => !string.IsNullOrEmpty(searchedText_);

  public bool IsCaseInsensitive {
    get => kind_.HasFlag(TextSearchKind.CaseInsensitive);
    set {
      if (SetKindFlag(TextSearchKind.CaseInsensitive, value)) {
        OnPropertyChange(nameof(IsCaseInsensitive));
      }
    }
  }

  public bool IsWholeWord {
    get => kind_.HasFlag(TextSearchKind.WholeWord);
    set {
      if (SetKindFlag(TextSearchKind.WholeWord, value)) {
        OnPropertyChange(nameof(IsWholeWord));
      }
    }
  }

  public bool IsRegex {
    get => kind_.HasFlag(TextSearchKind.Regex);
    set {
      if (SetKindFlag(TextSearchKind.Regex, value)) {
        OnPropertyChange(nameof(IsRegex));
      }
    }
  }

  public bool SearchAll {
    get => searchAll_ && searchAllEnabled_;
    set {
      if (value != searchAll_) {
        searchAll_ = value;
        OnPropertyChange(nameof(SearchAll));
      }
    }
  }

  public bool SearchAllEnabled {
    get => searchAllEnabled_;
    set {
      if (value != searchAllEnabled_) {
        searchAllEnabled_ = value;
        OnPropertyChange(nameof(SearchAllEnabled));
      }
    }
  }

  public int ResultCount {
    get => resultCount_;
    set {
      if (resultCount_ != value) {
        resultCount_ = value;
        OnPropertyChange(nameof(ResultText));
      }
    }
  }

  public int CurrentResult {
    get => currentResult_;
    set {
      if (currentResult_ != value) {
        currentResult_ = value;
        OnPropertyChange(nameof(ResultText));
      }
    }
  }

  public string ResultText => $"{(resultCount_ > 0 ? currentResult_ + 1 : 0)} / {resultCount_}";

  public bool ShowSearchAllButton {
    get => showSearchAllButton_;
    set {
      if (value != showSearchAllButton_) {
        showSearchAllButton_ = value;
        OnPropertyChange(nameof(ShowSearchAllButton));
      }
    }
  }

  public bool ShowNavigationSection {
    get => showNavigationSection_;
    set {
      if (value != showNavigationSection_) {
        showNavigationSection_ = value;
        OnPropertyChange(nameof(ShowNavigationSection));
      }
    }
  }

  public event PropertyChangedEventHandler PropertyChanged;

  public void OnPropertyChange(string propertyname) {
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyname));
  }

  private bool SetKindFlag(TextSearchKind flag, bool value) {
    if (value && !kind_.HasFlag(flag)) {
      kind_ |= flag;
      return true;
    }

    if (!value && kind_.HasFlag(flag)) {
      kind_ &= ~flag;
      return true;
    }

    return false;
  }
}