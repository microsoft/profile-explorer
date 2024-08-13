// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Windows;
using HtmlAgilityPack;

namespace ProfileExplorer.UI.Profile;

public static class ProfilingExporting {
  public static HtmlNode ExportFunctionListAsHtmlTable(List<ProfileCallTreeNode> list, HtmlDocument doc,
                                                       ISession session) {
    var itemList = new List<SearchableProfileItem>();
    var markerOptions = App.Settings.DocumentSettings.ProfileMarkerSettings;

    foreach (var node in list) {
      if (!node.HasFunction) {
        continue;
      }

      var item = ProfileListViewItem.From(node, session.ProfileData,
                                          session.CompilerInfo.NameProvider.FormatFunctionName, null);
      item.FunctionBackColor = markerOptions.PickBrushForPercentage(item.ExclusivePercentage);
      itemList.Add(item);
    }

    return ExportFunctionListAsHtmlTable(itemList, doc);
  }

  public static HtmlNode ExportFunctionListAsHtmlTable(List<SearchableProfileItem> list, HtmlDocument doc) {
    string TableStyle = @"border-collapse:collapse;border-spacing:0;";
    string HeaderStyle =
      @"background-color:#D3D3D3;white-space:nowrap;text-align:left;vertical-align:top;border-color:black;border-style:solid;border-width:1px;overflow:hidden;padding:2px 2px;font-family:Arial, sans-serif;";
    string CellStyle =
      @"text-align:left;vertical-align:top;word-wrap:break-word;max-width:300px;overflow:hidden;padding:2px 2px;border-color:black;border-style:solid;border-width:1px;font-family:Arial, sans-serif;";
    var markingSettings = App.Settings.DocumentSettings.ProfileMarkerSettings;
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
    th.InnerHtml = HttpUtility.HtmlEncode($"Time ({markingSettings.ValueUnitSuffix})");
    th.SetAttributeValue("style", HeaderStyle);
    tr.AppendChild(th);

    th = doc.CreateElement("th");
    th.InnerHtml = HttpUtility.HtmlEncode("Time (%)");
    th.SetAttributeValue("style", HeaderStyle);
    tr.AppendChild(th);

    th = doc.CreateElement("th");
    th.InnerHtml = HttpUtility.HtmlEncode($"Tine incl ({markingSettings.ValueUnitSuffix})");
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

      // Use a background color if defined.
      string colorAttr = "";

      if (node is ProfileListViewItem listViewItem) {
        string backColor = Utils.BrushToString(listViewItem.FunctionBackColor);
        colorAttr = backColor != null ? $";background-color:{backColor}" : "";
      }

      td = doc.CreateElement("td");
      td.InnerHtml = HttpUtility.HtmlEncode($"{node.ExclusiveWeight.TotalMilliseconds}");
      td.SetAttributeValue("style", $"{CellStyle}{colorAttr}");
      tr.AppendChild(td);
      td = doc.CreateElement("td");
      td.InnerHtml = HttpUtility.HtmlEncode($"{node.ExclusivePercentage.AsPercentageString()}");
      td.SetAttributeValue("style", $"{CellStyle}{colorAttr}");
      tr.AppendChild(td);
      td = doc.CreateElement("td");
      td.InnerHtml = HttpUtility.HtmlEncode($"{node.Weight.TotalMilliseconds}");
      td.SetAttributeValue("style", $"{CellStyle}{colorAttr}");
      tr.AppendChild(td);
      td = doc.CreateElement("td");
      td.InnerHtml = HttpUtility.HtmlEncode($"{node.Percentage.AsPercentageString()}");
      td.SetAttributeValue("style", $"{CellStyle}{colorAttr}");
      tr.AppendChild(td);

      tbody.AppendChild(tr);
    }

