// Copyright(c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using IRExplorerUI.Diff;
using IRExplorerUI.OptionsPanels;
using IRExplorerCore;
using DiffPlex.DiffBuilder.Model;

namespace IRExplorerUI {
    public partial class MainWindow : Window, ISession {
        OptionsPanelHostWindow diffOptionsPanelHost_;
        private DateTime documentLoadStartTime_;
        private bool documentLoadProgressVisible_;
        private bool loadingDocuments_;
        private bool diffOptionsVisible_;

        private async void OpenBaseDiffDocumentsExecuted(object sender, ExecutedRoutedEventArgs e) {
            await OpenBaseDiffDocuments();
        }

        private async Task OpenBaseDiffDocuments() {
            var openWindow = new DiffOpenWindow();
            openWindow.Owner = this;
            var result = openWindow.ShowDialog();

            if (result.HasValue && result.Value) {
                await OpenBaseDiffsDocuments(openWindow.BaseFilePath, openWindow.DiffFilePath);
            }
        }

        private async Task OpenBaseDiffsDocuments(string baseFilePath, string diffFilePath) {
            bool loaded = await OpenBaseDiffIRDocumentsImpl(baseFilePath, diffFilePath);

            if (!loaded) {
                using var centerForm = new DialogCenteringHelper(this);
                MessageBox.Show("Failed to load base/diff files", "IR Explorer", MessageBoxButton.OK,
                                MessageBoxImage.Exclamation);
            }
        }

        private async void ToggleDiffModeExecuted(object sender, ExecutedRoutedEventArgs e) {
            if (sessionState_ == null) {
                return; // No sessions started yet.
            }

            if (IsInDiffMode) {
                await ExitDocumentDiffState();
            }
            else {
                await EnterDocumentDiffState();
            }
        }

        private async void SwapDiffDocumentsExecuted(object sender, ExecutedRoutedEventArgs e) {
            if (sessionState_ == null) {
                return; // No sessions started yet.
            }

            await SwapDiffedDocuments();
        }

        private async Task<bool> OpenBaseDiffIRDocumentsImpl(string baseFilePath, string diffFilePath) {
            bool result = false;

            try {
                await EndSession();
                UpdateUIBeforeLoadDocument($"Loading {baseFilePath}, {diffFilePath}");
                var baseTask = Task.Run(() => LoadDocument(baseFilePath, Guid.NewGuid(), UpdateIRDocumentLoadProgress));
                var diffTask = Task.Run(() => LoadDocument(diffFilePath, Guid.NewGuid(), UpdateIRDocumentLoadProgress));
                await Task.WhenAll(baseTask, diffTask);

                if (baseTask.Result != null && diffTask.Result != null) {
                    await SetupOpenedIRDocument(SessionKind.Default, baseFilePath, baseTask.Result);
                    await SetupOpenedDiffIRDocument(diffFilePath, diffTask.Result);
                    result = true;
                }
                else {
                    Trace.TraceWarning($"Failed to load base/diff documents: base {baseTask.Result != null}, diff {diffTask.Result != null}");
                }
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed to load base/diff documents: {ex}");
            }

            UpdateUIAfterLoadDocument();
            return result;
        }

        private async void ToggleButton_Checked(object sender, RoutedEventArgs e) {
            if (ignoreDiffModeButtonEvent_) {
                return;
            }

            bool result = await EnterDocumentDiffState();

            if (!result) {
                UpdateDiffModeButton(false);
            }
        }

        private async void OpenDiffDocumentExecuted(object sender, ExecutedRoutedEventArgs e) {
            string filePath = ShowOpenFileDialog();

            if (filePath != null) {
                bool loaded = await OpenDiffIRDocument(filePath);

                if (!loaded) {
                    using var centerForm = new DialogCenteringHelper(this);
                    MessageBox.Show($"Failed to load diff file {filePath}", "IR Explorer",
                                    MessageBoxButton.OK, MessageBoxImage.Exclamation);
                }
            }
        }

        private async void CloseDiffDocumentExecuted(object sender, ExecutedRoutedEventArgs e) {
            if (!sessionState_.IsInTwoDocumentsDiffMode) {
                await ExitDocumentDiffState();

                // Close each opened section associated with this document.
                var closedDocuments = new List<DocumentHostInfo>();

                foreach (var docHostInfo in sessionState_.DocumentHosts) {
                    var summary = docHostInfo.DocumentHost.Section.ParentFunction.ParentSummary;

                    if (summary == sessionState_.DiffDocument.Summary) {
                        CloseDocument(docHostInfo);
                        closedDocuments.Add(docHostInfo);
                    }
                }

                foreach (var docHostInfo in closedDocuments) {
                    sessionState_.DocumentHosts.Remove(docHostInfo);
                }

                sessionState_.RemoveLoadedDocuemnt(sessionState_.DiffDocument);

                // Reset the section panel.
                SectionPanel.DiffSummary = null;
                sessionState_.ExitTwoDocumentDiffMode();
                UpdateWindowTitle();
            }
        }

