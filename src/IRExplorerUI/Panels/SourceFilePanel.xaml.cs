// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;
using IRExplorerCore;
using IRExplorerCore.IR;
using IRExplorerCore.IR.Tags;
using IRExplorerUI.Compilers.ASM;
using IRExplorerUI.Document;
using IRExplorerUI.Profile;
using IRExplorerUI.Utilities;
using Microsoft.Win32;
using Microsoft.Windows.EventTracing;
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
        private ElementHighlighter profileMarker_;
        private OverlayRenderer overlayRenderer_;
        private bool hasProfileInfo_;
        private int hottestSourceLine_;

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

            if (line != null && Session.CurrentDocument != null) {
                selectedLine_ = line.LineNumber;
                Session.CurrentDocument.SelectElementsOnSourceLine(line.LineNumber);
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

        private async Task<bool> LoadSourceFile(string path) {
            try {
                string text = await File.ReadAllTextAsync(path);
                TextView.Text = text;
                PathTextbox.Text = path;
                currentFilePath_ = path;
                fileLoaded_ = true;
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

            // Check if there is profile info.
            var profile = Session.ProfileData?.GetFunctionProfile(section_.ParentFunction);

            if (profile != null) {
                if (!string.IsNullOrEmpty(profile.SourceFilePath)) {
                    var sourceFilePath = profile.SourceFilePath;
                    var mappedSourceFilePath = File.Exists(sourceFilePath) ?
                            sourceFilePath :
                            sourceFileMapper_.Map(sourceFilePath, () => BrowseSourceFile(
                                filter: $"Source File|{Path.GetFileName(sourceFilePath)}",
                                title: $"Open {sourceFilePath}"));
                    // Load new source file.
                    //? TODO: Scroll down to the start of the func
                    //? Could have an option to scroll to hottest part
                    if (mappedSourceFilePath != null && await LoadSourceFile(mappedSourceFilePath)) {
                        await AnnotateProfilerData(profile);
                    }
                    else {
                        TextView.Text = $"Failed to load profile source file {profile.SourceFilePath}!";
                    }
                }
                else {
                    TextView.Text = $"No source file available";
                }
            }
        }

        public override void OnDocumentSectionUnloaded(IRTextSection section, IRDocument document) {
            base.OnDocumentSectionUnloaded(section, document);
            ResetSelectedLine();

            overlayRenderer_.Clear();
            profileMarker_.Clear();
            section_ = null;
            fileLoaded_ = false;
            hasProfileInfo_ = false;
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
                ScrollToLine(tag.Line);
            }
        }

        private void ScrollToLine(int line) {
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
            ResetSelectedLine();
            TextView.Text = "";
            fileLoaded_ = false;
        }

        #endregion

        private async Task AnnotateProfilerData(FunctionProfileData profile) {
            hasProfileInfo_ = true;

            foreach (var pair in profile.ChildrenWeights) {
                var child = Session.MainDocumentSummary.GetFunctionWithId(pair.Key);
            }

            var markerOptions = ProfileDocumentMarkerOptions.Default;
            var nextElementId = new IRElementId();

            var function = Session.CurrentDocument.Function;
            var metadataTag = function.GetTag<AssemblyMetadataTag>();
            bool hasInstrOffsetMetadata = metadataTag != null && metadataTag.OffsetToElementMap.Count > 0;
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
            ScrollToLine(hottestSourceLine_);
        }

        public void AddElementOverlay(IRElement element, IconDrawing icon, int index,
                                    double width, double height, string toolTip,
                                    ProfileDocumentMarkerOptions options,
                                    HorizontalAlignment alignmentX = HorizontalAlignment.Right,
                                    VerticalAlignment alignmentY = VerticalAlignment.Center,
                                    double marginX = 8, double marginY = 2) {
            var overlay = IconElementOverlay.CreateDefault(icon, width, height,
                                                            Brushes.Transparent, Brushes.Transparent, null,
                                                            toolTip, alignmentX, alignmentY, marginX, marginY);
            overlay.IsToolTipPinned = true;

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
    }
}
