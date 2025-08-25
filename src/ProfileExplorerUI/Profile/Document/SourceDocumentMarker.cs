// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using ProfileExplorerCore2.IR;
using ProfileExplorer.UI.Document;
using ProfileExplorerCore2.IR.Tags;
using ProfileExplorerCore2.Providers;

namespace ProfileExplorer.UI.Profile;

public class SourceDocumentMarker {
  private const int FunctionNameMaxLength = 80;
  private static readonly string SourceOverlayTag = "SourceTag";
  private SourceDocumentMarkerSettings settings_;
  private ICompilerInfoProvider irInfo_;

  public SourceDocumentMarker(SourceDocumentMarkerSettings settings, ICompilerInfoProvider ir) {
    settings_ = settings;
    irInfo_ = ir;
  }

  public async Task Mark(IRDocument document, FunctionIR function) {
    if (!settings_.AnnotateSourceLines && !settings_.AnnotateInlinees) {
      return;
    }

    var overlays = new List<IconElementOverlay>(function.InstructionCount);
    var inlineeOverlays = new List<IconElementOverlay>(function.InstructionCount);
    var lineLengths = new List<int>(function.InstructionCount);
    var lineToOperandMap = new Dictionary<int, OperandIR>();
    document.SuspendUpdate();

    await Task.Run(() => {
      // Cache the strings generated for each line and inlinee frame.
      string lineNumberTooltip = null;
      var inlineeLineNumberMap = new Dictionary<int, string>();
      var inlineeFrameMap = new Dictionary<SourceStackFrame, (string Title, string Tooltip)>();

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
          if (!inlineeLineNumberMap.TryGetValue(tag.Line, out string label)) {
            label = $"{tag.Line}";
            inlineeLineNumberMap[tag.Line] = label;
          }

          if (lineNumberTooltip == null) {
            string fileName = Utils.TryGetFileName(tag.FilePath);
            lineNumberTooltip = $"Line number for file {fileName}\nFile path: {tag.FilePath}";
          }

          var overlay = document.RegisterIconElementOverlay(instr, null, 16, 0, label, lineNumberTooltip);
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
          tooltipSb.AppendLine("name:line (file) in calling order:\n");

          var inlinees = tag.Inlinees;
          inlineeOverlays.EnsureCapacity(inlineeOverlays.Count + inlinees.Count);

          for (int k = 0; k < inlinees.Count; k++) {
            var inlinee = inlinees[inlinees.Count - k - 1]; // Append backwards.

            if (!inlineeFrameMap.TryGetValue(inlinee, out var inlineeText)) {
              string inlineeName = irInfo_.NameProvider.FormatFunctionName(inlinee.Function);

              inlineeText = new ValueTuple<string, string>();
              inlineeText.Title = $"{inlineeName}:{inlinees[k].Line}";
              inlineeText.Tooltip = MakeInlineeTooltip(inlineeName, inlinee.Line, inlinee.FilePath, k);
              inlineeFrameMap[inlinee] = inlineeText;
            }

            sb.Append(inlineeText.Title);
            tooltipSb.AppendLine(inlineeText.Tooltip);

            if (k != inlinees.Count - 1) {
              sb.Append("  |  ");
            }
          }

          // MakeInlineeTooltip(funcName, tag.Line, null, tag.Inlinees.Count, tooltipSb);
          var inlineeOverlay =
            document.RegisterIconElementOverlay(instr, null, 16, 0,
                                                sb.ToString().Trim(), tooltipSb.ToString().Trim());
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

    if (settings_.MarkCallTargets) {
      var callTypeFace = new Typeface(document.FontFamily, document.FontStyle, FontWeights.Bold, document.FontStretch);
      var colorizer = new OperandColorizer(lineToOperandMap, settings_.CallTargetTextColor.AsBrush(),
                                           settings_.CallTargetBackColor.AsBrush(), callTypeFace);
      document.RegisterTextTransformer(colorizer);
    }

    document.ResumeUpdate();
  }

  private void MarkCallInstruction(InstructionIR instr, IRDocument document,
                                   Dictionary<int, OperandIR> lineToOperandMap) {
    if (irInfo_.IR.IsCallInstruction(instr) &&
        irInfo_.IR.GetCallTarget(instr) is OperandIR callTargetOp &&
        callTargetOp.HasName) {
      // Mark only functions whose code is available in the session.
      if (DocumentUtils.FindCallTargetSection(callTargetOp, document.Section, document.Session) != null) {
        lineToOperandMap[instr.TextLocation.Line] = callTargetOp;
      }
    }
  }

  //? Currently calls are marked only with profiling data, here could be a mode
  //? that instead uses only the IR to mark calls.
  //private void MarkCallInstruction(InstructionIR instr, IRDocument document) {
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

  private string MakeInlineeTooltip(string inlineeName, int inlineeLine, string inlineeFilePath, int index) {
    string inlineeFileName = Utils.TryGetFileName(inlineeFilePath);
    inlineeName = inlineeName.TrimToLength(FunctionNameMaxLength);

    if (!string.IsNullOrEmpty(inlineeFileName)) {
      return $"{inlineeName}:{inlineeLine} ({inlineeFileName})";
    }
    else {
      return $"{inlineeName}:{inlineeLine}";
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

      if (start < line.Offset || end > line.EndOffset) {
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