    table.AppendChild(tbody);
    return table;
  }

  public static string ExportFunctionListAsMarkdownTable(List<ProfileCallTreeNode> list,
                                                         ISession session) {
    var itemList = new List<SearchableProfileItem>();
    var nameFormatter = session.CompilerInfo.NameProvider;

    foreach (var node in list) {
      if (!node.HasFunction) {
        continue;
      }

      itemList.Add(ProfileListViewItem.From(node, session.ProfileData,
                                            session.CompilerInfo.NameProvider.FormatFunctionName, null));
    }

    return ExportFunctionListAsMarkdownTable(itemList);
  }

  public static string ExportFunctionListAsMarkdownTable(List<SearchableProfileItem> list) {
    var markingSettings = App.Settings.DocumentSettings.ProfileMarkerSettings;
    var sb = new StringBuilder();
    string header = "| Function | Module |";
    string separator = "|----------|--------|";
    header +=
      $" Time ({markingSettings.ValueUnitSuffix}) | Time (%) | Time incl ({markingSettings.ValueUnitSuffix}) | Time incl (%) |";
    separator += "-----------|----------|----------------|---------------|";

    sb.AppendLine(header);
    sb.AppendLine(separator);

    foreach (var func in list) {
      sb.Append($"| {func.FunctionName} | {func.ModuleName} " +
                $"| {func.ExclusiveWeight.TotalMilliseconds} " +
                $"| {func.ExclusivePercentage.AsPercentageString()} " +
                $"| {func.Weight.TotalMilliseconds} " +
                $"| {func.Percentage.AsPercentageString()} |\n");
    }

    return sb.ToString();
  }

  public static (string Html, string Plaintext)
    ExportProfilingReportAsHtml(List<FunctionMarkingCategory> markingCategoryList, ISession session,
                                bool includeHottestFunctions, int hotFuncLimit = 20,
                                bool includeCategoriesTable = true,
                                bool includeOverview = true,
                                bool isCategoryList = true) {
    string TableStyle = @"border-collapse:collapse;border-spacing:0;";
    string HeaderStyle =
      @"background-color:#D3D3D3;white-space:nowrap;text-align:left;vertical-align:top;border-color:black;border-style:solid;border-width:1px;overflow:hidden;padding:2px 2px;font-family:Arial, sans-serif;";
    string CellStyle =
      @"text-align:left;vertical-align:top;word-wrap:break-word;max-width:300px;overflow:hidden;padding:2px 2px;border-color:black;border-style:solid;border-width:1px;font-family:Arial, sans-serif;";
    string PatternCellStyle =
      @"text-align:left;vertical-align:top;word-wrap:break-word;max-width:500px;overflow:hidden;padding:2px 2px;border-color:black;border-style:solid;border-width:1px;font-family:Arial, sans-serif;";

    var doc = new HtmlDocument();
    var sb = new StringBuilder();
    var markingSettings = App.Settings.DocumentSettings.ProfileMarkerSettings;

    if (includeCategoriesTable) {
      if (includeOverview) {
        ExportTraceOverviewAsHtml(session, doc, sb);
      }

      AppendTitleParagraph(includeHottestFunctions ? "Categories Summary" : "Markings Summary", doc);
      var table = doc.CreateElement("table");
      table.SetAttributeValue("style", TableStyle);

      var thead = doc.CreateElement("thead");
      var tbody = doc.CreateElement("tbody");
      var tr = doc.CreateElement("tr");
      var th = doc.CreateElement("th");
      string title = isCategoryList ? "Category" : "Marking";
      th.InnerHtml = title;
      th.SetAttributeValue("style", HeaderStyle);
      tr.AppendChild(th);

      th = doc.CreateElement("th");
      th.InnerHtml = $"Time ({markingSettings.ValueUnitSuffix})";
      th.SetAttributeValue("style", HeaderStyle);
      tr.AppendChild(th);

      th = doc.CreateElement("th");
      th.InnerHtml = "Time (%)";
      th.SetAttributeValue("style", HeaderStyle);
      tr.AppendChild(th);
      thead.AppendChild(tr);
      table.AppendChild(thead);

      string header = $"| {title} | Time ({markingSettings.ValueUnitSuffix}) | Time (%) |";
      string separator = "|----------|--------|--------|";
      sb.AppendLine(header);
      sb.AppendLine(separator);

      foreach (var category in markingCategoryList) {
        if (category.Weight == TimeSpan.Zero && !includeOverview) {
          continue;
        }

        tr = doc.CreateElement("tr");
        var td = doc.CreateElement("td");
        td.InnerHtml = HttpUtility.HtmlEncode(category.Marking.TitleOrName);
        td.SetAttributeValue("style", CellStyle);
        tr.AppendChild(td);

        td = doc.CreateElement("td");
        td.InnerHtml = HttpUtility.HtmlEncode(category.Weight.TotalMilliseconds);
        td.SetAttributeValue("style", CellStyle);
        tr.AppendChild(td);

        td = doc.CreateElement("td");
        td.InnerHtml = HttpUtility.HtmlEncode(category.Percentage.AsPercentageString());
        td.SetAttributeValue("style", CellStyle);
        tr.AppendChild(td);
        tbody.AppendChild(tr);

        sb.AppendLine(
          $"| {category.Marking.TitleOrName} | {category.Weight.TotalMilliseconds} | {category.Percentage.AsPercentageString()} |");
      }

      table.AppendChild(tbody);
      doc.DocumentNode.AppendChild(table);
      AppendHtmlNewLine(doc, sb);
    }

    if (includeHottestFunctions) {
      // Add a table with the hottest functions overall.
      var hottestFuncts = new List<ProfileCallTreeNode>();
      var funcList = session.ProfileData.GetSortedFunctions();

      string funcTitle = $"Hottest {hotFuncLimit} Functions";
      AppendTitleParagraph(funcTitle, doc, sb);

      foreach (var pair in funcList.Take(hotFuncLimit)) {
        hottestFuncts.Add(session.ProfileData.CallTree.GetCombinedCallTreeNode(pair.Item1));
      }

      var hotFuncTable = ExportFunctionListAsHtmlTable(hottestFuncts, doc, session);
      doc.DocumentNode.AppendChild(hotFuncTable);

      sb.AppendLine();
      sb.AppendLine(ExportFunctionListAsMarkdownTable(hottestFuncts, session));
    }

    // Add a table for each category.
    foreach (var category in markingCategoryList) {
      if (category.SortedFunctions.Count == 0) {
        continue;
      }

      string time = markingSettings.FormatWeightValue(category.Weight);
      string percentage = category.Percentage.AsPercentageString();

      AppendHtmlNewLine(doc);
      AppendTitleParagraph(category.Marking.TitleOrName, doc, sb);
      AppendParagraph($"Time: {time} ({percentage})", doc, sb);

      var table = ExportFunctionListAsHtmlTable(category.SortedFunctions, doc, session);
      doc.DocumentNode.AppendChild(table);
      sb.AppendLine();

      string plainText = ExportFunctionListAsMarkdownTable(category.SortedFunctions, session);
      sb.AppendLine(plainText);
    }

    if (includeCategoriesTable) {
      AppendHtmlNewLine(doc, sb);
      AppendTitleParagraph(includeHottestFunctions ? "Categories Definitions" :
                             "Markings Definitions", doc);
      var table = doc.CreateElement("table");
      table.SetAttributeValue("style", TableStyle);

      var thead = doc.CreateElement("thead");
      var tbody = doc.CreateElement("tbody");
      var tr = doc.CreateElement("tr");

      string title = isCategoryList ? "Category" : "Marking";
      var th = doc.CreateElement("th");
      th.InnerHtml = title;
      th.SetAttributeValue("style", HeaderStyle);
      tr.AppendChild(th);

      th = doc.CreateElement("th");
      th.InnerHtml = "Pattern";
      th.SetAttributeValue("style", HeaderStyle);
      tr.AppendChild(th);
      thead.AppendChild(tr);
      table.AppendChild(thead);

      string header = $"| {title} | Pattern |";
      string separator = "|----------|--------|";
      sb.AppendLine(header);
      sb.AppendLine(separator);

      foreach (var category in markingCategoryList) {
        if (category.Weight == TimeSpan.Zero && !includeOverview) {
          continue;
        }

        tr = doc.CreateElement("tr");
        var td = doc.CreateElement("td");
        td.InnerHtml = HttpUtility.HtmlEncode(category.Marking.TitleOrName);
        td.SetAttributeValue("style", CellStyle);
        tr.AppendChild(td);

        td = doc.CreateElement("td");
        td.InnerHtml = HttpUtility.HtmlEncode(category.Marking.Name);
        td.SetAttributeValue("style", PatternCellStyle);
        tr.AppendChild(td);
        tbody.AppendChild(tr);

        sb.AppendLine($"| {category.Marking.TitleOrName} | {category.Marking.Name} |");
      }

      table.AppendChild(tbody);
      doc.DocumentNode.AppendChild(table);
    }

    var writer = new StringWriter();
    doc.Save(writer);
    return (writer.ToString(), sb.ToString());
  }

  private static void ExportTraceOverviewAsHtml(ISession session, HtmlDocument doc, StringBuilder sb) {
    var report = session.ProfileData.Report;

    if (report != null) {
      AppendTitleParagraph("Overview", doc, sb);
      AppendParagraph($"Trace File: {report.TraceInfo.TraceFilePath}", doc, sb);
      AppendParagraph($"Trace Duration: {report.TraceInfo.ProfileDuration}", doc, sb);
      AppendParagraph($"Process Name: {report.Process.Name}", doc, sb);
      AppendParagraph($"Process Id: {report.Process.ProcessId}", doc, sb);
      AppendParagraph($"Total Time: {session.ProfileData.TotalWeight}", doc, sb);
      AppendParagraph($"Total Time (ms): {session.ProfileData.TotalWeight.AsMillisecondsString(2, "")}", doc, sb);
      AppendHtmlNewLine(doc, sb);
    }
  }

  private static void AppendParagraph(string text, HtmlDocument doc, StringBuilder sb = null) {
    string SubtitleStyle =
      @"margin:5;text-align:left;font-family:Arial, sans-serif;font-size:14px;margin-top:0em";
    var paragraph = doc.CreateElement("p");
    paragraph.InnerHtml = HttpUtility.HtmlEncode(text);
    paragraph.SetAttributeValue("style", SubtitleStyle);
    doc.DocumentNode.AppendChild(paragraph);

    if (sb != null) {
      sb.AppendLine($"{text}  ");
    }
  }

  private static void AppendTitleParagraph(string text, HtmlDocument doc, StringBuilder sb = null) {
    string TitleStyle =
      @"margin:5;text-align:left;font-family:Arial, sans-serif;font-weight:bold;font-size:16px;margin-top:0em";
    var paragraph = doc.CreateElement("p");
    paragraph.InnerHtml = HttpUtility.HtmlEncode(text);
    paragraph.SetAttributeValue("style", TitleStyle);
    doc.DocumentNode.AppendChild(paragraph);

    if (sb != null) {
      sb.AppendLine($"**{text}**  ");
    }
  }

  private static void AppendHtmlNewLine(HtmlDocument doc, StringBuilder sb = null) {
    var newLineParagraph = doc.CreateElement("p");
    newLineParagraph.InnerHtml = "&nbsp;";
    newLineParagraph.SetAttributeValue("style", "margin:5;");
    doc.DocumentNode.AppendChild(newLineParagraph);

    if (sb != null) {
      sb.AppendLine("  ");
    }
  }

  public static async Task CopyFunctionMarkingsAsHtml(ISession session) {
    (string html, string plaintext) = await ExportFunctionMarkingsAsHtml(session);
    Utils.CopyHtmlToClipboard(html, plaintext);
  }

  public static void CopyFunctionMarkingsAsHtml(List<FunctionMarkingCategory> markings, ISession session) {
    (string html, string plaintext) = ExportFunctionMarkingsAsHtml(markings, session);
    Utils.CopyHtmlToClipboard(html, plaintext);
  }

  private static (string Html, string Plaintext)
    ExportFunctionMarkingsAsHtml(List<FunctionMarkingCategory> markings, ISession session) {
    return ExportProfilingReportAsHtml(markings, session, false, 0, true, false, true);
  }

  private static async Task<(string html, string plaintext)>
    ExportFunctionMarkingsAsHtml(ISession session) {
    var markingCategoryList = await Task.Run(() =>
                                               ProfilingUtils.CollectMarkedFunctions(
                                                 App.Settings.MarkingSettings.FunctionColors, false, session));
    return ExportProfilingReportAsHtml(markingCategoryList, session,
                                       false, 0, true, false, false);
  }

  private static async Task<(string html, string plaintext)>
    ExportModuleMarkingsAsHtml(ISession session) {
    var markingCategoryList = await Task.Run<List<FunctionMarkingCategory>>(() =>
                                                                              ProfilingUtils.
                                                                                CreateModuleMarkingCategories(
                                                                                  App.Settings.MarkingSettings,
                                                                                  session));
    return ExportProfilingReportAsHtml(markingCategoryList, session,
                                       false, 0, true, false, false);
  }

  public static async Task ExportFunctionMarkingsAsHtmlFile(ISession session) {
    (string html, _) = await ExportFunctionMarkingsAsHtml(session);
    await ExportFunctionMarkingsAsHtmlFile(html, session);
  }

  private static async Task ExportFunctionMarkingsAsHtmlFile(string text, ISession session) {
    string path = Utils.ShowSaveFileDialog("HTML file|*.html", "*.html|All Files|*.*");
    bool success = true;

    if (!string.IsNullOrEmpty(path)) {
      try {
        await File.WriteAllTextAsync(path, text);
      }
      catch (Exception ex) {
        Trace.WriteLine($"Failed to save marked functions report to {path}: {ex.Message}");
        success = false;
      }

      if (!success) {
        using var centerForm = new DialogCenteringHelper(Application.Current.MainWindow);
        MessageBox.Show($"Failed to save marked functions report to {path}", "Profile Explorer",
                        MessageBoxButton.OK, MessageBoxImage.Exclamation);
      }
    }
  }

  public static async Task ExportFunctionMarkingsAsMarkdownFile(ISession session) {
    (_, string plaintext) = await ExportFunctionMarkingsAsHtml(session);
    await ExportFunctionMarkingsAsMarkdownFile(plaintext, session);
  }

  private static async Task ExportFunctionMarkingsAsMarkdownFile(string text, ISession session) {
    string path = Utils.ShowSaveFileDialog("Markdown file|*.md", "*.md|All Files|*.*");
    bool success = true;

    if (!string.IsNullOrEmpty(path)) {
      try {
        await File.WriteAllTextAsync(path, text);
      }
      catch (Exception ex) {
        Trace.WriteLine($"Failed to save marked functions report to {path}: {ex.Message}");
        success = false;
      }

      if (!success) {
        using var centerForm = new DialogCenteringHelper(Application.Current.MainWindow);
        MessageBox.Show($"Failed to save marked functions report to {path}", "Profile Explorer",
                        MessageBoxButton.OK, MessageBoxImage.Exclamation);
      }
    }
  }

  public static async Task
    ExportFunctionMarkingsAsHtmlFile(List<FunctionMarkingCategory> categories, ISession session) {
    (string html, _) = ExportFunctionMarkingsAsHtml(categories, session);
    await ExportFunctionMarkingsAsHtmlFile(html, session);
  }

  public static async Task ExportFunctionMarkingsAsMarkdownFile(List<FunctionMarkingCategory> categories,
                                                                ISession session) {
    (_, string plaintext) = ExportFunctionMarkingsAsHtml(categories, session);
    await ExportFunctionMarkingsAsMarkdownFile(plaintext, session);
  }

  public static async Task CopyModuleMarkingsAsHtml(ISession session) {
    (string html, string plaintext) = await ExportModuleMarkingsAsHtml(session);
    Utils.CopyHtmlToClipboard(html, plaintext);
  }

  public static async Task ExportModuleMarkingsAsHtmlFile(ISession session) {
    (string html, _) = await ExportModuleMarkingsAsHtml(session);
    await ExportFunctionMarkingsAsHtmlFile(html, session);
  }

  public static async Task ExportModuleMarkingsAsMarkdownFile(ISession session) {
    (_, string plaintext) = await ExportModuleMarkingsAsHtml(session);
    await ExportFunctionMarkingsAsMarkdownFile(plaintext, session);
  }
}