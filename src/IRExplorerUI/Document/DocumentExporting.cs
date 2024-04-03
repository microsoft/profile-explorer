// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClosedXML.Excel;
using HtmlAgilityPack;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using IRExplorerCore;
using IRExplorerCore.Analysis;
using IRExplorerCore.IR;

namespace IRExplorerUI.Document;

public static class DocumentExporting {
  private static string ExcelFileFilter = "Excel Worksheets|*.xlsx";
  private static string HtmlFileFilter = "HTML file|*.html";
  private static string MarkdownFileFilter = "Markdown file|*.md";
  private static string ExcelExtension = "*.xlsx|All Files|*.*";
  private static string HtmlExtension = "*.html|All Files|*.*";
  private static string MarkdownExtension = "*.md|All Files|*.*";

  public static async Task ExportToExcelFile(IRDocument textView, Func<IRDocument, string, Task<bool>> saveAction) {
    await ExportToFile(textView, ExcelFileFilter, ExcelExtension, saveAction);
  }

  public static async Task ExportToHtmlFile(IRDocument textView, Func<IRDocument, string, Task<bool>> saveAction) {
    await ExportToFile(textView, HtmlFileFilter, HtmlExtension, saveAction);
  }

  public static async Task ExportToMarkdownFile(IRDocument textView, Func<IRDocument, string, Task<bool>> saveAction) {
    await ExportToFile(textView, MarkdownFileFilter, MarkdownExtension, saveAction);
  }

  private static void ExportToFile(IRDocument textView, string fileFilter, string defaultExtension,
                                   Func<IRDocument, string, bool> saveAction) {
    string path = Utils.ShowSaveFileDialog(fileFilter, defaultExtension);
    bool success = true;

    if (!string.IsNullOrEmpty(path)) {
      try {
        success = saveAction(textView, path);
      }
      catch (Exception ex) {
        Trace.WriteLine($"Failed to save function to {path}: {ex.Message}");
      }

      if (!success) {
        using var centerForm = new DialogCenteringHelper(textView);
        MessageBox.Show($"Failed to save list to {path}", "IR Explorer",
                        MessageBoxButton.OK, MessageBoxImage.Exclamation);
      }
    }
  }

  public static async Task ExportToFile(IRDocument textView, string fileFilter, string defaultExtension,
                                        Func<IRDocument, string, Task<bool>> saveAction) {
    string path = Utils.ShowSaveFileDialog(fileFilter, defaultExtension);
    bool success = true;

    if (!string.IsNullOrEmpty(path)) {
      try {
        success = await saveAction(textView, path);
      }
      catch (Exception ex) {
        Trace.WriteLine($"Failed to save function to {path}: {ex.Message}");
      }

      if (!success) {
        using var centerForm = new DialogCenteringHelper(textView);
        MessageBox.Show($"Failed to save list to {path}", "IR Explorer",
                        MessageBoxButton.OK, MessageBoxImage.Exclamation);
      }
    }
  }

