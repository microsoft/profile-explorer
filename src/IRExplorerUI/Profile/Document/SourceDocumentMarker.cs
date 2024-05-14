// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using IRExplorerCore;
using IRExplorerCore.IR;
using IRExplorerUI.Compilers;
using IRExplorerUI.Document;
using OxyPlot;

namespace IRExplorerUI.Profile;

public class SourceDocumentMarker {
  private static readonly string SourceOverlayTag = "SourceTag";
  private const int FunctionNameMaxLength = 80;

  private SourceDocumentMarkerSettings settings_;
  private ICompilerInfoProvider irInfo_;

  public SourceDocumentMarker(SourceDocumentMarkerSettings settings, ICompilerInfoProvider ir) {
    settings_ = settings;
    irInfo_ = ir;
  }

  public async Task Mark(MarkedDocument document, FunctionIR function) {
    if (!settings_.AnnotateSourceLines && !settings_.AnnotateInlinees) {
      return;
    }

    
    var overlays = new List<IconElementOverlay>(function.InstructionCount);
    var inlineeOverlays = new List<IconElementOverlay>(function.InstructionCount);
    var lineLengths = new List<int>(function.InstructionCount);
    var lineToOperandMap = new Dictionary<int, OperandIR>();


    await Task.Run(() => {
      foreach (var instr in function.AllInstructions) {
        if (settings_.MarkCallTargets) {
          MarkCallInstruction(instr, document, lineToOperandMap);
        }

        // Annotate right-hand side with source line and inlinee info.
        var tag = instr.GetTag<SourceLocationTag>();

        if (tag == null) {
          continue;
        }

        if (tag.Line != 0 && settings_.AnnotateSourceLines) {
          string fileName = Utils.TryGetFileName(tag.FilePath);
          string label = $"{tag.Line}";
          string tooltip = $"Line number for file {fileName}";
          var overlay = document.RegisterIconElementOverlay(instr, null, 16, 0, label, tooltip);
          overlay.Tag = SourceOverlayTag;
          overlay.IsLabelPinned = true;
          overlay.AllowLabelEditing = false;
          overlay.TextColor = settings_.SourceLineTextColor.AsBrush();
          overlay.Background = settings_.SourceLineBackColor.AsBrush();

          overlays.Add(overlay);
          lineLengths.Add(instr.TextLength);
        }

        if (tag.HasInlinees && settings_.AnnotateInlinees) {
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
          inlineeOverlay.Tag = SourceOverlayTag;
          inlineeOverlay.TextColor = settings_.InlineeOverlayTextColor.AsBrush();
          inlineeOverlay.Background = settings_.SourceLineBackColor.AsBrush();
          inlineeOverlay.IsLabelPinned = true;
          inlineeOverlay.AllowLabelEditing = false;
          inlineeOverlays.Add(inlineeOverlay);
        }
      }

      lineLengths.Sort();
    });

    // Place the line numbers on a column aligned with most instrs.
    var settings = App.Settings.DocumentSettings;
    const double lengthPercentile = 0.9; // Consider length of most lines.
    const int overlayMargin = 10; // Distance from instruction end.
    const int inlineeOverlayMargin = 30;

    int percentileLength =
      lineLengths.Count > 0 ? lineLengths[(int)Math.Floor(lineLengths.Count * lengthPercentile)] : 0;
    double columnPosition = Utils.MeasureString(percentileLength, settings.FontName, settings.FontSize).Width;

    // Adjust position of all overlays.
    foreach (var overlay in overlays) {
      double position = Math.Max(settings_.VirtualColumnPosition, columnPosition);
      overlay.VirtualColumn = position + overlayMargin;
    }

    foreach (var overlay in inlineeOverlays) {
      double position = Math.Max(settings_.VirtualColumnPosition, columnPosition);
      overlay.VirtualColumn = position + overlayMargin + inlineeOverlayMargin;
    }
    
    if(settings_.MarkCallTargets) {
      var colorizer = new OperandColorizer(lineToOperandMap, settings_.CallTargetTextColor.AsBrush(),
                                           settings_.CallTargetBackColor.AsBrush());
      document.RegisterTextColorizer(colorizer);
    }
  }

  private void MarkCallInstruction(InstructionIR instr, MarkedDocument document,
                                   Dictionary<int, OperandIR> lineToOperandMap) {
    if (irInfo_.IR.IsCallInstruction(instr) &&
        irInfo_.IR.GetCallTarget(instr) is OperandIR callTargetOp &&
        callTargetOp.HasName) {
      // Mark only functions whose code is available in the session.
      if(DocumentUtils.FindCallTargetSection(callTargetOp, document.Section, document.Session) != null) {
        lineToOperandMap[instr.TextLocation.Line] = callTargetOp;
      }
    }
  }

  //? Currently calls are marked only with profiling data, here could be a mode
  //? that instead uses only the IR to mark calls.
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
    inlineeName = inlineeName.TrimToLength(FunctionNameMaxLength);

    if (!string.IsNullOrEmpty(inlineeFileName)) {
      tooltipSb.Append($"{inlineeName}:{inlineeLine} ({inlineeFileName})");
    }
    else {
      tooltipSb.Append($"{inlineeName}:{inlineeLine}");
    }
  }
  
  // Use to mark the call target function names.
  public sealed class OperandColorizer : DocumentColorizingTransformer {
    private Dictionary<int, OperandIR> lineToOperandMap_;
    private Brush textColor_;
    private Brush backColor_;
    private Typeface typeface_;

    public OperandColorizer(Dictionary<int, OperandIR> lineToOperandMap,
                            Brush textColor, Brush backColor, Typeface typeface = null) {
      lineToOperandMap_ = lineToOperandMap;
      textColor_ = textColor;
      backColor_ = backColor;
      typeface_ = typeface;
    }

    protected override void ColorizeLine(DocumentLine line) {
      if (line.Length == 0) {
        return;
      }

      if (!lineToOperandMap_.TryGetValue(line.LineNumber - 1, out var operand)) {
        return;
      }

      int start = operand.TextLocation.Offset;
      int end = start + operand.TextLength;
      
      if(start < line.Offset || end > line.EndOffset) {
        return;
      }
      
      ChangeLinePart(start, end, element => {
        element.TextRunProperties.SetTextDecorations(TextDecorations.Underline);

        if (textColor_ != null) {
          element.TextRunProperties.SetForegroundBrush(textColor_);
        }

        if (backColor_ != null) {
          element.TextRunProperties.SetBackgroundBrush(backColor_);
        }
        
        if (typeface_ != null) {
          element.TextRunProperties.SetTypeface(typeface_);
        }
      });
    }
  }
}