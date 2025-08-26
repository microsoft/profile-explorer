// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using HtmlAgilityPack;
using ProfileExplorer.UI.Profile;
using ProfileExplorer.Core.Providers;

namespace ProfileExplorer.UI;

public class SearchableProfileItem : BindableObject {
  private string functionName_;
  private FunctionNameFormatter funcNameFormatter_;
  private bool isMarked_;
  private TextBlock name_;

  public SearchableProfileItem(FunctionNameFormatter funcNameFormatter) {
    funcNameFormatter_ = funcNameFormatter;
  }

  public virtual string FunctionName {
    get {
      if (functionName_ != null) {
        return functionName_; // Cached.
      }

      functionName_ = GetFunctionName();

      if (funcNameFormatter_ != null && functionName_ != null) {
        functionName_ = funcNameFormatter_(functionName_);
      }

      return functionName_;
    }
    set => functionName_ = value;
  }

  public virtual string ModuleName { get; set; }
  public virtual TimeSpan Weight { get; set; }
  public virtual TimeSpan ExclusiveWeight { get; set; }
  public double Percentage { get; set; }
  public double ExclusivePercentage { get; set; }
  public TextSearchResult? SearchResult { get; set; }

  public bool IsMarked {
    get => isMarked_;
    set {
      SetAndNotify(ref isMarked_, value);
      ResetCachedName();
    }
  }

  public TextBlock Name {
    get {
      if (name_ == null) {
        name_ = CreateOnDemandName();
      }

      return name_;
    }
  }

  public void ResetCachedName() {
    SetAndNotify(ref name_, null, "Name");
  }

  protected virtual string GetFunctionName() {
    return null;
  }

  protected TextBlock CreateOnDemandName() {
    var textBlock = new TextBlock();

    if (IsMarked) {
      textBlock.FontWeight = FontWeights.Bold;
    }

    if (ShouldPrependModule() && !string.IsNullOrEmpty(ModuleName)) {
      textBlock.Inlines.Add(new Run(ModuleName) {
        Foreground = Brushes.DimGray,
        FontWeight = IsMarked ? FontWeights.Medium : FontWeights.Normal
      });

      textBlock.Inlines.Add("!");
      var nameFontWeight = IsMarked ? FontWeights.Bold : FontWeights.SemiBold;

      if (SearchResult.HasValue) {
        CreateSearchResultName(textBlock, nameFontWeight);
      }
      else {
        textBlock.Inlines.Add(new Run(FunctionName) {
          FontWeight = nameFontWeight
        });
      }
    }
    else {
      var nameFontWeight = IsMarked ? FontWeights.Bold : FontWeights.Medium;

      if (SearchResult.HasValue) {
        CreateSearchResultName(textBlock, nameFontWeight);
      }
      else {
        textBlock.Inlines.Add(new Run(FunctionName) {
          FontWeight = nameFontWeight
        });
      }
    }

    return textBlock;
  }

  protected virtual bool ShouldPrependModule() {
    return true;
  }

  private void CreateSearchResultName(TextBlock textBlock, FontWeight nameFontWeight) {
    if (SearchResult is {Offset: > 0}) {
      textBlock.Inlines.Add(new Run(FunctionName.Substring(0, SearchResult.Value.Offset)) {
        FontWeight = nameFontWeight
      });
    }

    textBlock.Inlines.Add(new Run(FunctionName.Substring(SearchResult.Value.Offset, SearchResult.Value.Length)) {
      Background = Brushes.Khaki
    });

    int remainingLength = FunctionName.Length - (SearchResult.Value.Offset + SearchResult.Value.Length);

    if (remainingLength > 0) {
      textBlock.Inlines.Add(new Run(FunctionName.Substring(FunctionName.Length - remainingLength, remainingLength)) {
        FontWeight = nameFontWeight
      });
    }
  }

  public static void CopyFunctionListAsHtml(List<SearchableProfileItem> list) {
    string html = ExportFunctionListAsHtml(list);
    string plainText = ProfilingExporting.ExportFunctionListAsMarkdownTable(list);
    Utils.CopyHtmlToClipboard(html, plainText);
  }

  public static string ExportFunctionListAsHtml(List<SearchableProfileItem> list) {
    var doc = new HtmlDocument();
    var table = ProfilingExporting.ExportFunctionListAsHtmlTable(list, doc);
    doc.DocumentNode.AppendChild(table);

    var writer = new StringWriter();
    doc.Save(writer);
    return writer.ToString();
  }
}