        private bool IsDiffModeDocument(IRDocumentHost document) {
            return IsInDiffMode &&
                   (document == sessionState_.SectionDiffState.LeftDocument ||
                    document == sessionState_.SectionDiffState.RightDocument);
        }

        private async Task<bool> EnterDocumentDiffState() {
            if (sessionState_ == null) {
                // No session started yet.
                return false;
            }

            await sessionState_.SectionDiffState.StartModeChange();

            if (IsInDiffMode) {
                sessionState_.SectionDiffState.EndModeChange();
                return true;
            }

            if (!PickLeftRightDocuments(out var leftDocument, out var rightDocument)) {
                sessionState_.SectionDiffState.EndModeChange();
                return false;
            }

            bool result = await EnterDocumentDiffState(leftDocument, rightDocument);
            sessionState_.SectionDiffState.EndModeChange();
            return result;
        }

        private async Task<bool> EnterDocumentDiffState(IRDocumentHost leftDocument,
                                                        IRDocumentHost rightDocument) {
            //? TODO: Both these checks should be an assert
            if (sessionState_ == null) {
                // No session started yet.
                return false;
            }

            if (IsInDiffMode) {
                return true;
            }

            sessionState_.SectionDiffState.LeftDocument = leftDocument;
            sessionState_.SectionDiffState.RightDocument = rightDocument;

            if (sessionState_.IsInTwoDocumentsDiffMode) {
                // Used when diffing two different documents.
                await SwitchDiffedDocumentSection(leftDocument.Section, leftDocument, false);
            }
            else {
                sessionState_.SectionDiffState.LeftSection = leftDocument.Section;
                sessionState_.SectionDiffState.RightSection = rightDocument.Section;
                await DiffCurrentDocuments(sessionState_.SectionDiffState);
            }

            // CreateDefaultSideBySidePanels();
            ShowDiffsControlsPanel();
            UpdateUIAfterSectionSwitch(leftDocument.Section, leftDocument);
            UpdateUIAfterSectionSwitch(rightDocument.Section, rightDocument);
            return true;
        }

        private bool PickLeftRightDocuments(out IRDocumentHost leftDocument,
                                            out IRDocumentHost rightDocument) {
            if (sessionState_.DocumentHosts.Count < 2) {
                leftDocument = rightDocument = null;
                return false;
            }

            // If one of the sections is already open, pick the associated document.
            // Otherwise, pick the last two created ones.
            leftDocument = sessionState_.DocumentHosts[^2].DocumentHost;
            rightDocument = sessionState_.DocumentHosts[^1].DocumentHost;
            return true;
        }

        private async Task ExitDocumentDiffState(bool isSessionEnding = false, bool disableControls = true) {
            await sessionState_.SectionDiffState.StartModeChange();

            if (!IsInDiffMode) {
                sessionState_.SectionDiffState.EndModeChange();
                return;
            }

            var leftDocument = sessionState_.SectionDiffState.LeftDocument;
            var rightDocument = sessionState_.SectionDiffState.RightDocument;
            sessionState_.SectionDiffState.End();

            if (!isSessionEnding) {
                // Reload sections in the same documents.
                Trace.TraceInformation("Diff mode: Reload original sections");
                var leftArgs = new OpenSectionEventArgs(leftDocument.Section, OpenSectionKind.ReplaceCurrent);
                var rightArgs = new OpenSectionEventArgs(rightDocument.Section, OpenSectionKind.ReplaceCurrent);

                await OpenDocumentSection(leftArgs, leftDocument, false);
                await leftDocument.ExitDiffMode();
                await OpenDocumentSection(rightArgs, rightDocument, false);
                await rightDocument.ExitDiffMode();
            }

            if (disableControls) {
                HideDiffsControlsPanel();
            }

            sessionState_.SectionDiffState.EndModeChange();
            sessionState_.ClearDiffModePanelState();
            Trace.TraceInformation("Diff mode: Exited");
        }

        private async void SectionPanel_EnterDiffMode(object sender, DiffModeEventArgs e) {
            if (IsInDiffMode) {
                sessionState_.SectionDiffState.End();
            }

            await sessionState_.SectionDiffState.StartModeChange();
            var leftDocument = FindDocumentWithSection(e.Left.Section);
            var rightDocument = FindDocumentWithSection(e.Right.Section);

            Trace.TraceInformation($"Diff mode: Start with left doc. {ObjectTracker.Track(leftDocument)}, " +
                                   $"right doc. {ObjectTracker.Track(rightDocument)}");

            leftDocument = await OpenDocumentSection(e.Left, leftDocument, false);
            rightDocument = await OpenDocumentSection(e.Right, rightDocument, false);
            bool result = await EnterDocumentDiffState(leftDocument, rightDocument);

            UpdateDiffModeButton(result);
            sessionState_.SectionDiffState.EndModeChange();
            Trace.TraceInformation("Diff mode: Entered");
        }

        private async void SectionPanel_SyncDiffedDocumentsChanged(object sender, bool value) {
            if(IsInDiffMode) {
                if ((IsInTwoDocumentsDiffMode && !value) ||
                    (!IsInTwoDocumentsDiffMode && value)) {
                    await ExitDocumentDiffState();
                }
            }

            sessionState_.SyncDiffedDocuments = value;
        }
        
