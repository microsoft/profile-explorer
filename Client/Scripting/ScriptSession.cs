using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Media;
using Core;
using Core.Analysis;
using Core.IR;

namespace Client.Scripting {
    public class ScriptSession {
        public class AnalysisInfo {
            FunctionIR function_;
            DominatorAlgorithm domAlgorithm_;

            public AnalysisInfo(FunctionIR function) {
                function_ = function;
            }

            public DominatorAlgorithm DominatorTree {
                get {
                    if (domAlgorithm_ == null) {
                        domAlgorithm_ = new DominatorAlgorithm(function_, DominatorAlgorithmOptions.BuildDominatorTree |
                            DominatorAlgorithmOptions.BuildQueryCache);
                    }

                    return domAlgorithm_;
                }
            }
        }

        CancelableTaskInfo task_;
        IRDocument document_;
        StringBuilder builder_;
        List<Tuple<IRElement, Color>> markedElements_;
        AnalysisInfo analysis_;

        public bool IsCanceled => task_.IsCanceled;
        public void Cancel() { task_.Cancel(); }

        public string OutputText => builder_.ToString();
        public List<Tuple<IRElement, Color>> MarkedElements => markedElements_;
        public AnalysisInfo Analysis => analysis_;

        public ScriptSession(IRDocument document) {
            task_ = new CancelableTaskInfo();
            document_ = document;
            builder_ = new StringBuilder();
            markedElements_ = new List<Tuple<IRElement, Color>>();
            analysis_ = new AnalysisInfo(document.Function);
        }

        public void Mark(IRElement element, Color color) {
            markedElements_.Add(new Tuple<IRElement, Color>(element, color));
        }

        public void Mark(IRElement element) {
            markedElements_.Add(new Tuple<IRElement, Color>(element, Colors.Transparent));
        }

        public void Write(string format, params object[] args) {
            var text = string.Format(format, args);
            builder_.Append(text);
        }

        public void WriteLine(string format, params object[] args) {
            var text = string.Format(format, args);
            builder_.AppendLine(text);
        }

        public void Message(string format, params object[] args) {
            var text = string.Format(format, args);
            MessageBox.Show(text, "Compiler Studio - Script message", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        public bool QuestionMessage(string format, params object[] args) {
            var text = string.Format(format, args);
            return MessageBox.Show(text, "Compiler Studio - Script message", MessageBoxButton.YesNo, MessageBoxImage.Question) ==
                   MessageBoxResult.Yes;
        }

        public void ErrorMessage(string format, params object[] args) {
            var text = string.Format(format, args);
            MessageBox.Show(text, "Compiler Studio - Script message", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
