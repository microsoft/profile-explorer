// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;
using IRExplorerCore;
using IRExplorerCore.IR;
using IRExplorerCore.IR.Tags;
using IRExplorerUI.Compilers;
using IRExplorerUI.Compilers.ASM;
using IRExplorerUI.Document;
using IRExplorerUI.Profile;
using IRExplorerUI.Utilities;
using Microsoft.Win32;
using Microsoft.Windows.EventTracing;
using static Microsoft.ApplicationInsights.MetricDimensionNames.TelemetryContext;
using Color = System.Windows.Media.Color;
using TimeSpan = System.TimeSpan;

namespace IRExplorerUI {
    /// <summary>
    ///     Interaction logic for SectionPanel.xaml
    /// </summary>
    public partial class SourceFilePanel : ToolPanelControl {
        private SourceFileMapper sourceFileMapper_ = new SourceFileMapper();
        private IRTextSection section_;
        private IRElement element_;
        private bool fileLoaded_;
        private bool ignoreNextCaretEvent_;
        private int selectedLine_;
        private string currentFilePath_;
        private string initialFilePath_;
        private ElementHighlighter profileMarker_;
        private OverlayRenderer overlayRenderer_;
        private bool hasProfileInfo_;
        private int hottestSourceLine_;
        private InlineeSourceLocation currentInlinee_;

