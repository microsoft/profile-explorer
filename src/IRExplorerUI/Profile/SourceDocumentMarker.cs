// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
using System.Collections.Generic;
using IRExplorerCore;
using IRExplorerCore.IR;
using System.Text;
using IRExplorerUI.Document;

//? TODO: EXTRACT SOURCE LOCATION MARKING TO OWN CLASS NOT PROFILE-DEPENDENT

namespace IRExplorerUI.Compilers.ASM {
    public class SourceDocumentMarker {
        private ProfileDocumentMarkerOptions options_;
        private ICompilerIRInfo ir_;

        public SourceDocumentMarker(ProfileDocumentMarkerOptions options, ICompilerIRInfo ir) {
            options_ = options;
            ir_ = ir;
        }

        public void Mark(IRDocument document, FunctionIR function) {
            MarkProfiledElements(document, function);
        }

        private void MarkProfiledElements(IRDocument document, FunctionIR function) {
            double virtualColumnAdjustment = ir_.Mode == IRMode.x86_64 ? 100 : 0;

            foreach (var element in function.AllInstructions) {
                var tag = element.GetTag<SourceLocationTag>();

                if (tag == null) {
                    continue;
                }

                var funcName = PDBDebugInfoProvider.DemangleFunctionName(function.Name, FunctionNameDemanglingOptions.OnlyName);

                if (tag.Line != 0) {
                    var label = $"{tag.Line}";
                    var tooltip = $"Line number for {funcName}";
                    var overlay = document.RegisterIcomElementOverlay(element, null, 16, 0, label, tooltip);
                    overlay.IsLabelPinned = true;
                    overlay.TextColor = options_.ElementOverlayTextColor;
                    overlay.Background = options_.ElementOverlayBackColor;
                    overlay.VirtualColumn = options_.VirtualColumnPosition + virtualColumnAdjustment;
                }

                if(!tag.HasInlinees) {
                    continue;
                }

                var sb = new StringBuilder();
                var tooltipSb = new StringBuilder();

                for (int k = 0; k < tag.Inlinees.Count; k++) {
                    var inlinee = tag.Inlinees[k];
                    var inlineeName = PDBDebugInfoProvider.DemangleFunctionName(inlinee.Function, FunctionNameDemanglingOptions.OnlyName);
                    sb.AppendFormat("{0}:{1}", inlineeName, tag.Inlinees[k].Line);

                    AppendInlineeTooltip(inlineeName, inlinee.Line, inlinee.FilePath, k, tooltipSb);
                    tooltipSb.AppendLine();

                    if (k != tag.Inlinees.Count - 1) {
                        sb.Append("  |  ");
                    }
                }

                AppendInlineeTooltip(funcName, tag.Line, null, tag.Inlinees.Count, tooltipSb);
                var inlineeOverlay = document.RegisterIcomElementOverlay(element, null, 16, 0, sb.ToString(), tooltipSb.ToString());
                inlineeOverlay.VirtualColumn = options_.VirtualColumnPosition + 50 + virtualColumnAdjustment;
                inlineeOverlay.TextColor = options_.InlineeOverlayTextColor;
                inlineeOverlay.Background = options_.ElementOverlayBackColor;
                inlineeOverlay.IsLabelPinned = true;
            }
        }

        private void AppendInlineeTooltip(string inlineeName, int inlineeLine, string inlineeFilePath,
                                          int index, StringBuilder tooltipSb) {
            // func1:line (file)
            //     func2
            //         ...
            for (int column = 0; column < index * 4; column++) {
                tooltipSb.Append(' ');
            }

            var inlineeFileName = Utils.TryGetFileName(inlineeFilePath);

            if (!string.IsNullOrEmpty(inlineeFileName)) {
                tooltipSb.AppendFormat("{0}:{1} ({2})", inlineeName, inlineeLine, inlineeFileName);
            }
            else {
                tooltipSb.AppendFormat("{0}:{1}", inlineeName, inlineeLine);
            }
        }
    }
}
