using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using IRExplorerCore;
using IRExplorerCore.IR;

namespace IRExplorerUI.Compilers.LLVM {
    class LLVMRemarkProvider : IRRemarkProvider
    {
        public string SettingsFilePath { get; }
        public bool SaveSettings() {
            return false;
        }

        public bool LoadSettings() {
            return false;
        }

        public List<RemarkCategory> RemarkCategories { get; }
        public List<RemarkSectionBoundary> RemarkSectionBoundaries { get; }
        public List<RemarkTextHighlighting> RemarkTextHighlighting { get; }
        public List<IRTextSection> GetSectionList(IRTextSection currentSection, int maxDepth, bool stopAtSectionBoundaries) {
            return new List<IRTextSection>();
        }

        public List<Remark> ExtractAllRemarks(List<IRTextSection> sections, FunctionIR function, LoadedDocument document, RemarkProviderOptions options,
            CancelableTask cancelableTask) {
            return new List<Remark>();
        }

        public List<Remark> ExtractRemarks(string text, FunctionIR function, IRTextSection section, RemarkProviderOptions options,
            CancelableTask cancelableTask) {
            return new List<Remark>();
        }

        public List<Remark> ExtractRemarks(List<string> textLines, FunctionIR function, IRTextSection section, RemarkProviderOptions options,
            CancelableTask cancelableTask) {
            return new List<Remark>();
        }

        public OptimizationRemark GetOptimizationRemarkInfo(Remark remark) {
            return null;
        }
    }
}