        private async Task DiffCurrentDocuments(DiffModeInfo diffState) {
            await EnableDocumentDiffState(diffState);
            var leftDocument = diffState.LeftDocument.TextView;
            var rightDocument = diffState.RightDocument.TextView;
            
            var leftText = await GetSectionTextAsync(leftDocument.Section);
            var rightText = await GetSectionTextAsync(rightDocument.Section);
            await DiffDocuments(leftDocument, rightDocument, leftText, rightText,
                              leftDocument.Section, rightDocument.Section);
            
            if (diffState.PassOutputVisible) {
                await DiffDocumentPassOutput(leftDocument, rightDocument, 
                                             diffState.PassOutputShowBefore);
            }
        }

        private async Task EnableDocumentDiffState(DiffModeInfo diffState) {
            await diffState.LeftDocument.EnterDiffMode();
            await diffState.RightDocument.EnterDiffMode();
            sessionState_.SectionDiffState.IsEnabled = true;
        }

        private Task<DiffMarkingResult> 
            MarkSectionDiffs(IRTextSection section, string text, string otherText,
                             DiffPaneModel diff, DiffPaneModel otherDiff, bool isRightDoc,
                             IDiffOutputFilter diffFilter, DiffStatistics diffStats) {
            var diffUpdater = new DocumentDiffUpdater(diffFilter, App.Settings.DiffSettings, compilerInfo_);

            return Task.Run(() => {
                var result = diffUpdater.MarkDiffs(text, otherText, diff, otherDiff,
                                                   isRightDoc, diffStats);
                diffUpdater.ReparseDiffedFunction(result, section);
                return result;
            });
        }

        private Task<DiffMarkingResult> MarkIdenticalSectionDiffs(IRTextSection section, string text,
                                                            IDiffOutputFilter diffFilter) {
            var diffUpdater = new DocumentDiffUpdater(diffFilter, App.Settings.DiffSettings, compilerInfo_);

            return Task.Run(() => {
                var result = diffUpdater.CreateNoDiffDocument(text);
                diffUpdater.ReparseDiffedFunction(result, section);
                return result;
            });
        }

        private async Task<SideBySideDiffModel> ComputeSectionDiffs(string leftText, string rightText,
                                                            IRTextSection newLeftSection,
                                                            IRTextSection newRightSection) {
            if (CanReuseSectionDiffs(newLeftSection, newRightSection)) {
                return sessionState_.SectionDiffState.CurrentDiffResults;
            }

            // Start the actual document diffing on another thread.
            var diffBuilder = new DocumentDiffBuilder(App.Settings.DiffSettings);
            var diff = await Task.Run(() => diffBuilder.ComputeDiffs(leftText, rightText));
            sessionState_.SectionDiffState.CurrentDiffResults = diff;
            sessionState_.SectionDiffState.CurrentDiffSettings = App.Settings.DiffSettings;
            return diff;
        }

        private bool IsSectionTextDifferent(IRTextSection sectionA, IRTextSection sectionB) {
            if (!sessionState_.AreSectionSignaturesComputed(sectionA) ||
                !sessionState_.AreSectionSignaturesComputed(sectionB)) {
                return true;
            }

            return sectionA.IsSectionTextDifferent(sectionB);
        }

        private bool CanReuseSectionDiffs(IRTextSection newLeftSection, IRTextSection newRightSection) {
            // Check if the text of the two sections is the same
            // as the ones being currently compared.
            if (sessionState_.SectionDiffState.CurrentDiffResults == null ||
                sessionState_.SectionDiffState.CurrentDiffSettings == null) {
                return false;
            }

            if (!sessionState_.SectionDiffState.CurrentDiffSettings.Equals(App.Settings.DiffSettings)) {
                return false;
            }

            return !IsSectionTextDifferent(newLeftSection, sessionState_.SectionDiffState.LeftSection) && 
                   !IsSectionTextDifferent(newRightSection, sessionState_.SectionDiffState.RightSection);
        }