        public SourceFilePanel() {
            InitializeComponent();
            TextView.TextArea.Caret.PositionChanged += Caret_PositionChanged;
            var lineBrush = Utils.BrushFromColor(Color.FromRgb(197, 222, 234));

            profileMarker_ = new ElementHighlighter(HighlighingType.Marked);
            TextView.TextArea.TextView.BackgroundRenderers.Add(profileMarker_);
            TextView.TextArea.TextView.BackgroundRenderers.Add(
                new CurrentLineHighlighter(TextView, lineBrush, ColorPens.GetPen(Colors.Gray)));

            // Create the overlay and place it on top of the text.
            overlayRenderer_ = new OverlayRenderer(profileMarker_);
            TextView.TextArea.TextView.BackgroundRenderers.Add(overlayRenderer_);
            TextView.TextArea.TextView.InsertLayer(overlayRenderer_, KnownLayer.Text, LayerInsertionPosition.Above);
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

            if (line != null && Document != null) {
                selectedLine_ = line.LineNumber;
                Document.SelectElementsOnSourceLine(line.LineNumber - 1, currentInlinee_);
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

        private string BrowseSourceFile() => BrowseSourceFile(
            filter: "C/C++ source files|*.c;*.cpp;*.cc;*.cxx;*.h;*.hpp;*.hxx;*.hh|All Files|*.*",
            title: string.Empty);

        private string BrowseSourceFile(string filter, string title) {
            var fileDialog = new OpenFileDialog {
                Filter = filter,
                Title = title
            };

            var result = fileDialog.ShowDialog();

            if (result.HasValue && result.Value) {
                return fileDialog.FileName;
            }

            return null;
        }

        private async Task<bool> LoadSourceFileImpl(string path, int sourceStartLine) {
            try {
                string text = await File.ReadAllTextAsync(path);
                TextView.Text = text;
                PathTextbox.Text = path;
                currentFilePath_ = path;
                fileLoaded_ = true;

                ScrollToLine(sourceStartLine);
                return true;
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed to load source file {path}: {ex.Message}");
                return false;
            }
        }

        private async void Button_Click(object sender, RoutedEventArgs e) {
            string path = BrowseSourceFile();

            if (path != null) {
                if(!await LoadSourceFile(path)) {
                    TextView.Text = $"Failed to load source file {path}!";
                }
            }
        }

        #region IToolPanel

        public override ToolPanelKind PanelKind => ToolPanelKind.Source;
        public override HandledEventKind HandledEvents => HandledEventKind.ElementSelection;

        public override async void OnDocumentSectionLoaded(IRTextSection section, IRDocument document) {
            base.OnDocumentSectionLoaded(section, document);
            section_ = section;

            if (!await LoadSourceFileForFunction(section_.ParentFunction)) {
                TextView.Text = $"No source file available";
            }
            else {
                ScrollToLine(hottestSourceLine_);
            }
        }

        private async Task<bool> LoadSourceFileForFunction(IRTextFunction function) {
            fileLoaded_ = false;

            // Check if there is profile info.
            var profile = Session.ProfileData?.GetFunctionProfile(function);

            if (profile != null) {
                if (!string.IsNullOrEmpty(profile.SourceFilePath)) {
                    initialFilePath_ = profile.SourceFilePath;

                    if (await LoadSourceFile(profile.SourceFilePath)) {
                        await AnnotateProfilerData(profile);
                        return true;
                    }
                }
            }

            if (!fileLoaded_) {
                // Without profile info, try to get the associated source file
                // from the debug info if specified and available.
                var loadedDoc = Session.SessionState.FindLoadedDocument(function);
                var debugFile = loadedDoc.DebugInfoFilePath;

                if (!string.IsNullOrEmpty(debugFile) &&
                    File.Exists(debugFile)) {
                    using var debugInfo = new PDBDebugInfoProvider();

                    if (debugInfo.LoadDebugInfo(debugFile)) {
                        var (sourceFilePath, sourceStartLine) = debugInfo.FindFunctionSourceFilePath(function);

                        if (!string.IsNullOrEmpty(sourceFilePath)) {
                            initialFilePath_ = sourceFilePath;
                            await LoadSourceFile(sourceFilePath, sourceStartLine);
                        }
                    }
                }
            }

            return fileLoaded_;
        }

        private async Task<bool> LoadSourceFile(string sourceFilePath, int sourceStartLine = -1) {
            if (fileLoaded_ && currentFilePath_ == sourceFilePath) {
                return true;
            }

            fileLoaded_ = false;
            string mappedSourceFilePath;

            if (File.Exists(sourceFilePath)) {
                mappedSourceFilePath = sourceFilePath;
            }
            else {
                mappedSourceFilePath = sourceFileMapper_.Map(sourceFilePath, () => 
                    BrowseSourceFile(filter: $"Source File|{Path.GetFileName(sourceFilePath)}",
                                     title: $"Open {sourceFilePath}"));
            }

            if (mappedSourceFilePath != null && await LoadSourceFileImpl(mappedSourceFilePath, sourceStartLine)) {
                return true;
            }
            else {
                TextView.Text = $"Failed to load profile source file {sourceFilePath}!";
                return false;
            }
        }

        public override void OnDocumentSectionUnloaded(IRTextSection section, IRDocument document) {
            base.OnDocumentSectionUnloaded(section, document);
            ResetState();
        }

        private void ResetState() {
            ResetSelectedLine();
            ResetProfileMarking();
            section_ = null;
            fileLoaded_ = false;
            currentInlinee_ = null;
        }

        private void ResetProfileMarking() {
            overlayRenderer_.Clear();
            profileMarker_.Clear();
            hasProfileInfo_ = false;
        }

        private void ResetSelectedLine() {
            selectedLine_ = -1;
            element_ = null;
        }

        public override async void OnElementSelected(IRElementEventArgs e) {
            if (!fileLoaded_ || e.Element == element_) {
                return;
            }

            element_ = e.Element;
            var instr = element_.ParentInstruction;
            var tag = instr?.GetTag<SourceLocationTag>();

            if (tag != null) {
                if (tag.HasInlinees) {
                    if(await LoadInlineeSourceFile(tag)) {
                        return;
                    }
                }
                else {
                    ResetInlinee();
                }

                await LoadSourceFileForFunction(section_.ParentFunction);
                ScrollToLine(tag.Line);
            }
        }

        private async Task<bool> LoadInlineeSourceFile(SourceLocationTag tag) {
            var last = tag.Inlinees[0];
            InlineeCombobox.ItemsSource = new ListCollectionView(tag.Inlinees);
            InlineeCombobox.SelectedItem = last;
            return await LoadInlineeSourceFile(last);
        }

        private void ResetInlinee() {
            InlineeCombobox.ItemsSource = null;
            currentInlinee_ = null;
        }

        //? TODO: Option to stop before STL functs (just my code like)

        //? TODO: Select source line must go through inlinee mapping to select proper asm 
        //     all instrs that have the line on the inlinee list for this func

        public async Task<bool> LoadInlineeSourceFile(InlineeSourceLocation inlinee) {
            if(inlinee == currentInlinee_) {
                return true;
            }

            // Try to load the profile info of the inlinee.
            var summary = section_.ParentFunction.ParentSummary;

            var inlineeFunc = summary.FindFunction((funcName) => {
                var demangledName = PDBDebugInfoProvider.DemangleFunctionName(funcName);
                return demangledName == inlinee.Function;
            });

            bool fileLoaded = false;

            if (inlineeFunc != null) {
                fileLoaded = await LoadSourceFileForFunction(inlineeFunc);
            }

            if (!fileLoaded && !string.IsNullOrEmpty(inlinee.FilePath)) {
                fileLoaded = await LoadSourceFile(inlinee.FilePath);
            }

            if (fileLoaded) {
                ScrollToLine(inlinee.Line);
            }

            currentInlinee_ = inlinee;
            return fileLoaded;
        }

        private void ScrollToLine(int line) {
            if (line <= 0 || line > TextView.Document.LineCount) {
                return;
            }

            var documentLine = TextView.Document.GetLineByNumber(line);

            if (documentLine.LineNumber != selectedLine_) {
                selectedLine_ = documentLine.LineNumber;
                ignoreNextCaretEvent_ = true;
                TextView.CaretOffset = documentLine.Offset;
                TextView.ScrollToLine(line);
            }
        }

        public override void OnSessionEnd() {
            base.OnSessionEnd();
            ResetState();
            TextView.Text = "";
        }

        #endregion

        private async Task AnnotateProfilerData(FunctionProfileData profile) {
            ResetProfileMarking();

            foreach (var pair in profile.ChildrenWeights) {
                var child = Session.MainDocumentSummary.GetFunctionWithId(pair.Key);
            }

            var markerOptions = ProfileDocumentMarkerOptions.Default;
            var nextElementId = new IRElementId();
            var lines = new List<Tuple<int, TimeSpan>>(profile.SourceLineWeight.Count);

            foreach (var pair in profile.SourceLineWeight) {
                lines.Add(new Tuple<int, TimeSpan>(pair.Key, pair.Value));
            }

            lines.Sort((a, b) => b.Item2.CompareTo(a.Item2));
            int lineIndex = 0;

            foreach (var pair in lines) {
                int sourceLine = pair.Item1;

                if (sourceLine <= 0 || sourceLine > TextView.Document.LineCount) {
                    continue;
                }

                double weightPercentage = profile.ScaleWeight(pair.Item2);
                var color = markerOptions.PickColorForWeight(weightPercentage);
                var style = new HighlightingStyle(color);
                IconDrawing icon = null;

                if (lineIndex == 0) {
                    icon = IconDrawing.FromIconResource("DotIconRed");
                    hottestSourceLine_ = sourceLine;
                }
                else if (lineIndex <= 3) {
                    icon = IconDrawing.FromIconResource("DotIconYellow");
                }
                
                var documentLine = TextView.Document.GetLineByNumber(sourceLine);
                var location = new TextLocation(documentLine.Offset, sourceLine, 0);
                var element = new IRElement(location, documentLine.Length);
                element.Id = nextElementId.NextOperand();

                var group = new HighlightedGroup(style);
                group.Add(element);
                profileMarker_.Add(group);

                var tooltip = $"{Math.Round(weightPercentage * 100, 2)}% ({Math.Round(pair.Item2.TotalMilliseconds, 2)} ms)";
                AddElementOverlay(element, icon, lineIndex, 16, 16, tooltip, markerOptions);
                lineIndex++;
            }

            UpdateHighlighting();
            hasProfileInfo_ = true;
        }

        public void AddElementOverlay(IRElement element, IconDrawing icon, int index,
                                    double width, double height, string label,
                                    ProfileDocumentMarkerOptions options,
                                    HorizontalAlignment alignmentX = HorizontalAlignment.Right,
                                    VerticalAlignment alignmentY = VerticalAlignment.Center,
                                    double marginX = 8, double marginY = 2) {
            var overlay = IconElementOverlay.CreateDefault(icon, width, height,
                                                            Brushes.Transparent, Brushes.Transparent, null,
                                                            label, null, alignmentX, alignmentY, marginX, marginY);
            overlay.IsLabelPinned = true;

            if (index <= 2) {
                overlay.TextColor = options.HotBlockOverlayTextColor;
                overlay.TextWeight = FontWeights.Bold;
            }
            else {
                overlay.TextColor = options.BlockOverlayTextColor;
            }

            overlayRenderer_.AddElementOverlay(element, overlay);
        }


        private void UpdateHighlighting() {
            TextView.TextArea.TextView.Redraw();
        }

        private void ButtonBase_OnClick(object sender, RoutedEventArgs e) {
            if(hasProfileInfo_) {
                ScrollToLine(hottestSourceLine_);
            }
        }

        private async void InlineeCombobox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (e.AddedItems.Count == 1) {
                var inlinee = (InlineeSourceLocation)e.AddedItems[0];
                await LoadInlineeSourceFile(inlinee);
            }
        }

        private void Button_Click_1(object sender, RoutedEventArgs e) {
            if(InlineeCombobox.ItemsSource != null &&
                InlineeCombobox.SelectedIndex > 0) {
                InlineeCombobox.SelectedIndex--;
            }
        }

        private void Button_Click_2(object sender, RoutedEventArgs e) {
            if (InlineeCombobox.ItemsSource != null &&
                InlineeCombobox.SelectedIndex < ((ListCollectionView)InlineeCombobox.ItemsSource).Count - 1) {
                InlineeCombobox.SelectedIndex++;
            }
        }
    }
}
