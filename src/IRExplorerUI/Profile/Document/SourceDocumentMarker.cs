// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using IRExplorerCore;
using IRExplorerCore.IR;
using System.Text;
using System.Threading.Tasks;
using IRExplorerUI.Document;
using IRExplorerUI.Compilers;

//? TODO: EXTRACT SOURCE LOCATION MARKING TO OWN CLASS NOT PROFILE-DEPENDENT

namespace IRExplorerUI.Profile {
    public class SourceDocumentMarker {
        private ProfileDocumentMarkerOptions options_;
        private ICompilerIRInfo ir_;

        public SourceDocumentMarker(ProfileDocumentMarkerOptions options, ICompilerIRInfo ir) {
            options_ = options;
            ir_ = ir;
        }

        public Task Mark(IRDocument document, FunctionIR function) {
            var overlays = new List<IconElementOverlay>(function.InstructionCount);
            var inlineeOverlays = new List<IconElementOverlay>(function.InstructionCount);
            var lineLengths = new List<int>(function.InstructionCount);
            int maxLineLength = 0;

            foreach (var element in function.AllInstructions) {
                var tag = element.GetTag<SourceLocationTag>();

                if (tag == null) {
                    continue;
                }

                var funcName = PDBDebugInfoProvider.DemangleFunctionName(function.Name, FunctionNameDemanglingOptions.OnlyName);

                if (tag.Line != 0) {
                    var label = $"{tag.Line}";
                    var tooltip = $"Line number for {funcName}";
                    var overlay = document.RegisterIconElementOverlay(element, null, 16, 0, label, tooltip);
                    overlay.IsLabelPinned = true;
                    overlay.TextColor = options_.ElementOverlayTextColor;
                    overlay.Background = options_.ElementOverlayBackColor;

                    overlays.Add(overlay);
                    lineLengths.Add(element.TextLength);
                }

                if (!tag.HasInlinees) {
                    continue;
                }

                var sb = new StringBuilder();
                var tooltipSb = new StringBuilder();

                for (int k = 0; k < tag.Inlinees.Count; k++) {
                    var inlinee = tag.Inlinees[k];
                    var inlineeName = PDBDebugInfoProvider.DemangleFunctionName(inlinee.Function, FunctionNameDemanglingOptions.OnlyName);
                    sb.Append($"{inlineeName}:{tag.Inlinees[k].Line}");

                    AppendInlineeTooltip(inlineeName, inlinee.Line, inlinee.FilePath, k, tooltipSb);
                    tooltipSb.AppendLine();

                    if (k != tag.Inlinees.Count - 1) {
                        sb.Append("  |  ");
                    }
                }

                AppendInlineeTooltip(funcName, tag.Line, null, tag.Inlinees.Count, tooltipSb);
                var inlineeOverlay = document.RegisterIconElementOverlay(element, null, 16, 0, sb.ToString(), tooltipSb.ToString());
                inlineeOverlay.TextColor = options_.InlineeOverlayTextColor;
                inlineeOverlay.Background = options_.ElementOverlayBackColor;
                inlineeOverlay.IsLabelPinned = true;
                inlineeOverlays.Add(inlineeOverlay);
            }

            // Place the line numbers on a column aligned with most instrs.
            var settings = App.Settings.DocumentSettings;
            const double lengthPercentile = 0.9;
            const int overlayMargin = 20;
            const int inlineeOverlayMargin = 30;

            lineLengths.Sort();
            int percentileLength = lineLengths.Count > 0 ? lineLengths[(int)Math.Floor(lineLengths.Count * lengthPercentile)] : 0;
            double columnPosition = Utils.MeasureString(percentileLength, settings.FontName, settings.FontSize).Width;

            foreach (var overlay in overlays) {
                if (overlay.Element.TextLength > percentileLength) {
                    //? adjust
                }

                double position = Math.Max(options_.VirtualColumnPosition, columnPosition);
                overlay.VirtualColumn = position + overlayMargin;
            }

            foreach (var overlay in inlineeOverlays) {
                double position = Math.Max(options_.VirtualColumnPosition, columnPosition);
                overlay.VirtualColumn = position + overlayMargin + inlineeOverlayMargin;
            }

            return Task.CompletedTask;
        }

        private void AppendInlineeTooltip(string inlineeName, int inlineeLine, string inlineeFilePath,
                                          int index, StringBuilder tooltipSb) {
            var inlineeFileName = Utils.TryGetFileName(inlineeFilePath);

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
}