        private async Task DiffDocuments(IRDocument leftDocument, IRDocument rightDocument, 
                                      string leftText, string rightText, 
                                      IRTextSection newLeftSection,
                                      IRTextSection newRightSection) {
            var leftDiffStats = new DiffStatistics();
            var rightDiffStats = new DiffStatistics();
            DiffMarkingResult leftDiffResult;
            DiffMarkingResult rightDiffResult;

            // Create the diff filter that will post-process the diff results.
            var diffInputFilter = compilerInfo_.CreateDiffInputFilter();
            var diffFilter = compilerInfo_.CreateDiffOutputFilter();
            diffInputFilter?.Initialize(App.Settings.DiffSettings, compilerInfo_.IR);
            diffFilter?.Initialize(App.Settings.DiffSettings, compilerInfo_.IR);

            // Fairly often the text is identical, don't do the diffing for such cases.
            // Also frequent is to have different text, but it is on both left/right sides
            // identical to the previous sections - in this case the diff results are reused.
            if(IsSectionTextDifferent(newLeftSection, newRightSection)) {
                var leftDiffText = leftText;
                var rightDiffText = rightText;

                if (diffInputFilter != null) {
                    leftDiffText = diffInputFilter.FilterInputText(leftText);
                    rightDiffText = diffInputFilter.FilterInputText(rightText);

                    leftText = leftDiffText;
                    rightText = rightDiffText;
                }

                var diff = await ComputeSectionDiffs(leftDiffText, rightDiffText, newLeftSection, newRightSection);

                // Apply the diff results on the left and right documents in parallel.
                // This will produce two AvalonEdit documents that will be installed 
                // in the doc. hosts once back on the UI thread.
                var leftMarkTask = MarkSectionDiffs(newLeftSection, leftText, rightText,
                    diff.OldText, diff.NewText,
                    false, diffFilter, leftDiffStats);
                var rightMarkTask = MarkSectionDiffs(newRightSection, leftText, rightText,
                    diff.NewText, diff.OldText,
                    true, diffFilter, rightDiffStats);

                await Task.WhenAll(leftMarkTask, rightMarkTask);
                leftDiffResult = await leftMarkTask;
                rightDiffResult = await rightMarkTask;
            }
            else {
                var leftMarkTask = MarkIdenticalSectionDiffs(newLeftSection, leftText, diffFilter);
                var rightMarkTask = MarkIdenticalSectionDiffs(newRightSection, rightText, diffFilter);

                await Task.WhenAll(leftMarkTask, rightMarkTask);
                leftDiffResult = await leftMarkTask;
                rightDiffResult = await rightMarkTask;
            }

            // Update the diff session state.
            sessionState_.SectionDiffState.UpdateResults(leftDiffResult, newLeftSection,
                                                    rightDiffResult, newRightSection);

            // The UI-thread dependent work.
            var leftDocumentHost = FindDocumentHost(leftDocument);
            var rightDocumentHost = FindDocumentHost(rightDocument);

            NotifyPanelsOfSectionUnload(leftDocument.Section, leftDocumentHost, true);
            NotifyPanelsOfSectionUnload(rightDocument.Section, rightDocumentHost, true);

            //? TODO: Workaround for some cases where the updated left/right docs
            //? don't have the same length due to a bug in diff updating.
            //? Making the docs the same length by appending whitespace prevents other
            //? problems that usually end up with the application asserting.
            if (leftDiffResult.DiffText.Length !=
                rightDiffResult.DiffText.Length) {
                var length = Math.Max(leftDiffResult.DiffText.Length,
                                      rightDiffResult.DiffText.Length);
                leftDiffResult.DiffText = leftDiffResult.DiffText.PadRight(length);
                rightDiffResult.DiffText = rightDiffResult.DiffText.PadRight(length);
            }
            
            await leftDocumentHost.LoadDiffedFunction(leftDiffResult, newLeftSection);
            await rightDocumentHost.LoadDiffedFunction(rightDiffResult, newRightSection);

            ScrollToFirstDiff(leftDocument, rightDocument, leftDiffResult, rightDiffResult);
            UpdateDiffStatus(rightDiffStats);

            // For the active document only, notify panels of the change
            // and redo other section post-load tasks.
            if (IsActiveDocument(leftDocumentHost)) {
                NotifyPanelsOfSectionLoad(newLeftSection, leftDocumentHost, true);
                await GenerateGraphs(newLeftSection, leftDocument, false);
            }
            else if(IsActiveDocument(rightDocumentHost)) {
                NotifyPanelsOfSectionLoad(newRightSection, rightDocumentHost, true);
                await GenerateGraphs(newRightSection, rightDocument, false);
            }
        }

        private async Task DiffDocumentPassOutput() {
            await DiffDocumentPassOutput(sessionState_.SectionDiffState.LeftDocument.TextView,
                                         sessionState_.SectionDiffState.RightDocument.TextView,
                                         sessionState_.SectionDiffState.PassOutputShowBefore);
        }

        private async Task DiffDocumentPassOutput(IRDocument leftDocument, 
                                                  IRDocument rightDocument,
                                                  bool useOutputBefore) {
            var leftText = await GetSectionOutputTextAsync(leftDocument.Section, useOutputBefore);
            var rightText = await GetSectionOutputTextAsync(rightDocument.Section, useOutputBefore);

            // Start the actual document diffing on another thread.
            var diffBuilder = new DocumentDiffBuilder(App.Settings.DiffSettings);
            var diff = await Task.Run(() => diffBuilder.ComputeDiffs(leftText, rightText));

            // Apply the diff results on the left and right documents in parallel.
            // This will produce two AvalonEdit documents that will be installed 
            // in the doc. hosts once back on the UI thread.
            var leftDiffStats = new DiffStatistics();
            var rightDiffStats = new DiffStatistics();
            var diffFilter = compilerInfo_.CreateDiffOutputFilter();
            diffFilter.Initialize(App.Settings.DiffSettings, compilerInfo_.IR);

            var leftMarkTask = MarkSectionDiffs(leftDocument.Section, leftText, rightText,
                                                diff.OldText, diff.NewText,
                                                false, diffFilter, leftDiffStats);
            var rightMarkTask = MarkSectionDiffs(rightDocument.Section, leftText, rightText,
                                                 diff.NewText, diff.OldText,
                                                 true, diffFilter, rightDiffStats);
            await Task.WhenAll(leftMarkTask, rightMarkTask);
            var leftDiffResult = await leftMarkTask;
            var rightDiffResult = await rightMarkTask;

            var leftDocumentHost = FindDocumentHost(leftDocument);
            var rightDocumentHost = FindDocumentHost(rightDocument);
            await leftDocumentHost.LoadDiffedPassOutput(leftDiffResult);
            await rightDocumentHost.LoadDiffedPassOutput(rightDiffResult);
        }

