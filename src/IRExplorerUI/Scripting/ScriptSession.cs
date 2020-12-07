using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using IRExplorerCore;
using IRExplorerCore.Analysis;
using IRExplorerCore.IR;
using IRExplorerUI.Document;

namespace IRExplorerUI.Scripting {
    public class IconOverlayInfo {
        public IconDrawing Icon { get; set; }
        public string Tooltip { get; set; }
        public HorizontalAlignment AlignmentX { get; set; }
        public double MarginX { get; set; }
    }

    public class ScriptSession {
        private AnalysisInfo analysis_;
        private StringBuilder builder_;
        private IRDocument document_;
        private List<Tuple<IRElement, Color>> markedElements_;
        private List<Tuple<IRElement, IconOverlayInfo>> iconElementOverlays_;
        private CancelableTask task_;
        private ISession session_;
        private Dictionary<string, object> variables_;
        private string sectionText_;

        public ScriptSession(IRDocument document, ISession session) {
            task_ = new CancelableTask();
            document_ = document;
            session_ = session;
            builder_ = new StringBuilder();
            markedElements_ = new List<Tuple<IRElement, Color>>();
            iconElementOverlays_ = new List<Tuple<IRElement, IconOverlayInfo>>();
            variables_ = new Dictionary<string, object>();

            if (document != null) {
                analysis_ = new AnalysisInfo(document.Function, session.CompilerInfo.IR);
            }
        }

        public string SessionName { get; set; }
        public object SessionObject { get; set; }

        public bool SessionResult { get; set; }
        public string SessionResultMessage { get; set; }

        public bool IsCanceled => task_.IsCanceled;

        public bool SilentMode { get; set; }
        public string OutputText => builder_.ToString();
        public List<Tuple<IRElement, Color>> MarkedElements => markedElements_;
        public List<Tuple<IRElement, IconOverlayInfo>> IconElementOverlays => iconElementOverlays_;
        public AnalysisInfo Analysis => analysis_;
        public FunctionIR CurrentFunction => document_?.Function;
        public IRTextSection CurrentSection => document_?.Section;

        public string CurrentSectionText {
            get {
                // Cache the section text.
                if (sectionText_ == null && CurrentSection != null) {
                    sectionText_ = GetSectionText(CurrentSection);
                }

                return sectionText_;
            }
        }

        public string IRName => session_.CompilerInfo.CompilerIRName;
        public ICompilerIRInfo IR => session_.CompilerInfo.IR;
        public bool IsInTwoDocumentsDiffMode => session_.IsInTwoDocumentsDiffMode;
        public IRTextSummary MainDocument => session_.MainDocumentSummary;
        public IRTextSummary DiffDocument => session_.DiffDocumentSummary;

        public bool AddVariable<T>(string name, T value) where T : class {
            return variables_.TryAdd(name, value);
        }

        public T GetVariable<T>(string name, T defaultValue = null) where T : class {
            if (variables_.TryGetValue(name, out var value)) {
                if (value is T valueOfT) {
                    return valueOfT;
                }
            }

            if (defaultValue != null) {
                return defaultValue;
            }

            throw new InvalidOperationException($"Variable {name} not found or has unexpected type!");
        }

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

        public void SetSessionResult(bool result, string message = "") {
            SessionResult = result;
            SessionResultMessage = message;
        }

        public void Mark(IRElement element, Color color) {
            markedElements_.Add(new Tuple<IRElement, Color>(element, color));
        }

        public void Mark(IRElement element) {
            markedElements_.Add(new Tuple<IRElement, Color>(element, Colors.Gold));
        }

        public void AddIcon(IRElement element, string iconName, string text = "") {
            var icon = IconDrawing.FromIconResource(iconName);
            
            if(icon == null) {
                WriteLine($"Failed to add element icon with name {iconName}");
                return;
            }

            bool isDestination = element is OperandIR ir && ir.IsDestinationOperand;

            iconElementOverlays_.Add(new Tuple<IRElement, IconOverlayInfo>(
                element,
                new IconOverlayInfo() {
                    Icon = icon,
                    Tooltip = text,
                    AlignmentX = isDestination ? HorizontalAlignment.Left : HorizontalAlignment.Right
                }));
        }

        public void AddWarningIcon(IRElement element, string text = "") {
            AddIcon(element, "WarningIconColor", text);
        }

        public void Write(string format, params object[] args) {
            string text = string.Format(format, args);
            builder_.Append(text);
        }

        public void WriteLine(string format, params object[] args) {
            string text = string.Format(format, args);
            builder_.AppendLine(text);
        }

        public void WriteLine() {
            builder_.AppendLine();
        }

        /// <summary>
        /// Writes the text (IR) representing the element.
        /// </summary>
        /// <param name="element">The IR element to print</param>
        /// <param name="section">The IR text section to use as a text source. If null it uses CurrentSectionText</param>
        public void Write(IRElement element, IRTextSection section = null) {
            string text;

            if (section == null) {
                text = CurrentSectionText;
            }
            else {
                text = GetSectionText(section);
            }

            Write(text.Substring(element.TextLocation.Offset, element.TextLength));
        }

        public void WriteLine(IRElement element, IRTextSection section = null) {
            Write(element, section);
            WriteLine();
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
            private DominatorAlgorithm postdomAlgorithm_;
            private FunctionIR function_;
            private ReferenceFinder referenceFinder_;
            private ICompilerIRInfo ir_;

            public AnalysisInfo(FunctionIR function, ICompilerIRInfo ir) {
                function_ = function;
                ir_ = ir;
            }

            public ReferenceFinder References {
                get {
                    if (referenceFinder_ == null) {
                        referenceFinder_ = new ReferenceFinder(function_, ir_);
                    }

                    return referenceFinder_;
                }
            }

            public DominatorAlgorithm DominatorTree {
                get {
                    if (domAlgorithm_ == null) {
                        domAlgorithm_ = new DominatorAlgorithm(
                            function_,
                            DominatorAlgorithmOptions.BuildTree |
                            DominatorAlgorithmOptions.BuildQueryCache);
                    }

                    return domAlgorithm_;
                }
            }

            public DominatorAlgorithm PostDominatorTree {
                get {
                    if (postdomAlgorithm_ == null) {
                        postdomAlgorithm_ = new DominatorAlgorithm(
                            function_,
                            DominatorAlgorithmOptions.PostDominators |
                            DominatorAlgorithmOptions.BuildTree |
                            DominatorAlgorithmOptions.BuildQueryCache);
                    }

                    return postdomAlgorithm_;
                }
            }
        }
    }
}
