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

namespace IRExplorerUI {
    public partial class MainWindow : Window, ISessionManager {
        OptionsPanelHostWindow sharingPanelHost_;
        private DateTime documentLoadStartTime_;
        private bool documentLoadProgressVisible_;
        private bool loadingDocuments_;
        private bool diffOptionsVisible_;

        public bool IsInDiffMode => sessionState_.DiffState.IsEnabled;

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
            if (IsInDiffMode) {
                await ExitDocumentDiffState();
            }
            else {
                await EnterDocumentDiffState();
            }
        }

        private async void SwapDiffDocumentsExecuted(object sender, ExecutedRoutedEventArgs e) {
            await SwapDiffedDocuments();
        }

        private async Task<bool> OpenBaseDiffIRDocumentsImpl(string baseFilePath, string diffFilePath) {
            bool result = false;

            try {
                EndSession();
                UpdateUIBeforeLoadDocument($"Loading {baseFilePath}, {diffFilePath}");
                var baseTask = Task.Run(() => LoadDocument(baseFilePath, UpdateIRDocumentLoadProgress));
                var diffTask = Task.Run(() => LoadDocument(diffFilePath, UpdateIRDocumentLoadProgress));
                await Task.WhenAll(baseTask, diffTask);

                if (baseTask.Result != null && diffTask.Result != null) {
                    SetupOpenedIRDocument(SessionKind.Default, baseFilePath, baseTask.Result);
                    SetupOpenedDiffIRDocument(diffFilePath, diffTask.Result);
                    result = true;
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
            if (diffDocument_ != null) {
                await ExitDocumentDiffState();

                // Close each opened section associated with this document.
                var closedDocuments = new List<DocumentHostInfo>();

                foreach (var docHostInfo in sessionState_.DocumentHosts) {
                    var summary = docHostInfo.DocumentHost.Section.ParentFunction.ParentSummary;

                    if (summary == diffDocument_.Summary) {
                        CloseDocument(docHostInfo);
                        closedDocuments.Add(docHostInfo);
                    }
                }

                foreach (var docHostInfo in closedDocuments) {
                    sessionState_.DocumentHosts.Remove(docHostInfo);
                }

                sessionState_.RemoveLoadedDocuemnt(diffDocument_);

                // Reset the section panel.
                SectionPanel.DiffSummary = null;
                diffDocument_ = null;
                UpdateWindowTitle();
            }
        }

        private bool IsDiffModeDocument(IRDocumentHost document) {
            return sessionState_.DiffState.IsEnabled &&
                   (document == sessionState_.DiffState.LeftDocument ||
                    document == sessionState_.DiffState.RightDocument);
        }

        private async Task<bool> EnterDocumentDiffState() {
            if (sessionState_ == null) {
                // No session started yet.
                return false;
            }

            sessionState_.DiffState.StartModeChange();

            if (sessionState_.DiffState.IsEnabled) {
                sessionState_.DiffState.EndModeChange();
                return true;
            }

            if (!PickLeftRightDocuments(out var leftDocument, out var rightDocument)) {
                sessionState_.DiffState.EndModeChange();
                return false;
            }

            bool result = await EnterDocumentDiffState(leftDocument, rightDocument);
            sessionState_.DiffState.EndModeChange();
            return result;
        }

        private async Task<bool> EnterDocumentDiffState(IRDocumentHost leftDocument,
                                                        IRDocumentHost rightDocument) {
            //? TODO: Both these checks should be an assert
            if (sessionState_ == null) {
                // No session started yet.
                return false;
            }

            if (sessionState_.DiffState.IsEnabled) {
                return true;
            }

            sessionState_.DiffState.LeftDocument = leftDocument;
            sessionState_.DiffState.RightDocument = rightDocument;

            if (diffDocument_ != null) {
                // Used when diffing two different documents.
                sessionState_.DiffState.IsEnabled = true;
                await SwitchDiffedDocumentSection(leftDocument.Section, leftDocument, false);
            }
            else {
                sessionState_.DiffState.LeftSection = leftDocument.Section;
                sessionState_.DiffState.RightSection = rightDocument.Section;
                await DiffCurrentDocuments(sessionState_.DiffState);
            }

            // CreateDefaultSideBySidePanels();
            ShowDiffsControlsPanel();
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
            sessionState_.DiffState.StartModeChange();

            if (!sessionState_.DiffState.IsEnabled) {
                sessionState_.DiffState.EndModeChange();
                return;
            }

            var leftDocument = sessionState_.DiffState.LeftDocument;
            var rightDocument = sessionState_.DiffState.RightDocument;
            sessionState_.DiffState.End();

            if (!isSessionEnding) {
                // Reload sections in the same documents.
                Trace.TraceInformation("Diff mode: Reload original sections");
                var leftArgs = new OpenSectionEventArgs(leftDocument.Section, OpenSectionKind.ReplaceCurrent);

                var rightArgs =
                    new OpenSectionEventArgs(rightDocument.Section, OpenSectionKind.ReplaceCurrent);

                await SwitchDocumentSection(leftArgs, leftDocument, false);
                await leftDocument.ExitDiffMode();
                await SwitchDocumentSection(rightArgs, rightDocument, false);
                await rightDocument.ExitDiffMode();
            }

            if (disableControls) {
                HideDiffsControlsPanel();
            }

            sessionState_.DiffState.EndModeChange();
            Trace.TraceInformation("Diff mode: Exited");
        }

        private async void SectionPanel_EnterDiffMode(object sender, DiffModeEventArgs e) {
            if (sessionState_.DiffState.IsEnabled) {
                sessionState_.DiffState.IsEnabled = false;
            }

            sessionState_.DiffState.StartModeChange();
            var leftDocument = FindDocumentWithSection(e.Left.Section);
            var rightDocument = FindDocumentWithSection(e.Right.Section);

            Trace.TraceInformation($"Diff mode: Start with left doc. {ObjectTracker.Track(leftDocument)}, " +
                                   $"right doc. {ObjectTracker.Track(rightDocument)}");

            leftDocument = await SwitchDocumentSection(e.Left, leftDocument, false);
            rightDocument = await SwitchDocumentSection(e.Right, rightDocument, false);
            bool result = await EnterDocumentDiffState(leftDocument, rightDocument);

            UpdateDiffModeButton(result);
            sessionState_.DiffState.EndModeChange();
            Trace.TraceInformation("Diff mode: Entered");
        }

        private async Task DiffCurrentDocuments(DiffModeInfo diffState) {
            await EnableDocumentDiffState(diffState);
            var leftDocument = diffState.LeftDocument.TextView;
            var rightDocument = diffState.RightDocument.TextView;

            var leftText = await GetSectionTextAsync(leftDocument.Section);
            var rightText = await GetSectionTextAsync(rightDocument.Section);
            await DiffDocuments(leftDocument, rightDocument, leftText, rightText);
        }

        private async Task EnableDocumentDiffState(DiffModeInfo diffState) {
            await diffState.LeftDocument.EnterDiffMode();
            await diffState.RightDocument.EnterDiffMode();
            sessionState_.DiffState.IsEnabled = true;
        }

        private async Task DiffDocuments(IRDocument leftDocument, IRDocument rightDocument, string leftText,
                                         string rightText, IRTextSection newLeftSection = null,
                                         IRTextSection newRightSection = null) {
            var diffBuilder = new DocumentDiffBuilder(App.Settings.DiffSettings);
            var diff = await Task.Run(() => diffBuilder.ComputeDiffs(leftText, rightText));

            var leftDiffStats = new DiffStatistics();
            var rightDiffStats = new DiffStatistics();
            var diffFilter = compilerInfo_.CreateDiffOutputFilter();
            var leftDiffUpdater = new DocumentDiffUpdater(diffFilter, App.Settings.DiffSettings, compilerInfo_);
            var rightDiffUpdater = new DocumentDiffUpdater(diffFilter, App.Settings.DiffSettings, compilerInfo_);

            var leftMarkTask = leftDiffUpdater.MarkDiffs(leftText, diff.OldText, diff.NewText, leftDocument,
                                                         false, leftDiffStats);

            var rightMarkTask = rightDiffUpdater.MarkDiffs(leftText, diff.NewText, diff.OldText, rightDocument,
                                                           true, rightDiffStats);

            await Task.WhenAll(leftMarkTask, rightMarkTask);
            var leftDiffResult = await leftMarkTask;
            var rightDiffResult = await rightMarkTask;

            sessionState_.DiffState.LeftSection = newLeftSection ?? sessionState_.DiffState.LeftSection;
            sessionState_.DiffState.RightSection = newRightSection ?? sessionState_.DiffState.RightSection;
            sessionState_.DiffState.LeftDiffResults = leftDiffResult;
            sessionState_.DiffState.RightDiffResults = rightDiffResult;

            // The UI-thread dependent work.
            await UpdateDiffedFunction(leftDocument, leftDiffResult, sessionState_.DiffState.LeftSection);
            await UpdateDiffedFunction(rightDocument, rightDiffResult, sessionState_.DiffState.RightSection);

            // Scroll to the first diff.
            if (leftDiffResult.DiffSegments.Count > 0) {
                var firstDiff = leftDiffResult.DiffSegments[0];
                leftDocument.BringTextOffsetIntoView(firstDiff.StartOffset);
            }
            else if (rightDiffResult.DiffSegments.Count > 0) {
                var firstDiff = rightDiffResult.DiffSegments[0];
                rightDocument.BringTextOffsetIntoView(firstDiff.StartOffset);
            }

            UpdateDiffStatus(rightDiffStats);
        }

        private async Task SwitchDiffedDocumentSection(IRTextSection section, IRDocumentHost document,
                                                       bool redoDiffs = true) {
            string leftText = null;
            string rightText = null;
            IRTextSection newLeftSection = null;
            IRTextSection newRightSection = null;

            if (document == sessionState_.DiffState.LeftDocument) {
                var result = await Task.Run(() => LoadAndParseSection(section));
                leftText = result.Text;
                newLeftSection = section;

                (rightText, newRightSection) =
                    await SwitchOtherDiffedDocumentSide(section, sessionState_.DiffState.RightDocument.Section, diffDocument_);
            }
            else if (document == sessionState_.DiffState.RightDocument) {
                var result = await Task.Run(() => LoadAndParseSection(section));
                rightText = result.Text;
                newRightSection = section;

                (leftText, newLeftSection) =
                    await SwitchOtherDiffedDocumentSide(section, sessionState_.DiffState.LeftDocument.Section, mainDocument_);
            }
            else {
                // Document is not part of the diff set.
                return;
            }

            if (newLeftSection != null) {
                UpdateUIAfterSectionLoad(newLeftSection, sessionState_.DiffState.LeftDocument);
            }

            if (newRightSection != null) {
                UpdateUIAfterSectionLoad(newRightSection, sessionState_.DiffState.RightDocument);
            }

            await EnableDocumentDiffState(sessionState_.DiffState);
            await DiffDocuments(sessionState_.DiffState.LeftDocument.TextView,
                                sessionState_.DiffState.RightDocument.TextView, leftText, rightText,
                                newLeftSection, newRightSection);
        }

        private async Task<Tuple<string, IRTextSection>>
            SwitchOtherDiffedDocumentSide(IRTextSection section, IRTextSection otherSection,
                                          LoadedDocument otherDocument) {
            if (diffDocument_ != null) {
                // When two documents are compared, try to pick 
                // the other section from that other document.
                var diffSection = FindDiffDocumentSection(section, otherDocument);

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

        private IRTextSection FindDiffDocumentSection(IRTextSection section, LoadedDocument diffDoc) {
            SectionPanel.SelectFunction(section.ParentFunction);
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
            var position = MainGrid.PointToScreen(new Point(238, MainMenu.ActualHeight + 1));
            sharingPanelHost_ = new OptionsPanelHostWindow(new DiffOptionsPanel(), position, width, height, this);
            sharingPanelHost_.PanelClosed += DiffOptionsPanel_PanelClosed;
            sharingPanelHost_.PanelReset += DiffOptionsPanel_PanelReset;
            sharingPanelHost_.SettingsChanged += DiffOptionsPanel_SettingsChanged;
            sharingPanelHost_.Settings = (DiffSettings)App.Settings.DiffSettings.Clone();
            sharingPanelHost_.IsOpen = true;
            diffOptionsVisible_ = true;
        }

        private async Task CloseDiffOptionsPanel() {
            if (!diffOptionsVisible_) {
                return;
            }

            sharingPanelHost_.IsOpen = false;
            sharingPanelHost_.PanelClosed -= DiffOptionsPanel_PanelClosed;
            sharingPanelHost_.PanelReset -= DiffOptionsPanel_PanelReset;
            sharingPanelHost_.SettingsChanged -= DiffOptionsPanel_SettingsChanged;

            var newSettings = (DiffSettings)sharingPanelHost_.Settings;
            await HandleNewDiffSettings(newSettings, true);

            sharingPanelHost_ = null;
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
            sharingPanelHost_.Settings = null;
            sharingPanelHost_.Settings = newSettings;
            await HandleNewDiffSettings(newSettings, true);
        }

        private async void DiffOptionsPanel_SettingsChanged(object sender, EventArgs e) {
            var newSettings = (DiffSettings)sharingPanelHost_.Settings;

            if (newSettings != null) {
                await HandleNewDiffSettings(newSettings, false);
                sharingPanelHost_.Settings = null;
                sharingPanelHost_.Settings = newSettings.Clone();
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
            var diffUpdater = new DocumentDiffUpdater(diffFilter, App.Settings.DiffSettings, compilerInfo_);
            var diffResult = await diffUpdater.MarkDiffs(prevText, diff.NewText, diff.OldText, doc.TextView, true, diffStats);
            await UpdateDiffedFunction(doc.TextView, diffResult, section);
            DiffStatusText.Text = diffStats.ToString();
        }

        private void PreviousSegmentDiffButton_Click(object sender, RoutedEventArgs e) {
            if (!sessionState_.DiffState.IsEnabled) {
                return;
            }

            //? TODO: Diff segments from left/right must be combined and sorted by offset
            //? TODO: This is almost identical to the next case
            var diffResults = sessionState_.DiffState.RightDiffResults;
            var document = sessionState_.DiffState.RightDocument;

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
            if (!sessionState_.DiffState.IsEnabled) {
                return;
            }

            // TODO: Diff segments from left/right must be combined and sorted by offset
            // TODO: When next/prev segment is needed, start from the current carret offset
            var diffResults = sessionState_.DiffState.RightDiffResults;
            var document = sessionState_.DiffState.RightDocument;

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

            var leftDocHost = sessionState_.DiffState.LeftDocument;
            var rightDocHost = sessionState_.DiffState.RightDocument;
            var leftSection = leftDocHost.Section;
            var rightSection = rightDocHost.Section;

            DiffSwapButton.IsEnabled = false;
            await ExitDocumentDiffState(isSessionEnding: false, disableControls: false);

            // Swap the left/right documents, then re-enter diff state.
            await SwitchDocumentSection(new OpenSectionEventArgs(rightSection, OpenSectionKind.ReplaceCurrent), leftDocHost);
            await SwitchDocumentSection(new OpenSectionEventArgs(leftSection, OpenSectionKind.ReplaceCurrent), rightDocHost);
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
            if (!sessionState_.DiffState.IsEnabled) {
                return;
            }

            var leftSection = sessionState_.DiffState.LeftSection;
            var rightSection = sessionState_.DiffState.RightSection;

            int leftIndex = leftSection.Number - 1;
            int rightIndex = rightSection.Number - 1;

            if (leftIndex > 0 && rightIndex > 0) {
                var prevLeftSection = leftSection.ParentFunction.Sections[leftIndex - 1];
                var leftArgs = new OpenSectionEventArgs(prevLeftSection, OpenSectionKind.ReplaceCurrent);
                await SwitchDocumentSection(leftArgs, sessionState_.DiffState.LeftDocument);

                var prevRightSection = rightSection.ParentFunction.Sections[rightIndex - 1];
                var rightArgs = new OpenSectionEventArgs(prevRightSection, OpenSectionKind.ReplaceCurrent);
                await SwitchDocumentSection(rightArgs, sessionState_.DiffState.RightDocument);
            }
        }

        private async void NextDiffButton_Click(object sender, RoutedEventArgs e) {
            if (!sessionState_.DiffState.IsEnabled) {
                return;
            }

            var leftSection = sessionState_.DiffState.LeftSection;
            var rightSection = sessionState_.DiffState.RightSection;

            int leftIndex = leftSection.Number - 1;
            int rightIndex = rightSection.Number - 1;

            if (leftIndex < leftSection.ParentFunction.SectionCount - 1 &&
                rightIndex < rightSection.ParentFunction.SectionCount - 1) {
                var prevLeftSection = leftSection.ParentFunction.Sections[leftIndex + 1];
                var leftArgs = new OpenSectionEventArgs(prevLeftSection, OpenSectionKind.ReplaceCurrent);
                await SwitchDocumentSection(leftArgs, sessionState_.DiffState.LeftDocument);

                var prevRightSection = rightSection.ParentFunction.Sections[rightIndex + 1];
                var rightArgs = new OpenSectionEventArgs(prevRightSection, OpenSectionKind.ReplaceCurrent);
                await SwitchDocumentSection(rightArgs, sessionState_.DiffState.RightDocument);
            }
        }

        private void JumpToDiffSegmentAtIndex(int index, DiffMarkingResult diffResults, IRDocumentHost document) {
            var nextDiff = diffResults.DiffSegments[index];
            document.TextView.BringTextOffsetIntoView(nextDiff.StartOffset);
            document.TextView.SetCaretAtOffset(nextDiff.StartOffset);
            diffResults.CurrentSegmentIndex = index;
        }

        private void CloseDiffDocumentCanExecute(object sender, CanExecuteRoutedEventArgs e) {
            e.CanExecute = diffDocument_ != null;
            e.Handled = true;
        }


        public async Task ReloadDiffSettings(DiffSettings newSettings, bool hasHandlingChanges) {
            if (!IsInDiffMode) {
                return;
            }

            if (hasHandlingChanges) {
                // Diffs must be recomputed.
                var leftDocument = sessionState_.DiffState.LeftDocument;
                var rightDocument = sessionState_.DiffState.RightDocument;

                await ExitDocumentDiffState();
                await EnterDocumentDiffState(leftDocument, rightDocument);
            }
            else {
                // Only diff style must be updated.
                sessionState_.DiffState.LeftDocument.ReloadSettings();
                sessionState_.DiffState.RightDocument.ReloadSettings();
            }
        }

    }
}