        private void ScrollToFirstDiff(IRDocument leftDocument, IRDocument rightDocument, 
                                       DiffMarkingResult leftDiffResult, DiffMarkingResult rightDiffResult) {
            // Scroll to the first diff. If minor diffs are enabled, scroll to the first 
            // major diff in either the left or right document.
            var firstLeftDiff = SelectFirstDiff(leftDiffResult);
            var firstRightDiff = SelectFirstDiff(rightDiffResult);
            DiffTextSegment firstDiff = firstLeftDiff;
            IRDocument firstDiffDocument = leftDocument;

            if (firstRightDiff != null) {
                if (firstDiff == null || firstDiff.StartOffset > firstRightDiff.StartOffset) {
                    firstDiff = firstRightDiff;
                    firstDiffDocument = rightDocument;
                }
            }

            if (firstDiff != null) {
                firstDiffDocument.BringTextOffsetIntoView(firstDiff.StartOffset);
            }
            else {
                // When there are no diffs, scroll both documents to the start.
                leftDocument.BringTextOffsetIntoView(0);
                rightDocument.BringTextOffsetIntoView(0);
            }
        }

        private DiffTextSegment SelectFirstDiff(DiffMarkingResult diffResult) {
            if(diffResult.DiffSegments.Count == 0) {
                return null;
            }

            if(App.Settings.DiffSettings.IdentifyMinorDiffs) {
                // Pick the first major diff.
                foreach(var diff in diffResult.DiffSegments) {
                    if(diff.Kind != DiffKind.MinorModification) {
                        return diff;
                    }
                }

                return null;
            }
            
            return diffResult.DiffSegments[0];
        }

        private async Task SwitchDiffedDocumentSection(IRTextSection section, IRDocumentHost document,
                                                       bool redoDiffs = true) {
            string leftText = null;
            string rightText = null;
            IRTextSection newLeftSection = null;
            IRTextSection newRightSection = null;

            if (document == sessionState_.SectionDiffState.LeftDocument) {
                var result = await Task.Run(() => LoadAndParseSection(section));
                leftText = result.Text;
                newLeftSection = section;

                (rightText, newRightSection) =
                    await SwitchOtherDiffedDocumentSide(section, sessionState_.SectionDiffState.RightDocument.Section,
                                                        sessionState_.DiffDocument);
                newRightSection ??= sessionState_.SectionDiffState.RightDocument.Section;
            }
            else if (document == sessionState_.SectionDiffState.RightDocument) {
                var result = await Task.Run(() => LoadAndParseSection(section));
                rightText = result.Text;
                newRightSection = section;

                (leftText, newLeftSection) =
                    await SwitchOtherDiffedDocumentSide(section, sessionState_.SectionDiffState.LeftDocument.Section,
                                                        sessionState_.MainDocument);
                newLeftSection ??= sessionState_.SectionDiffState.LeftDocument.Section;
            }
            else {
                // Document is not part of the diff set.
                return;
            }

            var leftDocument = sessionState_.SectionDiffState.LeftDocument;
            var rightDocument = sessionState_.SectionDiffState.RightDocument;
            await EnableDocumentDiffState(sessionState_.SectionDiffState);

            if (newLeftSection != null) {
                UpdateUIAfterSectionSwitch(newLeftSection, leftDocument);
            }

            if (newRightSection != null) {
                UpdateUIAfterSectionSwitch(newRightSection, rightDocument);
            }

            await DiffDocuments(leftDocument.TextView, rightDocument.TextView,
                              leftText, rightText,
                              newLeftSection, newRightSection);

            if (sessionState_.SectionDiffState.PassOutputVisible) {
                await DiffDocumentPassOutput(leftDocument.TextView, rightDocument.TextView,
                                             sessionState_.SectionDiffState.PassOutputShowBefore);
            }
        }

        private void HandleNoDiffDocuments(IRDocument leftDocument, IRDocument rightDocument) {
            // No diffs, don't run the differ.
            leftDocument.RemoveDiffTextSegments();
            rightDocument.RemoveDiffTextSegments();
            UpdateDiffStatus(new DiffStatistics());
        }

