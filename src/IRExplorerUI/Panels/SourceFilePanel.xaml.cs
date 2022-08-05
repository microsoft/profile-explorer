// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using IRExplorerCore;
using IRExplorerCore.IR;
using IRExplorerUI.Compilers;
using IRExplorerUI.Compilers.ASM;
using IRExplorerUI.Document;
using IRExplorerUI.Profile;
using Microsoft.Win32;
using TextLocation = IRExplorerCore.TextLocation;

namespace IRExplorerUI {
    public partial class SourceFilePanel : ToolPanelControl, MarkedDocument, INotifyPropertyChanged {
        private SourceFileMapper sourceFileMapper_ = new SourceFileMapper();
        private IRTextSection section_;
        private IRElement element_;
        private bool ignoreNextCaretEvent_;
        private bool disableCaretEvent_;
        private int selectedLine_;
        private ElementHighlighter profileMarker_;
        private OverlayRenderer overlayRenderer_;
        private bool hasProfileInfo_;
        private bool sourceFileLoaded_;
        private IRTextFunction sourceFileFunc_;
        private string sourceFilePath_;
        private int hottestSourceLine_;
        private IRExplorerCore.IR.StackFrame currentInlinee_;
        private bool sourceMapperDisabled_;
        private bool columnsVisible_;
        private double previousVerticalOffset_;
        private List<Tuple<IRElement, TimeSpan>> profileElements_;
        private int profileElementIndex_;

        public SourceFilePanel() {
            InitializeComponent();
            DataContext = this;
            UpdateDocumentStyle();

            profileMarker_ = new ElementHighlighter(HighlighingType.Marked);
            TextView.TextArea.TextView.BackgroundRenderers.Add(profileMarker_);
            TextView.TextArea.TextView.BackgroundRenderers.Add(
                new CurrentLineHighlighter(TextView, App.Settings.DocumentSettings.SelectedValueColor));

            // Create the overlay and place it on top of the text.
            overlayRenderer_ = new OverlayRenderer(profileMarker_);
            TextView.TextArea.Caret.PositionChanged += Caret_PositionChanged;
            TextView.TextArea.TextView.ScrollOffsetChanged += TextViewOnScrollOffsetChanged;
            ProfileColumns.ScrollChanged += ProfileColumns_ScrollChanged;

            TextView.TextArea.TextView.BackgroundRenderers.Add(overlayRenderer_);
            TextView.TextArea.TextView.InsertLayer(overlayRenderer_, KnownLayer.Text, LayerInsertionPosition.Above);
        }

        private void ProfileColumns_ScrollChanged(object sender, ScrollChangedEventArgs e) {
            if (Math.Abs(e.VerticalChange) < double.Epsilon) {
                return;
            }

            TextView.ScrollToVerticalOffset(e.VerticalOffset);
        }

        private void UpdateDocumentStyle() {
            var settings = App.Settings.DocumentSettings;
            TextView.Background = ColorBrushes.GetBrush(settings.BackgroundColor);
            Foreground = ColorBrushes.GetBrush(settings.TextColor);
            FontFamily = new FontFamily(settings.FontName);
            FontSize = settings.FontSize;
        }


        public bool ColumnsVisible {
            get => columnsVisible_;
            set {
                if (columnsVisible_ != value) {
                    columnsVisible_ = value;
                    OnPropertyChanged(nameof(ColumnsVisible));
                }
            }
        }

        private double columnsListItemHeight_;

        public double ColumnsListItemHeight {
            get => columnsListItemHeight_;
            set {
                if (columnsListItemHeight_ != value) {
                    columnsListItemHeight_ = value;
                    OnPropertyChanged(nameof(ColumnsListItemHeight));
                }
            }
        }

        private Brush selectedLineBrush_;
        public Brush SelectedLineBrush {
            get => selectedLineBrush_;
            set {
                selectedLineBrush_ = value;
                OnPropertyChanged(nameof(SelectedLineBrush));
            }
        }

        public bool HasProfileInfo {
            get => hasProfileInfo_;
            set {
                if (hasProfileInfo_ != value) {
                    hasProfileInfo_ = value;
                    OnPropertyChanged();
                }
            }
        }

