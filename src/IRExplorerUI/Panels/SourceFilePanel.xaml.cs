// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using IRExplorerCore;
using IRExplorerCore.IR;
using Microsoft.Win32;

namespace IRExplorerUI {
    /// <summary>
    ///     Interaction logic for SectionPanel.xaml
    /// </summary>
    public partial class SourceFilePanel : ToolPanelControl {
        private IRElement element_;
        private bool fileLoaded_;
        private bool ignoreNextCaretEvent_;
        private int selectedLine_;

        public SourceFilePanel() {
            InitializeComponent();
            TextView.TextArea.Caret.PositionChanged += Caret_PositionChanged;
            var lineBrush = Utils.BrushFromColor(Color.FromRgb(197, 222, 234));

            TextView.TextArea.TextView.BackgroundRenderers.Add(
                new CurrentLineHighlighter(TextView, lineBrush, Pens.GetPen(Colors.Gray)));
        }

        private void Caret_PositionChanged(object sender, EventArgs e) {
            if (ignoreNextCaretEvent_) {
                ignoreNextCaretEvent_ = false;
                return;
            }

            HighlightElementsOnSelectedLine();
        }

        private void HighlightElementsOnSelectedLine() {
            var line = TextView.Document.GetLineByOffset(TextView.CaretOffset);

            if (line != null && Session.CurrentDocument != null) {
                selectedLine_ = line.LineNumber;
                Session.CurrentDocument.HighlightElementsOnLine(line.LineNumber);
            }
        }

        private void ComboBox_Loaded(object sender, RoutedEventArgs e) {
            if (sender is ComboBox control) {
                Utils.PatchComboBoxStyle(control);
            }
        }

        private void ToolBar_Loaded(object sender, RoutedEventArgs e) {
            Utils.PatchToolbarStyle(sender as ToolBar);
        }

        private string BrowseSourceFile() {
            var fileDialog = new OpenFileDialog {
                Filter = "C/C++ source files|*.c;*.cpp;*.cc;*.cxx;*.h;*.hpp;*.hxx;*.hh|All Files|*.*"
            };

            var result = fileDialog.ShowDialog();

            if (result.HasValue && result.Value) {
                return fileDialog.FileName;
            }

            return null;
        }

        private async void LoadSourceFile(string path) {
            try {
                string text = await File.ReadAllTextAsync(path);
                TextView.Text = text;
                PathTextbox.Text = path;
                fileLoaded_ = true;
            }
            catch (Exception) {
                TextView.Text = "Failed to load source file!";
            }
        }

        private void Button_Click(object sender, RoutedEventArgs e) {
            string path = BrowseSourceFile();

            if (path != null) {
                LoadSourceFile(path);
            }
        }

        #region IToolPanel

        public override ToolPanelKind PanelKind => ToolPanelKind.Source;
        public override HandledEventKind HandledEvents => HandledEventKind.ElementSelection;

        public override void OnDocumentSectionUnloaded(IRTextSection section, IRDocument document) {
            ResetSelectedLine();
        }

        private void ResetSelectedLine() {
            selectedLine_ = -1;
            element_ = null;
        }

        public override void OnElementSelected(IRElementEventArgs e) {
            if (!fileLoaded_ || e.Element == element_) {
                return;
            }

            element_ = e.Element;
            var instr = element_.ParentInstruction;
            var tag = instr?.GetTag<SourceLocationTag>();

            if (tag != null && tag.Line >= 0 && tag.Line <= TextView.Document.LineCount) {
                var documentLine = TextView.Document.GetLineByNumber(tag.Line);

                if (documentLine.LineNumber != selectedLine_) {
                    selectedLine_ = documentLine.LineNumber;
                    ignoreNextCaretEvent_ = true;
                    TextView.CaretOffset = documentLine.Offset;
                    TextView.ScrollToLine(tag.Line);
                }
            }
        }

        public override void OnSessionEnd() {
            base.OnSessionEnd();
            ResetSelectedLine();
            TextView.Text = "";
            fileLoaded_ = false;
        }

        #endregion
    }
}
