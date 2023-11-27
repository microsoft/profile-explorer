// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls.Primitives;
using IRExplorerCore;
using IRExplorerCore.IR;
using IRExplorerUI.Compilers;
using IRExplorerUI.Document;
using OxyPlot;

namespace IRExplorerUI.Profile;

public class SourceDocumentMarker {
  private ProfileDocumentMarkerOptions options_;
  private ICompilerInfoProvider irInfo_;

  public SourceDocumentMarker(ProfileDocumentMarkerOptions options, ICompilerInfoProvider ir) {
    options_ = options;
    irInfo_ = ir;
  }

  public async Task Mark(MarkedDocument document, FunctionIR function) {
    var overlays = new List<IconElementOverlay>(function.InstructionCount);
    var inlineeOverlays = new List<IconElementOverlay>(function.InstructionCount);
    var lineLengths = new List<int>(function.InstructionCount);
    int maxLineLength = 0;

    await Task.Run(() => {
      foreach (var instr in function.AllInstructions) {
        // Annotate right-hand side with source line and inlinee info.
        var tag = instr.GetTag<SourceLocationTag>();

        if (tag == null) {
          continue;
        }

        string funcName = irInfo_.NameProvider.FormatFunctionName(function.Name);

        if (tag.Line != 0) {
          string label = $"{tag.Line}";
          string tooltip = $"Line number for {funcName}";
          var overlay = document.RegisterIconElementOverlay(instr, null, 16, 0, label, tooltip);
          overlay.IsLabelPinned = true;
          overlay.TextColor = options_.ElementOverlayTextColor;
          overlay.Background = options_.ElementOverlayBackColor;

          overlays.Add(overlay);
          lineLengths.Add(instr.TextLength);
        }

        if (!tag.HasInlinees) {
          continue;
        }

        var sb = new StringBuilder();
        var tooltipSb = new StringBuilder();
        tooltipSb.AppendLine("Inlined functions");
        tooltipSb.AppendLine("name:line (file) in call tree order:\n");

        for (int k = 0; k < tag.Inlinees.Count; k++) {
          var inlinee = tag.Inlinees[tag.Inlinees.Count - k - 1]; // Append backwards.
          string inlineeName = irInfo_.NameProvider.FormatFunctionName(inlinee.Function);
          sb.Append($"{inlineeName}:{tag.Inlinees[k].Line}");

          AppendInlineeTooltip(inlineeName, inlinee.Line, inlinee.FilePath, k, tooltipSb);
          tooltipSb.AppendLine();

          if (k != tag.Inlinees.Count - 1) {
            sb.Append("  |  ");
          }
        }

        // AppendInlineeTooltip(funcName, tag.Line, null, tag.Inlinees.Count, tooltipSb);
        var inlineeOverlay =
          document.RegisterIconElementOverlay(instr, null, 16, 0, sb.ToString(), tooltipSb.ToString());
        inlineeOverlay.TextColor = options_.InlineeOverlayTextColor;
        inlineeOverlay.Background = options_.ElementOverlayBackColor;
        inlineeOverlay.IsLabelPinned = true;
        inlineeOverlays.Add(inlineeOverlay);
      }

      lineLengths.Sort();
    });

    // Place the line numbers on a column aligned with most instrs.
    var settings = App.Settings.DocumentSettings;
    const double lengthPercentile = 0.9; // Consider length of most lines.
    const int overlayMargin = 20; // Distance from instruction end.
    const int inlineeOverlayMargin = 30;

    int percentileLength =
      lineLengths.Count > 0 ? lineLengths[(int)Math.Floor(lineLengths.Count * lengthPercentile)] : 0;
    double columnPosition = Utils.MeasureString(percentileLength, settings.FontName, settings.FontSize).Width;

    // Adjust position of all overlays.
    foreach (var overlay in overlays) {
      double position = Math.Max(options_.VirtualColumnPosition, columnPosition);
      overlay.VirtualColumn = position + overlayMargin;
    }

    foreach (var overlay in inlineeOverlays) {
      double position = Math.Max(options_.VirtualColumnPosition, columnPosition);
      overlay.VirtualColumn = position + overlayMargin + inlineeOverlayMargin;
    }
  }

  //private void MarkCallInstruction(InstructionIR instr, MarkedDocument document) {
  //  if (irInfo_.IR.IsCallInstruction(instr) &&
  //          irInfo_.IR.GetCallTarget(instr) is OperandIR callTargetOp &&
  //          callTargetOp.HasName) {
  //    var icon = IconDrawing.FromIconResource("ExecuteIcon");
  //    var overlay = document.RegisterIconElementOverlay(instr, icon, 16, 16);
  //    overlay.IsLabelPinned = false;
  //    overlay.UseLabelBackground = true;
  //    overlay.AlignmentX = System.Windows.HorizontalAlignment.Left;

  //    // Place before the call opcode.
  //    int lineOffset = lineOffset = instr.OpcodeLocation.Offset - instr.TextLocation.Offset;
  //    overlay.MarginX = Utils.MeasureString(lineOffset, App.Settings.DocumentSettings.FontName,
  //                                          App.Settings.DocumentSettings.FontSize).Width - 20;
  //    overlay.MarginY = 1;
  //  }
  //}

  private void AppendInlineeTooltip(string inlineeName, int inlineeLine, string inlineeFilePath,
                                    int index, StringBuilder tooltipSb) {
    string inlineeFileName = Utils.TryGetFileName(inlineeFilePath);

    if (inlineeName.Length > 80) {
      inlineeName = $"{inlineeName.Substring(0, 80)}...";
    }

    if (!string.IsNullOrEmpty(inlineeFileName)) {
      tooltipSb.Append($"{inlineeName}:{inlineeLine} ({inlineeFileName})");
    }
    else {
      tooltipSb.Append($"{inlineeName}:{inlineeLine}");
    }
  }
}
