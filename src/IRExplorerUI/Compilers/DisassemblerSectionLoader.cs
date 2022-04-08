using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using IRExplorerCore;

namespace IRExplorerUI.Compilers {
    public class DisassemblerSectionLoader : IRTextSectionLoader {
        public override IRTextSummary LoadDocument(ProgressInfoHandler progressHandler) {
            return null;
        }

        public override string GetDocumentOutputText() {
            return null;
        }

        public override byte[] GetDocumentTextBytes() {
            return new byte[] { };
        }

        public override ParsedIRTextSection LoadSection(IRTextSection section) {
            return null;
        }

        public override string GetSectionText(IRTextSection section) {
            return null;
        }

        public override ReadOnlyMemory<char> GetSectionTextSpan(IRTextSection section) {
            return default;
        }

        public override string GetSectionOutputText(IRPassOutput output) {
            return null;
        }

        public override ReadOnlyMemory<char> GetSectionOutputTextSpan(IRPassOutput output) {
            return default;
        }

        public override List<string> GetSectionOutputTextLines(IRPassOutput output) {
            return null;
        }

        public override string GetRawSectionText(IRTextSection section) {
            return null;
        }

        public override string GetRawSectionPassOutput(IRPassOutput output) {
            return null;
        }

        public override ReadOnlyMemory<char> GetRawSectionTextSpan(IRTextSection section) {
            return default;
        }

        public override ReadOnlyMemory<char> GetRawSectionPassOutputSpan(IRPassOutput output) {
            return default;
        }

        protected override void Dispose(bool disposing) {
        }
    }
}