        private async Task<Tuple<string, IRTextSection>>
            SwitchOtherDiffedDocumentSide(IRTextSection section, IRTextSection otherSection,
                                          LoadedDocument otherDocument) {
            if (sessionState_.DiffDocument != null) {
                // When two documents are compared, try to pick 
                // the other section from that other document.
                var diffSection = await FindDiffDocumentSection(section, otherDocument);

                if (diffSection != null) {
                    var result = await Task.Run(() => LoadAndParseSection(diffSection));
                    return new Tuple<string, IRTextSection>(result.Text, diffSection);
                }
                else {
                    return new Tuple<string, IRTextSection>($"Diff document does not have section {section.Name}", null);
                }
            }
            else {
                // Load the text of the other section, but don't reload anything else.
                var result = await Task.Run(() => LoadAndParseSection(otherSection));
                return new Tuple<string, IRTextSection>(result.Text, null);
            }
        }

        private async Task<IRTextSection> FindDiffDocumentSection(IRTextSection section, LoadedDocument diffDoc) {
            await SectionPanel.SelectFunction(section.ParentFunction);
            return SectionPanel.FindDiffDocumentSection(section);
        }

        private void UpdateDiffModeButton(bool state) {
            ignoreDiffModeButtonEvent_ = true;
            DiffModeButton.IsChecked = state;
            ignoreDiffModeButtonEvent_ = false;
            ;
        }
        private void UpdateDiffStatus(DiffStatistics stats) {
            string text = "";

            if (stats.LinesAdded == 0 && stats.LinesDeleted == 0 && stats.LinesModified == 0) {
                text = "0 diffs";
            }
            else {
                text = $"A {stats.LinesAdded}, D {stats.LinesDeleted}, M {stats.LinesModified}";
            }

            DiffStatusText.Text = text;
        }

        private async Task UpdateDiffedFunction(IRDocument document, DiffMarkingResult diffResult,
                                                IRTextSection newSection) {
            var documentHost = FindDocumentHost(document);
            NotifyPanelsOfSectionUnload(document.Section, documentHost, true);

            // Load new text and function after diffing.
            await documentHost.LoadDiffedFunction(diffResult, newSection);
            NotifyPanelsOfSectionLoad(document.Section, documentHost, true);

            await GenerateGraphs(newSection, document, false);
        }

        private async void DiffModeButton_Unchecked(object sender, RoutedEventArgs e) {
            if (ignoreDiffModeButtonEvent_) {
                return;
            }

            await ExitDocumentDiffState();
        }

        private void HideDiffsControlsPanel() {
            UpdateDiffModeButton(false);
            DiffControlsPanel.Visibility = Visibility.Collapsed;
        }

        private void ShowDiffsControlsPanel() {
            UpdateDiffModeButton(true);
            DiffControlsPanel.Visibility = Visibility.Visible;
        }

        private void ShowDiffOptionsPanel() {
            if (diffOptionsVisible_) {
                return;
            }

            var width = Math.Max(DiffOptionsPanel.MinimumWidth,
                    Math.Min(MainGrid.ActualWidth, DiffOptionsPanel.DefaultWidth));
            var height = Math.Max(DiffOptionsPanel.MinimumHeight,
                    Math.Min(MainGrid.ActualHeight, DiffOptionsPanel.DefaultHeight));
            var position = MainGrid.PointToScreen(new Point(230, MainMenu.ActualHeight + 1));
            diffOptionsPanelHost_ = new OptionsPanelHostWindow(new DiffOptionsPanel(), position, width, height, this);
            diffOptionsPanelHost_.PanelClosed += DiffOptionsPanel_PanelClosed;
            diffOptionsPanelHost_.PanelReset += DiffOptionsPanel_PanelReset;
            diffOptionsPanelHost_.SettingsChanged += DiffOptionsPanel_SettingsChanged;
            diffOptionsPanelHost_.Settings = (DiffSettings)App.Settings.DiffSettings.Clone();
            diffOptionsPanelHost_.IsOpen = true;
            diffOptionsVisible_ = true;
        }

        private async Task CloseDiffOptionsPanel() {
            if (!diffOptionsVisible_) {
                return;
            }

            diffOptionsPanelHost_.IsOpen = false;
            diffOptionsPanelHost_.PanelClosed -= DiffOptionsPanel_PanelClosed;
            diffOptionsPanelHost_.PanelReset -= DiffOptionsPanel_PanelReset;
            diffOptionsPanelHost_.SettingsChanged -= DiffOptionsPanel_SettingsChanged;

            var newSettings = (DiffSettings)diffOptionsPanelHost_.Settings;
            await HandleNewDiffSettings(newSettings, true);

            diffOptionsPanelHost_ = null;
            diffOptionsVisible_ = false;
        }

        private async Task HandleNewDiffSettings(DiffSettings newSettings, bool commit) {
            if (newSettings.HasChanges(App.Settings.DiffSettings)) {
                bool hasHandlingChanges = App.Settings.DiffSettings.HasDiffHandlingChanges(newSettings);
                App.Settings.DiffSettings = newSettings;
                await ReloadDiffSettings(newSettings, hasHandlingChanges);

                if (commit) {
                    App.SaveApplicationSettings();
                }
            }
        }