  public static async Task<bool> ExportSourceAsExcelFile(IRDocument textView, string filePath) {
    var function = textView.Section.ParentFunction;
    var (firstSourceLineIndex, lastSourceLineIndex) =
      await DocumentUtils.FindFunctionSourceLineRange(function, textView);

    if (firstSourceLineIndex == 0) {
      return false;
    }

    var wb = new XLWorkbook();
    var ws = wb.Worksheets.Add("Source");
    var columnData = textView.ProfileColumnData;
    int rowId = 2; // First row is for the table column names.
    int maxLineLength = 0;

    for (int i = firstSourceLineIndex; i <= lastSourceLineIndex; i++) {
      var line = textView.Document.GetLineByNumber(i);
      string text = textView.Document.GetText(line.Offset, line.Length);
      ws.Cell(rowId, 1).Value = text;
      ws.Cell(rowId, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
      maxLineLength = Math.Max(text.Length, maxLineLength);

      ws.Cell(rowId, 2).Value = i;
      ws.Cell(rowId, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
      ws.Cell(rowId, 2).Style.Font.FontColor = XLColor.DarkGreen;

      if (columnData != null) {
        IRElement tuple = null;
        tuple = DocumentUtils.FindTupleOnSourceLine(i, textView);

        if (tuple != null) {
          columnData.ExportColumnsToExcel(tuple, ws, rowId, 3);
        }
      }

      rowId++;
    }

    var firstCell = ws.Cell(1, 1);
    var lastCell = ws.LastCellUsed();
    var range = ws.Range(firstCell.Address, lastCell.Address);
    var table = range.CreateTable();
    table.Theme = XLTableTheme.None;

    foreach (var cell in table.HeadersRow().Cells()) {
      if (cell.Address.ColumnNumber == 1) {
        cell.Value = "Source";
      }
      else if (cell.Address.ColumnNumber == 2) {
        cell.Value = "Line";
      }
      else if (columnData != null && cell.Address.ColumnNumber - 3 < columnData.Columns.Count) {
        cell.Value = columnData.Columns[cell.Address.ColumnNumber - 3].Title;
      }

      cell.Style.Font.Bold = true;
      cell.Style.Fill.BackgroundColor = XLColor.LightGray;
    }

    for (int i = 1; i <= 1; i++) {
      ws.Column(i).AdjustToContents((double)1, maxLineLength);
    }

    wb.SaveAs(filePath);
    return true;
  }

  public static async Task<bool> ExportSourceAsHtmlFile(IRDocument textView, string filePath) {
    try {
      Trace.WriteLine("ExportFunctionAsHtmlFile");
      var doc = new HtmlDocument();
      string TitleStyle =
        @"text-align:left;font-family:Arial, sans-serif;font-weight:bold;font-size:16px;margin-top:0em";

      var p = doc.CreateElement("p");
      var function = textView.Section.ParentFunction;
      var funcName = function.FormatFunctionName(textView.Session);
      p.InnerHtml = $"Function: {HttpUtility.HtmlEncode(funcName)}";
      p.SetAttributeValue("style", TitleStyle);
      doc.DocumentNode.AppendChild(p);

      p = doc.CreateElement("p");
      p.InnerHtml = $"Module: {HttpUtility.HtmlEncode(function.ModuleName)}";
      p.SetAttributeValue("style", TitleStyle);
      doc.DocumentNode.AppendChild(p);

      var node = await ExportSourceAsHtml(textView);
      doc.DocumentNode.AppendChild(node);
      var writer = new StringWriter();
      doc.Save(writer);
      await File.WriteAllTextAsync(filePath, writer.ToString());
      return true;
    }
    catch (Exception ex) {
      Trace.WriteLine($"Failed to export to HTML file: {filePath}, {ex.Message}");
      return false;
    }
  }

  public static async Task<HtmlNode> ExportSourceAsHtml(IRDocument textView, int startLine = -1, int endLine = -1) {
    string TableStyle = @"border-collapse:collapse;border-spacing:0;";
    string HeaderStyle =
      @"background-color:#D3D3D3;white-space:nowrap;text-align:left;vertical-align:top;border-color:black;border-style:solid;border-width:1px;overflow:hidden;padding:2px 2px;font-size:14px;font-family:Arial, sans-serif;";
    string CellStyle =
      @"text-align:left;vertical-align:top;word-wrap:break-word;max-width:500px;overflow:hidden;padding:2px 2px;border-color:black;border-style:solid;border-width:1px;font-size:14px;font-family:Arial, sans-serif;";
    string LineNumberStyle =
      @"color:#006400;text-align:left;vertical-align:top;word-wrap:break-word;max-width:300px;overflow:hidden;padding:2px 2px;border-color:black;border-style:solid;border-width:1px;font-size:14px;font-family:Arial, sans-serif;";

    var function = textView.Section.ParentFunction;
    var (firstSourceLineIndex, lastSourceLineIndex) =
      await DocumentUtils.FindFunctionSourceLineRange(function, textView);

    var columnData = textView.ProfileColumnData;
    bool filterByLine = startLine != -1 && endLine != -1;
    int maxColumn = 2 + (columnData != null ? columnData.Columns.Count : 0);
    var doc = new HtmlDocument();
    var table = doc.CreateElement("table");
    table.SetAttributeValue("style", TableStyle);

    var thead = doc.CreateElement("thead");
    var tbody = doc.CreateElement("tbody");
    var tr = doc.CreateElement("tr");

    var th = doc.CreateElement("th");
    th.InnerHtml = "Source";
    th.SetAttributeValue("style", HeaderStyle);
    tr.AppendChild(th);
    th = doc.CreateElement("th");
    th.InnerHtml = "Line";
    th.SetAttributeValue("style", HeaderStyle);
    tr.AppendChild(th);

    if (columnData != null) {
      foreach (var column in columnData.Columns) {
        th = doc.CreateElement("th");
        th.InnerHtml = HttpUtility.HtmlEncode(column.Title);
        th.SetAttributeValue("style", HeaderStyle);
        tr.AppendChild(th);
      }
    }

    thead.AppendChild(tr);
    table.AppendChild(thead);

    for (int i = firstSourceLineIndex; i <= lastSourceLineIndex; i++) {
      // Filter out instructions not in line range if requested.
      if (filterByLine && (i < startLine || i > endLine)) {
        continue;
      }

      var line = textView.Document.GetLineByNumber(i);
      string text = textView.Document.GetText(line.Offset, line.Length);
      var tuple = columnData != null ? DocumentUtils.FindTupleOnSourceLine(i, textView) : null;

      tr = doc.CreateElement("tr");
      var td = doc.CreateElement("td");
      td.InnerHtml = PreprocessHtmlIndentation(text);
      td.SetAttributeValue("style", CellStyle);
      tr.AppendChild(td);

      td = doc.CreateElement("td");
      td.InnerHtml = HttpUtility.HtmlEncode(i);
      td.SetAttributeValue("style", LineNumberStyle);
      tr.AppendChild(td);

      if (columnData != null && tuple != null) {
        columnData.ExportColumnsAsHTML(tuple, doc, tr);
      }
      else {
        // Use empty cells for lines without data.
        for (int k = 2; k < maxColumn; k++) {
          td = doc.CreateElement("td");
          td.InnerHtml = "";
          td.SetAttributeValue("style", CellStyle);
          tr.AppendChild(td);
        }
      }

      tbody.AppendChild(tr);
    }

    table.AppendChild(tbody);
    doc.DocumentNode.AppendChild(table);
    return doc.DocumentNode;
  }

  public static async Task<bool> ExportSourceAsMarkdownFile(IRDocument textView, string filePath) {
    try {
      var text = await ExportSourceAsMarkdown(textView);
      await File.WriteAllTextAsync(filePath, text);
      return true;
    }
    catch (Exception ex) {
      Trace.WriteLine($"Failed to export to Markdown file: {filePath}, {ex.Message}");
      return false;
    }
  }

  public static async Task<string> ExportSourceAsMarkdown(IRDocument textView, int startLine = -1, int endLine = -1) {
    var sb = new StringBuilder();
    string header =    "| Source | Line |";
    string separator = "|--------|------|";

    var function = textView.Section.ParentFunction;
    var (firstSourceLineIndex, lastSourceLineIndex) =
      await DocumentUtils.FindFunctionSourceLineRange(function, textView);

    var columnData = textView.ProfileColumnData;
    int maxColumn = 2 + (columnData != null ? columnData.Columns.Count : 0);
    bool filterByLine = startLine != -1 && endLine != -1;

    if (columnData != null) {
      foreach (var column in columnData.Columns) {
        header += $" {column.Title} |";
        separator += $"{new string('-', column.Title.Length)}|";
      }
    }

    sb.AppendLine(header);
    sb.AppendLine(separator);

    for (int i = firstSourceLineIndex; i <= lastSourceLineIndex; i++) {
      // Filter out instructions not in line range if requested.
      if (filterByLine && (i < startLine || i > endLine)) {
        continue;
      }

      var line = textView.Document.GetLineByNumber(i);
      string text = textView.Document.GetText(line.Offset, line.Length);
      var tuple = columnData != null ? DocumentUtils.FindTupleOnSourceLine(i, textView) : null;

      sb.Append($"| {PreprocessHtmlIndentation(text)} | {i} |");

      if (columnData != null && tuple != null) {
        columnData.ExportColumnsAsMarkdown(tuple, sb);
      }
      else {
        for (int k = 2; k < maxColumn; k++) {
          sb.Append(" |");
        }
      }

      sb.AppendLine();
    }

    return sb.ToString();
  }

  private static string PreprocessHtmlIndentation(string text) {
    var sb = new StringBuilder();

    for (int i = 0; i < text.Length; i++) {
      if (text[i] == ' ') {
        sb.Append("&nbsp;");
      }
      else if (text[i] == '\t') {
        sb.Append("&emsp;");
      }
      else {
        sb.Append(text[i]);
      }
    }

    return sb.ToString();
  }

  public static async Task<bool> ExportFunctionAsMarkdownFile(IRDocument textView, string filePath) {
    try {
      var text = ExportFunctionAsMarkdown(textView);
      await File.WriteAllTextAsync(filePath, text);
      return true;
    }
    catch (Exception ex) {
      Trace.WriteLine($"Failed to export to Markdown file: {filePath}, {ex.Message}");
      return false;
    }
  }

  public static async Task<bool> ExportFunctionAsHtmlFile(IRDocument textView, string filePath) {
    try {
      Trace.WriteLine("ExportFunctionAsHtmlFile");
      var doc = new HtmlDocument();
      string TitleStyle =
        @"text-align:left;font-family:Arial, sans-serif;font-weight:bold;font-size:16px;margin-top:0em";

      var p = doc.CreateElement("p");
      var funcName = textView.Section.FormatFunctionName(textView.Session);

      p.InnerHtml = $"Function: {HttpUtility.HtmlEncode(funcName)}";
      p.SetAttributeValue("style", TitleStyle);
      doc.DocumentNode.AppendChild(p);

      p = doc.CreateElement("p");
      p.InnerHtml = $"Module: {HttpUtility.HtmlEncode(textView.Section.ModuleName)}";
      p.SetAttributeValue("style", TitleStyle);
      doc.DocumentNode.AppendChild(p);

      doc.DocumentNode.AppendChild(ExportFunctionAsHtml(textView));
      var writer = new StringWriter();
      doc.Save(writer);
      await File.WriteAllTextAsync(filePath, writer.ToString());
      return true;
    }
    catch (Exception ex) {
      Trace.WriteLine($"Failed to export to HTML file: {filePath}, {ex.Message}");
      return false;
    }
  }

  public static async Task CopySelectedLinesAsHtml(IRDocument textView) {
    int startLine = textView.TextArea.Selection.StartPosition.Line - 1;
    int endLine = textView.TextArea.Selection.EndPosition.Line - 1;

    if (startLine > endLine) {
      // Happens when selecting bottom-up.
      (startLine, endLine) = (endLine, startLine);
    }

    var doc = new HtmlDocument();
    doc.DocumentNode.AppendChild(ExportFunctionAsHtml(textView, false, startLine, endLine));
    var writer = new StringWriter();
    doc.Save(writer);

    // Also save as Markdown so that it can be pasted in plain text editors.
    //var plainText = ExportFunctionListAsMarkdown(funcList);
    string plainText = ExportFunctionAsMarkdown(textView, false, startLine, endLine);
    Utils.CopyHtmlToClipboard(writer.ToString(), plainText);
  }

  private static HtmlNode ExportFunctionAsHtml(IRDocument textView, bool includeBlocks = true,
                                               int startLine = -1, int endLine = -1) {
    string TableStyle = @"border-collapse:collapse;border-spacing:0;";
    string HeaderStyle =
      @"background-color:#D3D3D3;white-space:nowrap;text-align:left;vertical-align:top;border-color:black;border-style:solid;border-width:1px;overflow:hidden;padding:2px 2px;font-size:14px;font-family:Arial, sans-serif;";
    string CellStyle =
      @"text-align:left;vertical-align:top;word-wrap:break-word;max-width:500px;overflow:hidden;padding:2px 2px;border-color:black;border-style:solid;border-width:1px;font-size:14px;font-family:Arial, sans-serif;";
    string BlockStyle =
      @"color:#00008B;font-weight:bold;text-align:left;vertical-align:top;word-wrap:break-word;max-width:300px;overflow:hidden;padding:2px 2px;border-color:black;border-style:solid;border-width:1px;font-size:14px;font-family:Arial, sans-serif;";
    string LineNumberStyle =
      @"color:#006400;text-align:left;vertical-align:top;word-wrap:break-word;max-width:300px;overflow:hidden;padding:2px 2px;border-color:black;border-style:solid;border-width:1px;font-size:14px;font-family:Arial, sans-serif;";

    var columnData = textView.ProfileColumnData;
    bool filterByLine = startLine != -1 && endLine != -1;
    int maxColumn = 2 + (columnData != null ? columnData.Columns.Count : 0);
    int rowId = 1; // First row is for the table column names.
    var doc = new HtmlDocument();
    var table = doc.CreateElement("table");
    table.SetAttributeValue("style", TableStyle);

    var thead = doc.CreateElement("thead");
    var tbody = doc.CreateElement("tbody");
    var tr = doc.CreateElement("tr");

    var th = doc.CreateElement("th");
    th.InnerHtml = "Instruction";
    th.SetAttributeValue("style", HeaderStyle);
    tr.AppendChild(th);
    th = doc.CreateElement("th");
    th.InnerHtml = "Line";
    th.SetAttributeValue("style", HeaderStyle);
    tr.AppendChild(th);

    if (columnData != null) {
      foreach (var column in columnData.Columns) {
        th = doc.CreateElement("th");
        th.InnerHtml = HttpUtility.HtmlEncode(column.Title);
        th.SetAttributeValue("style", HeaderStyle);
        tr.AppendChild(th);
      }
    }

    thead.AppendChild(tr);
    table.AppendChild(thead);

    void AddSimpleRow(string text, string style) {
      tr = doc.CreateElement("tr");
      var td = doc.CreateElement("td");
      td.InnerHtml = HttpUtility.HtmlEncode(text);
      td.SetAttributeValue("style", style);
      tr.AppendChild(td);

      for (int i = 1; i < maxColumn; i++) {
        td = doc.CreateElement("td");
        td.SetAttributeValue("style", BlockStyle);
        tr.AppendChild(td);
      }

      tbody.AppendChild(tr);
    }

    foreach (var block in textView.Function.Blocks) {
      bool addedBlockRow = false;

      if (includeBlocks && !filterByLine) {
        AddSimpleRow($"Block {block.Number}", BlockStyle);
        addedBlockRow = true;
      }

      foreach (var tuple in block.Tuples) {
        // Filter out instructions not in line range if requested.
        if (filterByLine) {
          if (tuple.TextLocation.Line < startLine ||
              tuple.TextLocation.Line > endLine) {
            continue;
          }

          if (!addedBlockRow) {
            AddSimpleRow($"Block {block.Number}", BlockStyle);
            addedBlockRow = true;

            if (tuple.IndexInBlock > 0) {
              AddSimpleRow($"...", CellStyle);
            }
          }
        }

        rowId++;
        var line = textView.Document.GetLineByNumber(tuple.TextLocation.Line + 1);
        string text = textView.Document.GetText(line.Offset, line.Length);
        tr = doc.CreateElement("tr");
        var td = doc.CreateElement("td");
        td.InnerHtml = HttpUtility.HtmlEncode(text);
        td.SetAttributeValue("style", CellStyle);
        tr.AppendChild(td);
        td = doc.CreateElement("td");

        var sourceTag = tuple.GetTag<SourceLocationTag>();

        if (sourceTag != null) {
          td.InnerHtml = HttpUtility.HtmlEncode(sourceTag.Line);
        }

        td.SetAttributeValue("style", LineNumberStyle);
        tr.AppendChild(td);

        if (columnData != null) {
          columnData.ExportColumnsAsHTML(tuple, doc, tr);
        }

        tbody.AppendChild(tr);
      }
    }

    table.AppendChild(tbody);
    doc.DocumentNode.AppendChild(table);
    return doc.DocumentNode;
  }

  private static string ExportFunctionAsMarkdown(IRDocument textView, bool includeBlocks = true,
                                                 int startLine = -1, int endLine = -1) {
    var sb = new StringBuilder();
    string header    = "| Instruction | Line |";
    string separator = "|-------------|------|";

    var columnData = textView.ProfileColumnData;
    int maxColumn = 2 + (columnData != null ? columnData.Columns.Count : 0);
    bool filterByLine = startLine != -1 && endLine != -1;

    if (columnData != null) {
      foreach (var column in columnData.Columns) {
        header += $" {column.Title} |";
        separator += $"{new string('-', column.Title.Length)}|";
      }
    }

    sb.AppendLine(header);
    sb.AppendLine(separator);

    void AddSimpleRow(string text) {
      sb.Append($"| {text} |");

      for (int i = 1; i < maxColumn; i++) {
        sb.Append(" |");
      }

      sb.AppendLine();
    }

    foreach (var block in textView.Function.Blocks) {
      bool addedBlockRow = false;

      if (includeBlocks && !filterByLine) {
        AddSimpleRow($"Block {block.Number}");
        addedBlockRow = true;
      }

      foreach (var tuple in block.Tuples) {
        // Filter out instructions not in line range if requested.
        if (filterByLine) {
          if (tuple.TextLocation.Line < startLine ||
              tuple.TextLocation.Line > endLine) {
            continue;
          }

          if (!addedBlockRow) {
            AddSimpleRow($"Block {block.Number}");
            addedBlockRow = true;

            if (tuple.IndexInBlock > 0) {
              AddSimpleRow($"...");
            }
          }
        }

        var line = textView.Document.GetLineByNumber(tuple.TextLocation.Line + 1);
        string text = textView.Document.GetText(line.Offset, line.Length);
        var sourceTag = tuple.GetTag<SourceLocationTag>();
        var sourceLine = sourceTag != null ? sourceTag.Line.ToString() : "";
        sb.Append($"| {text} | {sourceLine} |");

        if (columnData != null) {
          columnData.ExportColumnsAsMarkdown(tuple, sb);
        }

        sb.AppendLine();
      }
    }

    return sb.ToString();
  }


  public static async Task<bool> ExportFunctionAsExcelFile(IRDocument textView, string filePath) {
    var wb = new XLWorkbook();
    var ws = wb.Worksheets.Add("Function");
    var columnData = textView.ProfileColumnData;
    int rowId = 1; // First row is for the table column names.
    int maxColumn = 2 + (columnData != null ? columnData.Columns.Count : 0);
    int maxLineLength = 0;

    foreach (var block in textView.Function.Blocks) {
      rowId++;
      ws.Cell(rowId, 1).Value = $"Block {block.Number}";
      ws.Cell(rowId, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
      ws.Cell(rowId, 1).Style.Font.Bold = true;
      ws.Cell(rowId, 1).Style.Font.FontColor = XLColor.DarkBlue;

      for (int i = 1; i <= maxColumn; i++) {
        ws.Cell(rowId, i).Style.Border.BottomBorder = XLBorderStyleValues.Thin;
      }

      foreach (var tuple in block.Tuples) {
        rowId++;
        var line = textView.Document.GetLineByNumber(tuple.TextLocation.Line + 1);
        string text = textView.Document.GetText(line.Offset, line.Length);
        ws.Cell(rowId, 1).Value = text;
        ws.Cell(rowId, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        maxLineLength = Math.Max(text.Length, maxLineLength);

        var sourceTag = tuple.GetTag<SourceLocationTag>();

        if (sourceTag != null) {
          ws.Cell(rowId, 2).Value = sourceTag.Line;
          ws.Cell(rowId, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
          ws.Cell(rowId, 2).Style.Font.FontColor = XLColor.DarkGreen;
        }

        if (columnData != null) {
          columnData.ExportColumnsToExcel(tuple, ws, rowId, 3);
        }
      }
    }

    var firstCell = ws.Cell(1, 1);
    var lastCell = ws.LastCellUsed();
    var range = ws.Range(firstCell.Address, lastCell.Address);
    var table = range.CreateTable();
    table.Theme = XLTableTheme.None;

    foreach (var cell in table.HeadersRow().Cells()) {
      if (cell.Address.ColumnNumber == 1) {
        cell.Value = "Instruction";
      }
      else if (cell.Address.ColumnNumber == 2) {
        cell.Value = "Line";
      }
      else if (columnData != null && cell.Address.ColumnNumber - 3 < columnData.Columns.Count) {
        cell.Value = columnData.Columns[cell.Address.ColumnNumber - 3].Title;
      }

      cell.Style.Font.Bold = true;
      cell.Style.Fill.BackgroundColor = XLColor.LightGray;
    }

    for (int i = 1; i <= 1; i++) {
      ws.Column(i).AdjustToContents((double)1, maxLineLength);
    }

    await Task.Run(() => wb.SaveAs(filePath));
    return true;
  }

  public static async Task CopySelectedSourceLinesAsHtml(IRDocument textView) {
    int startLine = textView.TextArea.Selection.StartPosition.Line;
    int endLine = textView.TextArea.Selection.EndPosition.Line;

    if (startLine > endLine) {
      // Happens when selecting bottom-up.
      (startLine, endLine) = (endLine, startLine);
    }

    var doc = new HtmlDocument();
    doc.DocumentNode.AppendChild(await DocumentExporting.ExportSourceAsHtml(textView, startLine, endLine));
    var writer = new StringWriter();
    doc.Save(writer);

    // Also save as Markdown so that it can be pasted in plain text editors.
    //var plainText = ExportFunctionListAsMarkdown(funcList);
    string plainText = await DocumentExporting.ExportSourceAsMarkdown(textView, startLine, endLine);
    Utils.CopyHtmlToClipboard(writer.ToString(), plainText);
  }
}