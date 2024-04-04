// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using HtmlAgilityPack;

namespace IRExplorerUI;

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

  public virtual void ResetCachedName() {
    SetAndNotify(ref name_, null, "Name");
  }

  protected virtual string GetFunctionName() {
    return null;
  }

  protected virtual TextBlock CreateOnDemandName() {
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
    if (SearchResult.Value.Offset > 0) {
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
    var html = ExportFunctionListAsHtml((list));
    var plainText = ExportFunctionListAsMarkdown(list);
    Utils.CopyHtmlToClipboard(html, plainText);
  }

  public static string ExportFunctionListAsHtml(List<SearchableProfileItem> list) {
    string TableStyle = @"border-collapse:collapse;border-spacing:0;";
    string HeaderStyle = @"background-color:#D3D3D3;white-space:nowrap;text-align:left;vertical-align:top;border-color:black;border-style:solid;border-width:1px;overflow:hidden;padding:2px 2px;font-family:Arial, sans-serif;";
    string CellStyle = @"text-align:left;vertical-align:top;word-wrap:break-word;max-width:300px;overflow:hidden;padding:2px 2px;border-color:black;border-style:solid;border-width:1px;font-family:Arial, sans-serif;";

    var doc = new HtmlDocument();
    var table = doc.CreateElement("table");
    table.SetAttributeValue("style", TableStyle);

    var thead = doc.CreateElement("thead");
    var tbody = doc.CreateElement("tbody");
    var tr = doc.CreateElement("tr");

    var th = doc.CreateElement("th");
    th.InnerHtml = "Function";
    th.SetAttributeValue("style", HeaderStyle);
    tr.AppendChild(th);
    th = doc.CreateElement("th");
    th.InnerHtml = "Module";
    th.SetAttributeValue("style", HeaderStyle);
    tr.AppendChild(th);
    thead.AppendChild(tr);

    th = doc.CreateElement("th");
    th.InnerHtml = HttpUtility.HtmlEncode("Time (ms)");
    th.SetAttributeValue("style", HeaderStyle);
    tr.AppendChild(th);

    th = doc.CreateElement("th");
    th.InnerHtml = HttpUtility.HtmlEncode("Time (%)");
    th.SetAttributeValue("style", HeaderStyle);
    tr.AppendChild(th);

    th = doc.CreateElement("th");
    th.InnerHtml = HttpUtility.HtmlEncode("Tine incl (ms)");
    th.SetAttributeValue("style", HeaderStyle);
    tr.AppendChild(th);

    th = doc.CreateElement("th");
    th.InnerHtml = HttpUtility.HtmlEncode("Time incl (%)");
    th.SetAttributeValue("style", HeaderStyle);
    tr.AppendChild(th);

    table.AppendChild(thead);

    foreach (var node in list) {
      tr = doc.CreateElement("tr");
      var td = doc.CreateElement("td");
      td.InnerHtml = HttpUtility.HtmlEncode(node.FunctionName);
      td.SetAttributeValue("style", CellStyle);
      tr.AppendChild(td);
      td = doc.CreateElement("td");
      td.InnerHtml = HttpUtility.HtmlEncode(node.ModuleName);
      td.SetAttributeValue("style", CellStyle);
      tr.AppendChild(td);

      td = doc.CreateElement("td");
      td.InnerHtml = HttpUtility.HtmlEncode($"{node.ExclusiveWeight.TotalMilliseconds}");
      td.SetAttributeValue("style", CellStyle);
      tr.AppendChild(td);
      td = doc.CreateElement("td");
      td.InnerHtml = HttpUtility.HtmlEncode($"{node.ExclusivePercentage.AsPercentageString(2, false)}");
      td.SetAttributeValue("style", CellStyle);
      tr.AppendChild(td);
      td = doc.CreateElement("td");
      td.InnerHtml = HttpUtility.HtmlEncode($"{node.Weight.TotalMilliseconds}");
      td.SetAttributeValue("style", CellStyle);
      tr.AppendChild(td);
      td = doc.CreateElement("td");
      td.InnerHtml = HttpUtility.HtmlEncode($"{node.Percentage.AsPercentageString(2, false)}");
      td.SetAttributeValue("style", CellStyle);
      tr.AppendChild(td);

      tbody.AppendChild(tr);
    }

    table.AppendChild(tbody);
    doc.DocumentNode.AppendChild(table);


    var writer = new StringWriter();
    doc.Save(writer);
    return writer.ToString();
  }

  public static string ExportFunctionListAsMarkdown(List<SearchableProfileItem> list) {
    var sb = new StringBuilder();
    string header    = "| Function | Module |";
    string separator = "|----------|--------|";
    header    += " Time (ms) | Time (%) | Time incl (ms) | Time incl (%) |";
    separator += "-----------|----------|----------------|---------------|";

    sb.AppendLine(header);
    sb.AppendLine(separator);

    foreach (var func in list) {
      sb.Append($"| {func.FunctionName} | {func.ModuleName} " +
                $"| {func.ExclusiveWeight.TotalMilliseconds} " +
                $"| {func.ExclusivePercentage.AsPercentageString(2, false)} " +
                $"| {func.Weight.TotalMilliseconds} " +
                $"| {func.Percentage.AsPercentageString(2, false)} |\n");
    }

    return sb.ToString();
  }
}