        private async void DiffOptionsPanel_PanelReset(object sender, EventArgs e) {
            var newSettings = new DiffSettings();
            diffOptionsPanelHost_.Settings = null;
            diffOptionsPanelHost_.Settings = newSettings;
            await HandleNewDiffSettings(newSettings, true);
        }

        private async void DiffOptionsPanel_SettingsChanged(object sender, EventArgs e) {
            var newSettings = (DiffSettings)diffOptionsPanelHost_.Settings;

            if (newSettings != null) {
                await HandleNewDiffSettings(newSettings, false);

                // It's possible that the options panel closes before the async method returns.
                if (diffOptionsPanelHost_ != null) {
                    diffOptionsPanelHost_.Settings = null;
                    diffOptionsPanelHost_.Settings = newSettings.Clone();
                }
            }
        }

        private async void DiffSettingsButton_Click(object sender, RoutedEventArgs e) {
            if (diffOptionsVisible_) {
                await CloseDiffOptionsPanel();
            }
            else {
                ShowDiffOptionsPanel();
            }
        }

        private async void DiffOptionsPanel_PanelClosed(object sender, EventArgs e) {
            await CloseDiffOptionsPanel();
        }

        private async Task DiffSingleDocumentSections(IRDocumentHost doc, IRTextSection section, IRTextSection prevSection) {
            var prevText = sessionState_.FindLoadedDocument(prevSection).Loader.GetSectionText(prevSection);
            var currentText = sessionState_.FindLoadedDocument(section).Loader.GetSectionText(section);

            var diffBuilder = new DocumentDiffBuilder(App.Settings.DiffSettings);
            var diff = await Task.Run(() => diffBuilder.ComputeDiffs(prevText, currentText));

            var diffStats = new DiffStatistics();
            var diffFilter = compilerInfo_.CreateDiffOutputFilter();
            diffFilter.Initialize(App.Settings.DiffSettings, compilerInfo_.IR);
            
            var diffResult = await MarkSectionDiffs(section, prevText, currentText, 
                                                    diff.NewText, diff.OldText,
                                                    true, diffFilter, diffStats);
            await UpdateDiffedFunction(doc.TextView, diffResult, section);
            DiffStatusText.Text = diffStats.ToString();
        }

        private void PreviousSegmentDiffButton_Click(object sender, RoutedEventArgs e) {
            if (!IsInDiffMode) {
                return;
            }

            //? TODO: Diff segments from left/right must be combined and sorted by offset
            //? TODO: This is almost identical to the next case
            var diffResults = sessionState_.SectionDiffState.RightDiffResults;
            var document = sessionState_.SectionDiffState.RightDocument;

            if (diffResults.DiffSegments.Count > 0) {
                int offset = document.TextView.CaretOffset;
                int index = diffResults.CurrentSegmentIndex;
                var currentSegment = diffResults.DiffSegments[index];
                int currentLine = document.TextView.Document.GetLineByOffset(offset).LineNumber;
                bool found = false;
                ;

                //? TODO: Should use binary search
                while (index >= 0) {
                    var candidate = diffResults.DiffSegments[index];

                    if (candidate.StartOffset < offset) {
                        int line = document.TextView.Document.GetLineByOffset(candidate.StartOffset).LineNumber;

                        if (line < currentLine) {
                            // Skip groups of segments of the same type following one after another in the text
                            // after the first one in the group has been selected.
                            if (candidate.Kind == currentSegment.Kind && line == currentLine - 1) {
                                currentLine = line;
                                currentSegment = candidate;
                                index--;
                                continue;
                            }

                            found = true;
                            break;
                        }
                    }

                    index--;
                }

                if (found) {
                    JumpToDiffSegmentAtIndex(index, diffResults, document);
                }
            }
        }

        private void NextDiffSegmentButton_Click(object sender, RoutedEventArgs e) {
            if (!IsInDiffMode) {
                return;
            }

            // TODO: Diff segments from left/right must be combined and sorted by offset
            // TODO: When next/prev segment is needed, start from the current carret offset
            var diffResults = sessionState_.SectionDiffState.RightDiffResults;
            var document = sessionState_.SectionDiffState.RightDocument;

            if (diffResults.DiffSegments.Count > 0) {
                int offset = document.TextView.CaretOffset;
                int index = diffResults.CurrentSegmentIndex;
                var currentSegment = diffResults.DiffSegments[index];
                int currentLine = document.TextView.Document.GetLineByOffset(offset).LineNumber;
                bool found = false;
                ;

                //? TODO: Should use binary search
                while (index < diffResults.DiffSegments.Count) {
                    var candidate = diffResults.DiffSegments[index];

                    if (candidate.StartOffset > offset) {
                        int line = document.TextView.Document.GetLineByOffset(candidate.StartOffset).LineNumber;

                        if (line > currentLine) {
                            // Skip groups of segments of the same type following one after another in the text
                            // after the first one in the group has been selected.
                            if (candidate.Kind == currentSegment.Kind && line == currentLine + 1) {
                                currentLine = line;
                                currentSegment = candidate;
                                index++;
                                continue;
                            }

                            found = true;
                            break;
                        }
                    }

                    index++;
                }

                if (found) {
                    JumpToDiffSegmentAtIndex(index, diffResults, document);
                }
            }
        }

