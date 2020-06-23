using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Media;
using CoreLib;
using CoreLib.Analysis;
using CoreLib.IR;

namespace Client.Scripting {
    public class ScriptSession {
        private AnalysisInfo analysis_;
        private StringBuilder builder_;
        private IRDocument document_;
        private List<Tuple<IRElement, Color>> markedElements_;

        private CancelableTaskInfo task_;

        public ScriptSession(IRDocument document) {
            task_ = new CancelableTaskInfo();
            document_ = document;
            builder_ = new StringBuilder();
            markedElements_ = new List<Tuple<IRElement, Color>>();
            analysis_ = new AnalysisInfo(document.Function);
        }

        public bool IsCanceled => task_.IsCanceled;

        public string OutputText => builder_.ToString();
        public List<Tuple<IRElement, Color>> MarkedElements => markedElements_;
        public AnalysisInfo Analysis => analysis_;

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

        public void Message(string format, params object[] args) {
            string text = string.Format(format, args);

            MessageBox.Show(text, "Compiler Studio - Script message", MessageBoxButton.OK,
                            MessageBoxImage.Information);
        }

        public bool QuestionMessage(string format, params object[] args) {
            string text = string.Format(format, args);

            return MessageBox.Show(text, "Compiler Studio - Script message", MessageBoxButton.YesNo,
                                   MessageBoxImage.Question) ==
                   MessageBoxResult.Yes;
        }

        public void ErrorMessage(string format, params object[] args) {
            string text = string.Format(format, args);

            MessageBox.Show(text, "Compiler Studio - Script message", MessageBoxButton.OK,
                            MessageBoxImage.Error);
        }

        public class AnalysisInfo {
            private DominatorAlgorithm domAlgorithm_;
            private FunctionIR function_;

            public AnalysisInfo(FunctionIR function) {
                function_ = function;
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