        private void Caret_PositionChanged(object sender, EventArgs e) {
            if (columnsVisible_) {
                var line = TextView.Document.GetLineByOffset(TextView.TextArea.Caret.Offset);
                ProfileColumns.SelectRow(line.LineNumber - 1);
            }

            if (ignoreNextCaretEvent_) {
                ignoreNextCaretEvent_ = false;
                return;
            }
            else if(disableCaretEvent_) {
                return;
            }

            HighlightElementsOnSelectedLine();
        }

        private void HighlightElementsOnSelectedLine() {
            var line = TextView.Document.GetLineByOffset(TextView.CaretOffset);

            if (line != null && Document != null) {
                selectedLine_ = line.LineNumber;
                Document.SelectElementsOnSourceLine(line.LineNumber, currentInlinee_);
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
            filter: "C/C++ source files|*.c;*.cpp;*.cc;*.cxx;*.h;*.hpp;*.hxx;*.hh|.NET source files|*.cs;*.vb|All Files|*.*",
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

        private async Task<bool> LoadSourceFileImpl(string path, string originalPath, int sourceStartLine) {
            try {
                string text = await File.ReadAllTextAsync(path);
                SetSourceText(text);
                SetPanelName(originalPath);
                ScrollToLine(sourceStartLine);
                return true;
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed to load source file {path}: {ex.Message}");
                return false;
            }
        }

        private void SetSourceText(string text) {
            disableCaretEvent_ = true; // Changing the text triggers the caret event twice.
            TextView.Text = text;
            disableCaretEvent_ = false;
        }

        private void SetPanelName(string path) {
            if (!string.IsNullOrEmpty(path)) {
                TitleSuffix = $" - {Utils.TryGetFileName(path)}";
                TitleToolTip = path;
            }
            else {
                TitleSuffix = "";
                TitleToolTip = null;
            }

            Session.UpdatePanelTitles();
        }

        private async void OpenButton_Click(object sender, RoutedEventArgs e) {
            string path = BrowseSourceFile();

            if (path != null) {
                var sourceInfo = new DebugFunctionSourceFileInfo(path, path);
                await LoadSourceFile(sourceInfo, section_.ParentFunction);
            }
        }

        #region IToolPanel

        public override ToolPanelKind PanelKind => ToolPanelKind.Source;
        public override HandledEventKind HandledEvents => HandledEventKind.ElementSelection;

        public override async void OnDocumentSectionLoaded(IRTextSection section, IRDocument document) {
            base.OnDocumentSectionLoaded(section, document);
            section_ = section;

            if (await LoadSourceFileForFunction(section_.ParentFunction)) {
                ScrollToLine(hottestSourceLine_);
            }
        }

        private IDebugInfoProvider GetDebugInfo(LoadedDocument loadedDoc) {
            //? Provider ASM should return instance instead of JSONDebug
            if (loadedDoc.DebugInfo != null) {
                // Used for managed binaries, where the debug info is constructed during profiling.
                return loadedDoc.DebugInfo;
            }
            else if (loadedDoc.DebugInfoFileExists) {
                var debugInfo = Session.CompilerInfo.CreateDebugInfoProvider(loadedDoc.BinaryFilePath);
                
                if (debugInfo.LoadDebugInfo(loadedDoc.DebugInfoFilePath)) {
                    return debugInfo;
                }
            }

            return null;
        }

        private async Task<bool> LoadSourceFileForFunction(IRTextFunction function) {
            if (sourceFileLoaded_ && sourceFileFunc_ == function) {
                return true; // Right file already loaded.
            }

            // Get the associated source file from the debug info if available,
            // since it also includes the start line number.
            var loadedDoc = Session.SessionState.FindLoadedDocument(function);
            FunctionProfileData funcProfile = null;
            string failureText = "";
            bool funcLoaded = false;

            //? TODO: Make async too
            var debugInfo = GetDebugInfo(loadedDoc);

            if(debugInfo != null) {
                var sourceInfo = DebugFunctionSourceFileInfo.Unknown;
                funcProfile = Session.ProfileData?.GetFunctionProfile(function);

                if (funcProfile != null) {
                    sourceInfo = LocateSourceFile(funcProfile, debugInfo);
                }

                if (sourceInfo.IsUnknown) {
                    // Try again using the function name.
                    sourceInfo = debugInfo.FindFunctionSourceFilePath(function);
                }
                else {
                    failureText = $"Could not find debug info for function:\n{function.Name}";
                }

                if (sourceInfo.HasFilePath) {
                    funcLoaded = await LoadSourceFile(sourceInfo, function);
                }
                else {
                    failureText = $"Missing file path in debug info for function:\n{function.Name}";
                }
            }
            else {
                failureText = $"Could not find debug info for module:\n{loadedDoc.ModuleName}";
            }

            if (funcProfile == null) {
                // Check if there is profile info.
                // This path is taken only if there is no debug info.
                funcProfile = Session.ProfileData?.GetFunctionProfile(function);
            }

            if (funcProfile != null) {
                if (!funcLoaded && !string.IsNullOrEmpty(funcProfile.SourceFilePath)) {
                    var sourceInfo = new DebugFunctionSourceFileInfo(funcProfile.SourceFilePath, funcProfile.SourceFilePath, 0);
                    funcLoaded = await LoadSourceFile(sourceInfo, function);
                }

                if (debugInfo != null) {
                    await AnnotateProfilerData(funcProfile, debugInfo);
                }
            }

            if (!funcLoaded) {
                HandleMissingSourceFile(failureText);
            }

            return funcLoaded;
        }

        private DebugFunctionSourceFileInfo LocateSourceFile(FunctionProfileData funcProfile,
                                                              IDebugInfoProvider debugInfo) {
            var sourceInfo = DebugFunctionSourceFileInfo.Unknown;

            // Lookup function by RVA, more precise.
            if (funcProfile.DebugInfo != null) {
                sourceInfo = debugInfo.FindSourceFilePathByRVA(funcProfile.DebugInfo.RVA);
            }

            return sourceInfo;
        }

        private async Task<bool> LoadSourceFile(DebugFunctionSourceFileInfo sourceInfo, IRTextFunction function) {
            // Check if the file can be found. If it's from another machine,
            // a mapping is done after the user is asked to pick the new location of the file.
            string mappedSourceFilePath = null;

            if (File.Exists(sourceInfo.FilePath)) {
                mappedSourceFilePath = sourceInfo.FilePath;
            }
            else if(!sourceMapperDisabled_) {
                mappedSourceFilePath = sourceFileMapper_.Map(sourceInfo.FilePath, () => 
                    BrowseSourceFile(filter: $"Source File|{Path.GetFileName(sourceInfo.OriginalFilePath)}",
                                     title: $"Open {sourceInfo.OriginalFilePath}"));

                if (string.IsNullOrEmpty(mappedSourceFilePath)) {
                    using var centerForm = new DialogCenteringHelper(this);

                    if (MessageBox.Show("Continue asking for source file location during this session?", "IR Explorer",
                                        MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == 
                                        MessageBoxResult.No) {
                        sourceMapperDisabled_ = true;
                    }
                }
            }
            
            if (mappedSourceFilePath != null &&
                await LoadSourceFileImpl(mappedSourceFilePath, sourceInfo.OriginalFilePath, sourceInfo.StartLine)) {
                sourceFileLoaded_ = true;
                sourceFileFunc_ = function;
                sourceFilePath_ = mappedSourceFilePath;
                return true;
            }
            else {
                HandleMissingSourceFile($"Could not find local copy of source file:\n{sourceInfo.FilePath}");
                return false;
            }
        }

        private void HandleMissingSourceFile(string failureText) {
            var text = $"Failed to load profile source file.";

            if (!string.IsNullOrEmpty(failureText)) {
                text += $"\n{failureText}";
            }

            TextView.Text = text;
            SetPanelName("");
            sourceFileLoaded_ = false;
            sourceFileFunc_ = null;
        }

        public override void OnDocumentSectionUnloaded(IRTextSection section, IRDocument document) {
            base.OnDocumentSectionUnloaded(section, document);
            ResetState();
        }

        private void ResetState() {
            ResetSelectedLine();
            ResetProfileMarking();
            section_ = null;
            sourceFileLoaded_ = false;
            sourceFileFunc_ = null;
            currentInlinee_ = null;
        }

        private void ResetProfileMarking() {
            overlayRenderer_.Clear();
            profileMarker_.Clear();
        }

        private void ResetSelectedLine() {
            selectedLine_ = -1;
            element_ = null;
        }

        public override async void OnElementSelected(IRElementEventArgs e) {
            if (!sourceFileLoaded_ || e.Element == element_) {
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

                if (await LoadSourceFileForFunction(section_.ParentFunction)) {
                    ScrollToLine(tag.Line);
                }
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

        public async Task<bool> LoadInlineeSourceFile(IRExplorerCore.IR.StackFrame inlinee) {
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
            else {
                //? TODO: Warning that the func can't be found (likely not present in binary at all, inlined everywhere)
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

        private async Task AnnotateProfilerData(FunctionProfileData profile, IDebugInfoProvider debugInfo) {
            ResetProfileMarking();

            var markerOptions = ProfileDocumentMarkerOptions.Default;
            var nextElementId = new IRElementId();

            var result = profile.ProcessSourceLines(debugInfo);
            var sourceLineWeights = result.SourceLineWeightList;

            if (sourceLineWeights.Count == 0) {
                return;
            }

            int totalLines = TextView.Document.LineCount;
            var dummyElements = new List<IRElement>(totalLines);
            var ids = IRElementId.NewFunctionId();
            var dummyFunc = new FunctionIR();
            var dummyBlock = new BlockIR(ids.NewBlock(0), 0, dummyFunc);
            dummyFunc.Blocks.Add(dummyBlock);
            dummyFunc.AssignBlockIndices();

            var processingResult = new FunctionProfileData.ProcessingResult();

            TupleIR MakeDummyTuple(TextLocation textLocation, DocumentLine documentLine1)
            {
                var tupleIr = new TupleIR(ids.NextTuple(), TupleKind.Other, dummyBlock);
                tupleIr.TextLocation = textLocation;
                tupleIr.TextLength = documentLine1.Length;
                dummyBlock.Tuples.Add(tupleIr);
                return tupleIr;
            }

            for (int lineNumber = 1; lineNumber <= totalLines; lineNumber++) {
                var documentLine = TextView.Document.GetLineByNumber(lineNumber);
                var location = new IRExplorerCore.TextLocation(documentLine.Offset, lineNumber - 1, 0);

                //? TODO: Tuples not needed for all lines, AddDummyRow below
                TupleIR dummyTuple = null;

                if (result.SourceLineWeight.TryGetValue(lineNumber, out var lineWeight)) {
                    //Trace.WriteLine($"Time on line {lineNumber}");
                    dummyTuple = MakeDummyTuple(location, documentLine);
                    processingResult.SampledElements.Add(new Tuple<IRElement, TimeSpan>(dummyTuple, lineWeight));
                }

                if (result.SourceLineCounters.TryGetValue(lineNumber, out var counters)) {
                    dummyTuple ??= MakeDummyTuple(location, documentLine);
                    processingResult.CounterElements.Add(new Tuple<IRElement, PerformanceCounterSet>(dummyTuple, counters));
                }
            }

            processingResult.SortSampledElements();
            processingResult.FunctionCounters = result.FunctionCounters;
            var profileOptions = ProfileDocumentMarkerOptions.Default;
            var profileMarker = new ProfileDocumentMarker(profile, Session.ProfileData, profileOptions, Session.CompilerInfo.IR);
            var columnData = await profileMarker.MarkSourceLines(this, dummyFunc, processingResult);

            ColumnsVisible = columnData.HasData;

            if (ColumnsVisible) {
                ProfileColumns.Display(columnData, TextView.LineCount, dummyFunc);
                profileElements_ = processingResult.SampledElements;
                UpdateHighlighting();
            }
            else {
                ProfileColumns.Reset();
            }

            HasProfileInfo = true;
        }

        private void TextViewOnScrollOffsetChanged(object? sender, EventArgs e) {
            double offset = TextView.TextArea.TextView.VerticalOffset;
            double changeAmount = offset - previousVerticalOffset_;
            previousVerticalOffset_ = offset;

            // Sync scrolling with the optional columns.
            SyncColumnsVerticalScrollOffset(offset);
        }


        private void SyncColumnsVerticalScrollOffset(double offset) {
            // Sync scrolling with the optional columns.
            if (columnsVisible_) {
                ProfileColumns.ScrollToVerticalOffset(offset);
            }
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
                var inlinee = (IRExplorerCore.IR.StackFrame)e.AddedItems[0];
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

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private async void DefaultButton_Click(object sender, RoutedEventArgs e) {
            if (section_ == null) {
                return; //? TODO: Button should be disabled, needs commands
            }

            // Re-enable source mapper if it was disabled before.
            sourceMapperDisabled_ = false;

            if (await LoadSourceFileForFunction(section_.ParentFunction)) {
                ScrollToLine(hottestSourceLine_);
            }
        }

        public double DefaultLineHeight => TextView.TextArea.TextView.DefaultLineHeight;

        public void SuspendUpdate() {
            
        }

        public void ResumeUpdate() {
            
        }

        public void ClearInstructionMarkers() {
            ResetProfileMarking();
        }

        public void MarkElements(ICollection<(IRElement, Color)> elementColorPairs) {
            foreach (var pair in elementColorPairs) {
                var style = new HighlightingStyle(pair.Item2);
                var group = new HighlightedGroup(style);
                group.Add(pair.Item1);
                profileMarker_.Add(group);
            }
            
            UpdateHighlighting();
        }

        public void MarkBlock(IRElement element, Color selectedColor, bool raiseEvent = true) {
            throw new NotImplementedException();
        }

        public IconElementOverlay RegisterIconElementOverlay(IRElement element, IconDrawing icon,
            double width, double height,
            string label, string tooltip) {
            throw new NotImplementedException();
        }

        private void JumpToProfiledElementExecuted(object sender, ExecutedRoutedEventArgs e) {
            if (!HasProfileElements()) {
                return;
            }

            profileElementIndex_ = 0;
            JumpToProfiledElement(profileElements_[profileElementIndex_].Item1);
        }

        private bool HasProfileElements() {
            return ColumnsVisible && profileElements_ != null && profileElements_.Count > 0;
        }

        private bool HasProfileElement(int offset) {
            return ColumnsVisible && profileElements_ != null &&
                   profileElementIndex_ + offset >= 0 &&
                   profileElementIndex_ + offset < profileElements_.Count;
        }

        private void JumpToNextProfiledElementExecuted(object sender, ExecutedRoutedEventArgs e) {
            JumpToProfiledElement(-1);
        }

        private void JumpToPreviousProfiledElementExecuted(object sender, ExecutedRoutedEventArgs e) {
            JumpToProfiledElement(1);
        }

        private void JumpToProfiledElement(int offset) {
            if (!HasProfileElement(offset)) {
                return;
            }

            profileElementIndex_ += offset;
            JumpToProfiledElement(profileElements_[profileElementIndex_].Item1);
        }

        private void JumpToProfiledElement(IRElement element) {
            TextView.ScrollToLine(element.TextLocation.Line);
            double offset = TextView.TextArea.TextView.VerticalOffset;
            SyncColumnsVerticalScrollOffset(offset);
        }

        private void JumpToNextProfiledElementCanExecute(object sender, CanExecuteRoutedEventArgs e) {
            e.CanExecute = HasProfileElement(-1);
        }

        private void JumpToPreviousProfiledElementCanExecute(object sender, CanExecuteRoutedEventArgs e) {
            e.CanExecute = HasProfileElement(1);
        }

        private void SourceFile_CopyPath(object sender, RoutedEventArgs e) {
            if (!string.IsNullOrEmpty(sourceFilePath_)) {
                Clipboard.SetText(sourceFilePath_);
            }
        }

        private void SourceFile_Open(object sender, RoutedEventArgs e) {
            if (!string.IsNullOrEmpty(sourceFilePath_)) {
                Utils.OpenExternalFile(sourceFilePath_);
            }
        }
    }
}