        private async void DiffSwapButton_Click(object sender, RoutedEventArgs e) {
            await SwapDiffedDocuments();
        }

        private async Task SwapDiffedDocuments() {
            if (!IsInDiffMode) {
                return;
            }

            var leftDocHost = sessionState_.SectionDiffState.LeftDocument;
            var rightDocHost = sessionState_.SectionDiffState.RightDocument;
            var leftSection = leftDocHost.Section;
            var rightSection = rightDocHost.Section;

            DiffSwapButton.IsEnabled = false;
            await ExitDocumentDiffState(isSessionEnding: false, disableControls: false);

            // Swap the section displayed in the documents.
            await SwitchSection(rightSection, leftDocHost, false);
            await SwitchSection(leftSection, rightDocHost, false);
            await EnterDocumentDiffState(leftDocHost, rightDocHost);
            DiffSwapButton.IsEnabled = true;
        }

        private async void ExternalDiffButton_Click(object sender, RoutedEventArgs e) {
            var newSettings = (DiffSettings)App.Settings.DiffSettings.Clone();
            newSettings.DiffImplementation = newSettings.DiffImplementation == DiffImplementationKind.Internal ?
                                             DiffImplementationKind.External : DiffImplementationKind.Internal;
            await HandleNewDiffSettings(newSettings, false);
        }

        private async void PreviousDiffButton_Click(object sender, RoutedEventArgs e) {
            if (!IsInDiffMode) {
                return;
            }

            var leftSection = sessionState_.SectionDiffState.LeftSection;
            var rightSection = sessionState_.SectionDiffState.RightSection;

            int leftIndex = leftSection.Number - 1;
            int rightIndex = rightSection.Number - 1;

            if (leftIndex > 0 && rightIndex > 0) {
                var prevLeftSection = leftSection.ParentFunction.Sections[leftIndex - 1];
                var leftArgs = new OpenSectionEventArgs(prevLeftSection, OpenSectionKind.ReplaceCurrent);
                await OpenDocumentSection(leftArgs, sessionState_.SectionDiffState.LeftDocument);

                var prevRightSection = rightSection.ParentFunction.Sections[rightIndex - 1];
                var rightArgs = new OpenSectionEventArgs(prevRightSection, OpenSectionKind.ReplaceCurrent);
                await OpenDocumentSection(rightArgs, sessionState_.SectionDiffState.RightDocument);
            }
        }

        private async void NextDiffButton_Click(object sender, RoutedEventArgs e) {
            if (!IsInDiffMode) {
                return;
            }

            var leftSection = sessionState_.SectionDiffState.LeftSection;
            var rightSection = sessionState_.SectionDiffState.RightSection;

            int leftIndex = leftSection.Number - 1;
            int rightIndex = rightSection.Number - 1;

            if (leftIndex < leftSection.ParentFunction.SectionCount - 1 &&
                rightIndex < rightSection.ParentFunction.SectionCount - 1) {
                var prevLeftSection = leftSection.ParentFunction.Sections[leftIndex + 1];
                var leftArgs = new OpenSectionEventArgs(prevLeftSection, OpenSectionKind.ReplaceCurrent);
                await OpenDocumentSection(leftArgs, sessionState_.SectionDiffState.LeftDocument);

                var prevRightSection = rightSection.ParentFunction.Sections[rightIndex + 1];
                var rightArgs = new OpenSectionEventArgs(prevRightSection, OpenSectionKind.ReplaceCurrent);
                await OpenDocumentSection(rightArgs, sessionState_.SectionDiffState.RightDocument);
            }
        }

        private void JumpToDiffSegmentAtIndex(int index, DiffMarkingResult diffResults, IRDocumentHost document) {
            var nextDiff = diffResults.DiffSegments[index];
            document.TextView.BringTextOffsetIntoView(nextDiff.StartOffset);
            document.TextView.SetCaretAtOffset(nextDiff.StartOffset);
            diffResults.CurrentSegmentIndex = index;
        }

        private void CloseDiffDocumentCanExecute(object sender, CanExecuteRoutedEventArgs e) {
            e.CanExecute = sessionState_ != null && sessionState_.IsInTwoDocumentsDiffMode;
            e.Handled = true;
        }


        public async Task ReloadDiffSettings(DiffSettings newSettings, bool hasHandlingChanges) {
            if (!IsInDiffMode) {
                return;
            }

            if (hasHandlingChanges) {
                // Diffs must be recomputed.
                var leftDocument = sessionState_.SectionDiffState.LeftDocument;
                var rightDocument = sessionState_.SectionDiffState.RightDocument;

                await ExitDocumentDiffState();
                await EnterDocumentDiffState(leftDocument, rightDocument);
            }
            else {
                // Only diff style must be updated.
                sessionState_.SectionDiffState.LeftDocument.ReloadSettings();
                sessionState_.SectionDiffState.RightDocument.ReloadSettings();
            }
        }

    }
}
