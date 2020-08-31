using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using IRExplorerCore;
using IRExplorerCore.Analysis;
using IRExplorerCore.IR;

namespace IRExplorerUI.Scripting {
    public class ScriptSession {
        private AnalysisInfo analysis_;
        private StringBuilder builder_;
        private IRDocument document_;
        private List<Tuple<IRElement, Color>> markedElements_;
        private CancelableTask task_;
        private ISession session_;

        public ScriptSession(IRDocument document, ISession session) {
            task_ = new CancelableTask();
            document_ = document;
            session_ = session;
            builder_ = new StringBuilder();
            markedElements_ = new List<Tuple<IRElement, Color>>();

            if (document != null) {
                analysis_ = new AnalysisInfo(document.Function);
            }
        }

        public bool IsCanceled => task_.IsCanceled;

        public bool SilentMode { get; set; }
        public string OutputText => builder_.ToString();
        public List<Tuple<IRElement, Color>> MarkedElements => markedElements_;
        public AnalysisInfo Analysis => analysis_;
        public FunctionIR CurrentFunction => document_?.Function;

        public string SessionName { get; set; }
        public string IRName => session_.CompilerInfo.CompilerIRName;
        public ICompilerIRInfo IR => session_.CompilerInfo.IR;
        public bool IsInTwoDocumentsDiffMode => session_.IsInTwoDocumentsDiffMode;
        public IRTextSummary MainDocument => session_.MainDocumentSummary;
        public IRTextSummary DiffDocument => session_.DiffDocumentSummary;

        public FunctionIR ParseSection(IRTextSection section) {
            var errorHandler = IR.CreateParsingErrorHandler();
            var parser = IR.CreateSectionParser(errorHandler);
            var text = session_.GetSectionTextAsync(section).Result;
            return parser.ParseSection(section, text);
        }

        public string GetSectionText(IRTextSection section) {
            return session_.GetSectionTextAsync(section).Result;
        }

        public void Cancel() {
            task_.Cancel();
        }

        public void Mark(IRElement element, Color color) {
            markedElements_.Add(new Tuple<IRElement, Color>(element, color));
        }

        public void Mark(IRElement element) {
            markedElements_.Add(new Tuple<IRElement, Color>(element, Colors.Transparent));
        }

        public void Write(string format, params object[] args) {
            string text = string.Format(format, args);
            builder_.Append(text);
        }

        public void WriteLine(string format, params object[] args) {
            string text = string.Format(format, args);
            builder_.AppendLine(text);
        }

        public void Write(IRElement element, IRTextSection section) {
            var text = GetSectionText(section);
            WriteLine(text.Substring(element.TextLocation.Offset, element.TextLength));
        }

        public void Message(string format, params object[] args) {
            string text = string.Format(format, args);

            if (SilentMode) {
                WriteLine($"[silent] {text}");
                return;
            }

            //? TODO: using var centerForm = new DialogCenteringHelper(this);
            MessageBox.Show(text, "IR Explorer - Script message", MessageBoxButton.OK,
                            MessageBoxImage.Information);
        }

        public bool QuestionMessage(string format, params object[] args) {
            string text = string.Format(format, args);

            if (SilentMode) {
                WriteLine($"[silent question] {text}");
                return false;
            }

            //using var centerForm = new DialogCenteringHelper(this);
            return MessageBox.Show(text, "IR Explorer - Script message", MessageBoxButton.YesNo,
                                   MessageBoxImage.Question) ==
                   MessageBoxResult.Yes;
        }

        public void ErrorMessage(string format, params object[] args) {
            string text = string.Format(format, args);

            if (SilentMode) {
                WriteLine($"[silent error] {text}");
                return;
            }

            //using var centerForm = new DialogCenteringHelper(this);

            MessageBox.Show(text, "IR Explorer - Script message", MessageBoxButton.OK,
                            MessageBoxImage.Error);
        }

        public bool SaveOutput(string filePath) {
            try {
                File.WriteAllText(filePath, builder_.ToString());
                return true;
            }
            catch (Exception ex) {
                WriteLine($"Failed to save output to file {filePath}: {ex.Message}");
                return false;
            }
        }

        public class AnalysisInfo {
            private DominatorAlgorithm domAlgorithm_;
            private FunctionIR function_;
            private ReferenceFinder referenceFinder_;

            public AnalysisInfo(FunctionIR function) {
                function_ = function;
            }

            public ReferenceFinder References {
                get {
                    if (referenceFinder_ == null) {
                        referenceFinder_ = new ReferenceFinder(function_);
                    }

                    return referenceFinder_;
                }
            }

            public DominatorAlgorithm DominatorTree {
                get {
                    if (domAlgorithm_ == null) {
                        domAlgorithm_ = new DominatorAlgorithm(
                            function_,
                            DominatorAlgorithmOptions.BuildDominatorTree |
                            DominatorAlgorithmOptions.BuildQueryCache);
                    }

                    return domAlgorithm_;
                }
            }
        }
    }
}
