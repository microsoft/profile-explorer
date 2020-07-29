// Copyright(c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using IRExplorer.DebugServer;
using IRExplorer.Diff;
using IRExplorer.Document;
using IRExplorer.OptionsPanels;
using IRExplorerCore;
using IRExplorerCore.Analysis;
using IRExplorerCore.Graph;
using IRExplorerCore.GraphViz;
using IRExplorerCore.IR;
using IRExplorerCore.IR.Tags;
using Microsoft.Win32;

namespace IRExplorer {
    public partial class MainWindow : Window, ISessionManager {
        public ICompilerInfoProvider CompilerInfo => compilerInfo_;
        public IRDocument CurrentDocument => FindActiveDocumentHost()?.TextView;

        public IRTextSection CurrentDocumentSection {
            get {
                var activeDocument = FindActiveDocumentHost();
                return activeDocument?.Section;
            }
        }

        public List<IRDocument> OpenDocuments {
            get {
                var list = new List<IRDocument>();
                sessionState_.DocumentHosts.ForEach(doc => list.Add(doc.DocumentHost.TextView));
                return list;
            }
        }

        private async Task OpenDocument(string filePath) {
            bool loaded;

            if (Path.HasExtension(filePath) && Path.GetExtension(filePath) == ".irx") {
                loaded = await OpenSessionDocument(filePath);
            }
            else {
                loaded = await OpenIRDocument(filePath);
            }

            if (!loaded) {
                using var centerForm = new DialogCenteringHelper(this);
                MessageBox.Show($"Failed to load file {filePath}", "IR Explorer", MessageBoxButton.OK,
                                MessageBoxImage.Exclamation);
            }
        }

        private async void OpenDocumentExecuted(object sender, ExecutedRoutedEventArgs e) {
            await OpenDocument();
        }

        private async Task OpenDocument() {
            string filePath = ShowOpenFileDialog();

            if (filePath != null) {
                await OpenDocument(filePath);
            }
        }

        private async Task<bool> OpenSessionDocument(string filePath) {
            try {
                EndSession();
                UpdateUIBeforeReadSession(filePath);
                var data = await File.ReadAllBytesAsync(filePath);
                var state = await SessionStateManager.DeserializeSession(data);
                bool loaded = false;

                if (state != null) {
                    loaded = await LoadSessionDocument(state);
                }

                UpdateUIAfterLoadDocument();
                return loaded;
            }
            catch (IOException ioEx) {
                Trace.TraceError($"Failed to save session, IO exception: {ioEx}");
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed to save session, exception: {ex}");
            }

            UpdateUIAfterLoadDocument();
            return false;
        }

        private void InitializeFromLoadedSession(SessionState state) {
            sessionState_.Info.Notes = state.Info.Notes;
            int index = 0;

            foreach (var docState in state.Documents) {
                var summary = sessionState_.Documents[index].Summary;
                index++;

                // Reload state of document annotations.
                foreach (var sectionState in docState.SectionStates) {
                    var section = summary.GetSectionWithId(sectionState.Item1);
                    sessionState_.SaveDocumentState(sectionState.Item2, section);
                }

                // Reload state of panels.
                foreach (var panelState in docState.PanelStates) {
                    var section = summary.GetSectionWithId(panelState.Item1);
                    var panelInfo = FindActivePanel(panelState.Item2.PanelKind);
                    sessionState_.SavePanelState(panelState.Item2.StateObject, panelInfo.Panel, section);
                }
            }

            foreach (var panelState in state.GlobalPanelStates) {
                var panelInfo = FindActivePanel(panelState.PanelKind);
                sessionState_.SavePanelState(panelState.StateObject, panelInfo.Panel, null);
            }

            SetupSectionPanel();
            NotifyPanelsOfSessionStart();

            //? TODO: Reload sections left open.
            ///foreach (var sectionId in state.OpenSections) {
            ///    var section = DocumentSummary.GetSectionWithId(sectionId);
            ///    var args = new OpenSectionEventArgs(section, OpenSectionKind.NewTabDockRight);
            ///    await SwitchDocumentSection(args);
            ///    SectionPanel.SelectSection(section);
            ///}

            StartAutoSaveTimer();
        }

        private async Task<bool> SaveSessionDocument(string filePath) {
            try {
                NotifyPanelsOfSessionSave();
                NotifyDocumentsOfSessionSave();
                var data = await sessionState_.SerializeSession(mainDocument_.Loader).ConfigureAwait(false);

                if (data != null) {
                    await File.WriteAllBytesAsync(filePath, data).ConfigureAwait(false);
                    return true;
                }
            }
            catch (IOException ioEx) {
                Trace.TraceError($"Failed to save session, IO exception: {ioEx}");
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed to save session, exception: {ex}");
            }

            return false;
        }

        private bool RequestSessionFilePath(bool forceNewFile = false) {
            if (!forceNewFile && sessionState_.Info.IsFileSession) {
                return true; // Save over same session file.
            }

            var fileDialog = new SaveFileDialog {
                DefaultExt = "*.irx",
                Filter = "IR Explorer Session File|*.irx"
            };

            var result = fileDialog.ShowDialog();

            if (result.HasValue && result.Value) {
                sessionState_.Info.FilePath = fileDialog.FileName;
                sessionState_.Info.Kind = SessionKind.FileSession;
                UpdateWindowTitle();
                return true;
            }

            return false;
        }

        private void StartSession(string filePath, SessionKind sessionKind) {
            sessionState_ = new SessionStateManager(filePath, sessionKind);
            sessionState_.DocumentChanged += DocumentState_DocumentChangedEvent;
            sessionState_.ChangeDocumentWatcherState(App.Settings.AutoReloadDocument);
            ClearGraphLayoutCache();

            if (sessionKind != SessionKind.DebugSession) {
                AddRecentFile(filePath);
            }

            DiffModeButton.IsEnabled = true;
            HideStartPage();
        }

        private async void EndSession(bool showStartPage = false) {
            if (sessionState_ == null) {
                return; // Session not opened.
            }

            if (autoSaveTimer_ != null) {
                autoSaveTimer_.Stop();
                autoSaveTimer_ = null;
            }

            // Close all documents and notify all panels.
            NotifyPanelsOfSessionEnd();

            foreach (var docHostInfo in sessionState_.DocumentHosts) {
                CloseDocument(docHostInfo);
            }

            await ExitDocumentDiffState(isSessionEnding: true, disableControls: true);
            sessionState_.DocumentChanged -= DocumentState_DocumentChangedEvent;
            sessionState_.EndSession();
            sessionState_ = null;
            mainDocument_ = null;
            diffDocument_ = null;

            FunctionAnalysisCache.ResetCache();
            DiffModeButton.IsEnabled = false;

            if (showStartPage) {
                ShowStartPage();
            }
        }

        private void CloseDocument(DocumentHostInfo docHostInfo) {
            ResetDocumentEvents(docHostInfo.DocumentHost);
            docHostInfo.HostParent.Children.Remove(docHostInfo.Host);
        }

        private async Task<bool> OpenIRDocument(string filePath) {
            try {
                EndSession();
                UpdateUIBeforeLoadDocument(filePath);
                var result = await Task.Run(() => LoadDocument(filePath, UpdateIRDocumentLoadProgress));

                if (result != null) {
                    SetupOpenedIRDocument(SessionKind.Default, filePath, result);
                    return true;
                }
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed to load document: {ex}");
            }

            UpdateUIAfterLoadDocument();
            return false;
        }

        private void UpdateIRDocumentLoadProgress(IRSectionReader reader, SectionReaderProgressInfo info) {
            if (info.TotalBytes == 0) {
                return;
            }

            Dispatcher.BeginInvoke(() => {
                if (!loadingDocuments_) {
                    // It can happen that this code on the dispatchers runs after
                    // the document has already been loaded, so just ignore the events.
                    return;
                }

                bool firstTimeShowPanel = false;

                if (!documentLoadProgressVisible_) {
                    // Progress panel not displayed yet, show it only after a bit of time
                    // passes since most files are small and load instantly.
                    var timeDiff = DateTime.UtcNow - documentLoadStartTime_;

                    if (timeDiff.TotalMilliseconds < 500) {
                        return;
                    }

                    firstTimeShowPanel = true;
                }

                double percentage = Math.Ceiling(100 * ((double)info.BytesProcessed / (double)info.TotalBytes));

                // If the progress panel is about to be displayed, but most of the file
                // as been processed already, there's no point in showing it anymore.
                if (firstTimeShowPanel) {
                    if (percentage < 50) {
                        return;
                    }

                    ShowProgressBar();
                }

                DocumentLoadProgressBar.Value = percentage;
            });
        }

        private void SetupOpenedIRDocument(SessionKind sessionKind, string filePath, LoadedDocument result) {
            mainDocument_ = result;
            StartSession(filePath, sessionKind);
            sessionState_.NewLoadedDocument(result);

            UpdateUIAfterLoadDocument();
            SetupSectionPanel();
            NotifyPanelsOfSessionStart();
            StartAutoSaveTimer();
        }

        private async Task<bool> OpenDiffIRDocument(string filePath) {
            try {
                var result = await Task.Run(() => LoadDocument(filePath, UpdateIRDocumentLoadProgress));

                if (result != null) {
                    SetupOpenedDiffIRDocument(filePath, result);
                    return true;
                }
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed to load diff document: {ex}");
            }

            UpdateUIAfterLoadDocument();
            return false;
        }

        private void SetupOpenedDiffIRDocument(string diffFilePath, LoadedDocument result) {
            diffDocument_ = result;
            sessionState_.NewLoadedDocument(result);
            UpdateUIAfterLoadDocument();
            ShowSectionPanelDiffs(result);
        }

        private LoadedDocument LoadDocument(string path, ProgressInfoHandler progressHandler) {
            try {
                var result = new LoadedDocument(path);
                result.Loader = new DocumentSectionLoader(path, compilerInfo_.IR);
                result.Summary = result.Loader.LoadDocument(progressHandler);
                return result;
            }
            catch (Exception) {
                Trace.TraceError("$Failed to load document {path}");
                return null;
            }
        }

        private LoadedDocument LoadDocument(byte[] data, string path, ProgressInfoHandler progressHandler) {
            try {
                var result = new LoadedDocument(path);
                result.Loader = new DocumentSectionLoader(data, compilerInfo_.IR);
                result.Summary = result.Loader.LoadDocument(progressHandler);
                return result;
            }
            catch (Exception) {
                Trace.TraceError("$Failed to load in-memory document");
                return null;
            }
        }

        private async Task<bool> LoadSessionDocument(SessionState state) {
            try {
                //? TODO: Proper support for multiple docs
                StartSession(state.Info.FilePath, SessionKind.FileSession);
                bool failed = false;

                foreach (var docState in state.Documents) {
                    var result = await Task.Run(() => LoadDocument(docState.DocumentText, docState.FilePath, UpdateIRDocumentLoadProgress));

                    if (result != null) {
                        sessionState_.NewLoadedDocument(result);

                        if (mainDocument_ == null) {
                            mainDocument_ = result;
                        }
                        else {
                            Debug.Assert(diffDocument_ == null);
                            diffDocument_ = result;
                        }
                    }
                    else {
                        failed = true;
                    }
                }

                UpdateUIAfterLoadDocument();
                InitializeFromLoadedSession(state);
                return !failed;
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed to load in-memory document: {ex}");
            }

            UpdateUIAfterLoadDocument();
            return false;
        }

        private async void SectionPanel_OpenSection(object sender, OpenSectionEventArgs args) {
            await SwitchDocumentSection(args, args.TargetDocument);
        }

        private async Task<IRDocumentHost> SwitchDocumentSection(OpenSectionEventArgs args,
                                                                 IRDocumentHost targetDocument = null,
                                                                 bool awaitExtraTasks = true) {
            var document = targetDocument;

            if (document == null &&
                (args.OpenKind == OpenSectionKind.ReplaceCurrent ||
                 args.OpenKind == OpenSectionKind.ReplaceLeft ||
                 args.OpenKind == OpenSectionKind.ReplaceRight)) {
                document = FindSameSummaryDocumentHost(args.Section);

                if (document == null && args.OpenKind == OpenSectionKind.ReplaceCurrent) {
                    document = FindActiveDocumentHost();
                }
            }

            if (document == null ||
                (targetDocument == null &&
                 (args.OpenKind == OpenSectionKind.NewTab ||
                  args.OpenKind == OpenSectionKind.NewTabDockLeft ||
                  args.OpenKind == OpenSectionKind.NewTabDockRight))) {
                document = AddNewDocument(args.OpenKind).DocumentHost;
            }

            // In diff mode, reload both left/right sections and redo the diffs.
            if (sessionState_.DiffState.IsEnabled && sessionState_.DiffState.IsDiffDocument(document)) {
                await SwitchDiffedDocumentSection(args.Section, document);
                return document;
            }

            var parsedSection = await SwitchSection(args.Section, document);

            if (awaitExtraTasks) {
                await GenerateGraphs(args.Section, document.TextView);
            }
            else {
                // Used in diff mode to have the UI update faster.
                GenerateGraphs(args.Section, document.TextView, false);
            }

            return document;
        }

        private async Task<ParsedSection> SwitchSection(IRTextSection section, IRDocumentHost document) {
            Trace.TraceInformation(
                $"Document {ObjectTracker.Track(document)}: Switch to section ({section.Number}) {section.Name}");

            NotifyOfSectionUnload(document, true);
            ResetDocumentEvents(document);
            ResetStatusBar();
            var delayedAction = UpdateUIBeforeSectionLoad(section, document);
            var result = await Task.Run(() => LoadAndParseSection(section));

            if (result.Function == null) {
                //? TODO: Handle load function failure better
                Trace.TraceError($"Document {ObjectTracker.Track(document)}: Failed to parse function");
                OptionalStatusText.Text = "Failed to parser section IR";
                OptionalStatusText.ToolTip = FormatParsingErrors(result, "Section IR parsing errors");
            }
            else if (result.HadParsingErrors) {
                Trace.TraceWarning($"Document {ObjectTracker.Track(document)}: Parsed function with errors");
                OptionalStatusText.Text = "IR parsing issues";
                OptionalStatusText.ToolTip = FormatParsingErrors(result, "Section IR parsing errors");
            }
            else {
                OptionalStatusText.Text = "";
            }

            document.LoadSectionMinimal(result);
            NotifyPanelsOfSectionLoad(section, document, true);
            SetupDocumentEvents(document);
            await document.LoadSection(result);

            UpdateUIAfterSectionLoad(section, document, delayedAction);
            return result;
        }

        private string FormatParsingErrors(ParsedSection result, string message) {
            if (!result.HadParsingErrors) {
                return "";
            }

            var builder = new StringBuilder();
            builder.AppendLine($"{message}: {result.ParsingErrors.Count}");

            foreach (var error in result.ParsingErrors) {
                builder.AppendLine(error.ToString());
            }

            return builder.ToString();
        }

        private async Task GenerateGraphs(IRTextSection section, IRDocument document,
                                          bool awaitTasks = true) {
            var tasks = new[] {
                GenerateGraphs(GraphKind.FlowGraph, section, document),
                GenerateGraphs(GraphKind.DominatorTree, section, document),
                GenerateGraphs(GraphKind.PostDominatorTree, section, document)
            };

            if (awaitTasks) {
                await Task.WhenAll(tasks);
            }
        }

        private async Task GenerateGraphs(GraphKind graphKind, IRTextSection section, IRDocument document) {
            var panelKind = graphKind switch
            {
                GraphKind.FlowGraph => ToolPanelKind.FlowGraph,
                GraphKind.DominatorTree => ToolPanelKind.DominatorTree,
                GraphKind.PostDominatorTree => ToolPanelKind.PostDominatorTree,
                _ => throw new InvalidOperationException("Unexpected graph kind!")
            };

            var action = GetComputeGraphAction(graphKind);
            var flowGraphPanels = FindTargetPanels(document, panelKind);

            foreach (var panelInfo in flowGraphPanels) {
                await SwitchGraphsAsync((GraphPanel)panelInfo.Panel, section, document, action);
            }
        }

        private Func<FunctionIR, IRTextSection, CancelableTaskInfo, LayoutGraph> GetComputeGraphAction(
            GraphKind graphKind) {
            return graphKind switch
            {
                GraphKind.FlowGraph => ComputeFlowGraph,
                GraphKind.DominatorTree => ComputeDominatorTree,
                GraphKind.PostDominatorTree => ComputePostDominatorTree,
                _ => throw new InvalidOperationException("Unexpected graph kind!")
            };
        }

        private Func<FunctionIR, IRTextSection, CancelableTaskInfo, LayoutGraph> GetComputeGraphAction(
            ToolPanelKind graphKind) {
            return graphKind switch
            {
                ToolPanelKind.FlowGraph => ComputeFlowGraph,
                ToolPanelKind.DominatorTree => ComputeDominatorTree,
                ToolPanelKind.PostDominatorTree => ComputePostDominatorTree,
                _ => throw new InvalidOperationException("Unexpected graph kind!")
            };
        }

        public async Task SwitchGraphsAsync(GraphPanel graphPanel, IRTextSection section, IRDocument document,
                                            Func<FunctionIR, IRTextSection, CancelableTaskInfo, LayoutGraph> computeGraphAction) {
            //? TODO: When the section is changed quickly and there are long-running tasks,
            //? the CFG panel can get out of sync - the doc. tries to highlight a block
            //? for another CFG if the loading of the prev. CFG completes between loading the text
            //? and the caret reset event.
            var loadTask = graphPanel.OnGenerateGraphStart(section);

            var functionGraph =
                await Task.Run(() => computeGraphAction(document.Function, section, loadTask));

            if (functionGraph != null) {
                graphPanel.DisplayGraph(functionGraph);
                graphPanel.OnGenerateGraphDone(loadTask);
            }
            else {
                //? TODO: Handle CFG failure
                graphPanel.OnGenerateGraphDone(loadTask, true);
                Trace.TraceError($"Document {ObjectTracker.Track(document)}: Failed to load CFG");
            }
        }

        private void GraphViewer_GraphNodeSelected(object sender, IRElementEventArgs e) {
            var panel = ((GraphViewer)sender).HostPanel;
            var document = FindTargetDocument(panel);
            document.TextView.HighlightElement(e.Element, HighlighingType.Hovered);
        }

        private ParsedSection LoadAndParseSection(IRTextSection section) {
            var docInfo = sessionState_.FindLoadedDocument(section);
            var parsedSection = docInfo.Loader.LoadSection(section);

            if (parsedSection.Function != null) {
                compilerInfo_.AnalyzeLoadedFunction(parsedSection.Function);
                addressTag_ = parsedSection.Function.GetTag<AddressMetadataTag>();
            }

            return parsedSection;
        }

        private async void ReloadDocumentSection(IRDocumentHost document) {
            if (document.Section != null) {
                await SwitchSection(document.Section, document);
            }
        }

        private LayoutGraph ComputeFlowGraph(FunctionIR function, IRTextSection section,
                                                         CancelableTaskInfo loadTask) {
            var graphLayout = GetGraphLayoutCache(GraphKind.FlowGraph);
            return graphLayout.GenerateGraph(function, section, loadTask, (object)null);
        }

        private LayoutGraph ComputeDominatorTree(FunctionIR function, IRTextSection section,
                                                             CancelableTaskInfo loadTask) {
            var graphLayout = GetGraphLayoutCache(GraphKind.DominatorTree);
            return graphLayout.GenerateGraph(function, section, loadTask, (object)null);
        }

        private LayoutGraph ComputePostDominatorTree(FunctionIR function, IRTextSection section,
                                                                 CancelableTaskInfo loadTask) {
            var graphLayout = GetGraphLayoutCache(GraphKind.PostDominatorTree);
            return graphLayout.GenerateGraph(function, section, loadTask, (object)null);
        }

        private void GraphViewer_BlockUnmarked(object sender, IRElementMarkedEventArgs e) {
            var panel = ((GraphViewer)sender).HostPanel;
            var document = FindTargetDocument(panel);
            document.TextView.UnmarkBlock(e.Element, HighlighingType.Marked);
        }

        private void GraphViewer_BlockMarked(object sender, IRElementMarkedEventArgs e) {
            var panel = ((GraphViewer)sender).HostPanel;
            var document = FindTargetDocument(panel);
            document.TextView.MarkBlock(e.Element, e.Style);
        }

        private void GraphViewer_GraphLoaded(object sender, EventArgs e) {
            var panel = ((GraphViewer)sender).HostPanel;
            var document = FindTargetDocument(panel);
            document.TextView.PanelContentLoaded(panel);
        }

        private void SetupGraphLayoutCache() {
            graphLayout_ = new Dictionary<GraphKind, GraphLayoutCache>();
            graphLayout_.Add(GraphKind.FlowGraph, new GraphLayoutCache(GraphKind.FlowGraph));
            graphLayout_.Add(GraphKind.DominatorTree, new GraphLayoutCache(GraphKind.DominatorTree));
            graphLayout_.Add(GraphKind.PostDominatorTree, new GraphLayoutCache(GraphKind.PostDominatorTree));
            graphLayout_.Add(GraphKind.ExpressionGraph, new GraphLayoutCache(GraphKind.ExpressionGraph));
        }

        private void ClearGraphLayoutCache() {
            foreach (var cache in graphLayout_.Values) {
                cache.ClearCache();
            }
        }

        private GraphLayoutCache GetGraphLayoutCache(GraphKind kind) {
            return graphLayout_[kind];
        }


        private DelayedAction UpdateUIBeforeSectionLoad(IRTextSection section, IRDocumentHost document) {
            document.Focusable = false;
            document.IsHitTestVisible = false;
            var delayedAction = new DelayedAction();
            delayedAction.Start(TimeSpan.FromMilliseconds(500), () => { document.Opacity = 0.5; });
            return delayedAction;
        }

        private void UpdateUIAfterSectionLoad(IRTextSection section, IRDocumentHost document,
                                              DelayedAction delayedAction = null) {
            delayedAction?.Cancel();
            document.Opacity = 1;
            document.Focusable = true;
            document.IsHitTestVisible = true;
            var docHostPair = FindDocumentHostPair(document);
            docHostPair.Host.Title = GetSectionName(section);
            docHostPair.Host.ToolTip = GetDocumentDescription(section);
            RenameAllPanels(); // For bound panels.
            SectionPanel.SelectSection(section, false);
        }

        private IRDocumentHost FindTargetDocument(IToolPanel panel) {
            if (panel.BoundDocument != null) {
                return FindDocumentHost(panel.BoundDocument);
            }

            return FindActiveDocumentHost();
        }


        private void SetupDocumentEvents(IRDocumentHost document) {
            document.TextView.ActionPerformed += TextView_ActionPerformed;
            document.TextView.ElementSelected += TextView_IRElementSelected;
            document.TextView.ElementHighlighting += TextView_IRElementHighlighting;
            document.TextView.BlockSelected += TextView_BlockSelected;
            document.TextView.BookmarkAdded += TextView_BookmarkAdded;
            document.TextView.BookmarkRemoved += TextView_BookmarkRemoved;
            document.TextView.BookmarkChanged += TextView_BookmarkChanged;
            document.TextView.BookmarkSelected += TextView_BookmarkSelected;
            document.TextView.BookmarkListCleared += TextView_BookmarkListCleared;
            document.ScrollChanged += Document_ScrollChanged;
        }

        private void ResetDocumentEvents(IRDocumentHost document) {
            document.TextView.ElementSelected -= TextView_IRElementSelected;
            document.TextView.ElementHighlighting -= TextView_IRElementHighlighting;
            document.TextView.BlockSelected -= TextView_BlockSelected;
            document.TextView.BookmarkAdded -= TextView_BookmarkAdded;
            document.TextView.BookmarkRemoved -= TextView_BookmarkRemoved;
            document.TextView.BookmarkChanged -= TextView_BookmarkChanged;
            document.TextView.BookmarkSelected -= TextView_BookmarkSelected;
            document.TextView.BookmarkListCleared -= TextView_BookmarkListCleared;
            document.ScrollChanged -= Document_ScrollChanged;
        }


        private void Document_ScrollChanged(object sender, ScrollChangedEventArgs e) {
            if (!sessionState_.DiffState.IsEnabled || Math.Abs(e.VerticalChange) < double.Epsilon) {
                return;
            }

            var document = sender as IRDocumentHost;

            if (sessionState_.DiffState.LeftDocument == document) {
                sessionState_.DiffState.RightDocument.TextView.ScrollToVerticalOffset(e.VerticalOffset);
            }
            else if (sessionState_.DiffState.RightDocument == document) {
                sessionState_.DiffState.LeftDocument.TextView.ScrollToVerticalOffset(e.VerticalOffset);
            }
        }

        private void TextView_ActionPerformed(object sender, DocumentAction e) {
            var document = sender as IRDocument;
            Debug.Assert(document != null);

            if (e.ActionKind == DocumentActionKind.ShowExpressionGraph) {
                var section = document.Section;
                var loadTask = ExpressionGraphPanel.OnGenerateGraphStart(section);
                var graphLayout = GetGraphLayoutCache(GraphKind.ExpressionGraph);
                var options = App.Settings.ExpressionGraphSettings.GetGraphPrinterOptions();
                options.IR = CompilerInfo.IR;
                var graph = graphLayout.GenerateGraph(e.Element, section, loadTask, options);

                if (graph != null) {
                    ExpressionGraphPanel.DisplayGraph(graph);
                    ExpressionGraphPanel.OnGenerateGraphDone(loadTask);
                    var panelHost = FindPanelHost(ExpressionGraphPanel).Host;

                    if (!panelHost.IsActive) {
                        panelHost.IsActive = true;
                    }
                }
                else {
                    ExpressionGraphPanel.OnGenerateGraphDone(loadTask, true);
                    Trace.TraceError($"Document {ObjectTracker.Track(document)}: Failed to load CFG");
                }

                return;
            }

            MirrorElementAction(e.Element, document,
                                (otherElement, otherDocument) => {
                                    otherDocument.ExecuteDocumentAction(e.WithNewElement(otherElement));
                                });
        }

        private void TextView_BlockSelected(object sender, IRElementEventArgs e) {
            var document = sender as IRDocument;

            if (document != null) {
                NotifyPanelsOfElementSelection(e, document);
            }

            var block = e.Element.ParentBlock;
            UpdateBlockStatusBar(block);
        }

        private void TextView_BookmarkSelected(object sender, SelectedBookmarkInfo e) {
            var document = sender as IRDocument;
            var bookmarksPanel = FindTargetPanel<BookmarksPanel>(document, ToolPanelKind.Bookmarks);
            bookmarksPanel.Bookmarks.Refresh();
            BookmarkStatus.Text = $"{e.SelectedIndex + 1} / {e.TotalBookmarks}";
        }

        private void TextView_BookmarkListCleared(object sender, EventArgs e) {
            var document = sender as IRDocument;
            var bookmarksPanel = FindTargetPanel<BookmarksPanel>(document, ToolPanelKind.Bookmarks);
            bookmarksPanel.Bookmarks.Clear();
        }

        private void TextView_BookmarkRemoved(object sender, Bookmark e) {
            var document = sender as IRDocument;
            var bookmarksPanel = FindTargetPanel<BookmarksPanel>(document, ToolPanelKind.Bookmarks);
            bookmarksPanel.Bookmarks.Remove(e);
        }

        private void TextView_BookmarkAdded(object sender, Bookmark e) {
            var document = sender as IRDocument;
            var bookmarksPanel = FindTargetPanel<BookmarksPanel>(document, ToolPanelKind.Bookmarks);
            bookmarksPanel.Bookmarks.Add(e);
        }

        private void TextView_BookmarkChanged(object sender, Bookmark e) {
            var document = sender as IRDocument;
            var bookmarksPanel = FindTargetPanel<BookmarksPanel>(document, ToolPanelKind.Bookmarks);
            bookmarksPanel.Bookmarks.Refresh();
        }

        private void TextView_IRElementHighlighting(object sender, IRHighlightingEventArgs e) {
            var document = sender as IRDocument;

            if (document != null) {
                NotifyPanelsOfElementHighlight(e, document);
            }
        }

        private void TextView_IRElementSelected(object sender, IRElementEventArgs e) {
            if (sender is IRDocument document) {
                NotifyPanelsOfElementSelection(e, document);
            }
        }

        private void MirrorElementAction(IRElement element, IRDocument sourceDocument,
                                         Action<IRElement, IRDocument> action) {
            Trace.TraceInformation(
                $"Mirror action from {ObjectTracker.Track(sourceDocument)} on element {element}");

            foreach (var docInfo in sessionState_.DocumentHosts) {
                var otherDocument = docInfo.DocumentHost.TextView;

                // Skip if same section or from another function.
                if (otherDocument == sourceDocument ||
                    otherDocument.Section.ParentFunction != sourceDocument.Section.ParentFunction) {
                    continue;
                }

                if (element != null) {
                    var finder = new ReferenceFinder(otherDocument.Function);
                    var otherOp = finder.FindEquivalentValue(element);

                    if (otherOp != null) {
                        action(otherOp, otherDocument);
                        continue;
                    }
                }

                action(null, otherDocument);
            }
        }

        private void UpdateUIBeforeReadSession(string documentTitle) {
            Title = $"IR Explorer - Reading {documentTitle}";
            Utils.DisableControl(DockManager, 0.85);
            Mouse.OverrideCursor = Cursors.Wait;
        }

        private void UpdateUIBeforeLoadDocument(string documentTitle) {
            Title = $"IR Explorer - Loading {documentTitle}";
            Utils.DisableControl(DockManager, 0.85);
            Mouse.OverrideCursor = Cursors.Wait;
            documentLoadStartTime_ = DateTime.UtcNow;
            loadingDocuments_ = true;
            lastDocumentLoadTime_ = DateTime.UtcNow;
        }

        private void UpdateUIAfterLoadDocument() {
            loadingDocuments_ = false;
            Mouse.OverrideCursor = null;

            if (sessionState_ != null) {
                UpdateWindowTitle();
            }
            else {
                Title = "IR Explorer - Failed to load file";
            }

            // Hide temporary UI.
            HideProgressBar();
        }


        private IRDocumentHost FindDocumentWithSection(IRTextSection section) {
            var result = sessionState_.DocumentHosts.Find(item => item.DocumentHost.Section == section);
            return result?.DocumentHost;
        }

        private async void DocumentState_DocumentChangedEvent(object sender, EventArgs e) {
            var loadedDoc = (LoadedDocument)sender;
            var eventTime = DateTime.UtcNow;

            if (appIsActivated_) {
                // The event doesn't run on the main thread, redirect.
                await Dispatcher.BeginInvoke(async () => {
                    if (eventTime < lastDocumentLoadTime_) {
                        return; // Event happened before the last document reload, ignore.
                    }

                    if (ShowDocumentReloadQuery(loadedDoc.FilePath)) {
                        await ReloadDocument(loadedDoc.FilePath);
                    }
                });
            }
            else {
                // Queue for later, when the application gets focus back.
                changedDocuments_[loadedDoc.FilePath] = eventTime;
            }
        }

        private bool ShowDocumentReloadQuery(string filePath) {
            using var centerForm = new DialogCenteringHelper(this);
            return MessageBox.Show(
                       $"File {filePath} changed by an external application?\nDo you want to reload?",
                       "IR Explorer", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No,
                       MessageBoxOptions.DefaultDesktopOnly) ==
                       MessageBoxResult.Yes;
        }

        private async Task ReloadDocument(string filePath) {
            if (diffDocument_ != null) {
                // If the file is one of the diff documents, reload in diff mode.
                if (filePath == mainDocument_.FilePath || filePath == diffDocument_.FilePath) {
                    await OpenBaseDiffIRDocumentsImpl(mainDocument_.FilePath, diffDocument_.FilePath);
                    return;
                }
            }

            bool loaded = await OpenIRDocument(filePath);

            if (!loaded) {
                using var centerForm = new DialogCenteringHelper(this);
                MessageBox.Show($"Failed to reload file {filePath}", "IR Explorer", MessageBoxButton.OK,
                                MessageBoxImage.Exclamation);
            }
        }

        private void StartAutoSaveTimer() {
            try {
                string filePath = Utils.GetAutoSaveFilePath();

                if (File.Exists(filePath)) {
                    File.Delete(filePath);
                }
            }
            catch (Exception) {
                Trace.TraceError("Failed to delete autosave file");
            }

            //? TODO: For huge files, autosaving uses a lot of memory.
            if (!sessionState_.Info.IsDebugSession) {
                try {
                    long fileSize = new FileInfo(sessionState_.Info.FilePath).Length;

                    if (fileSize > SectionReaderBase.MAX_PRELOADED_FILE_SIZE) {
                        Trace.TraceWarning(
                            $"Disabling auto-saving for large file: {sessionState_.Info.FilePath}");

                        sessionState_.IsAutoSaveEnabled = false;
                        return;
                    }
                }
                catch (Exception ex) {
                    Trace.TraceError($"Failed to get auto-saved file size: {ex}");
                }
            }

            sessionState_.IsAutoSaveEnabled = true;
            autoSaveTimer_ = new DispatcherTimer { Interval = TimeSpan.FromSeconds(300) };
            autoSaveTimer_.Tick += async delegate { await AutoSaveSession().ConfigureAwait(false); };
            autoSaveTimer_.Start();
        }

        private async Task AutoSaveSession() {
            if (sessionState_ == null || !sessionState_.IsAutoSaveEnabled) {
                return;
            }

            string filePath = Utils.GetAutoSaveFilePath();
            bool saved = await SaveSessionDocument(filePath).ConfigureAwait(false);
            Trace.TraceInformation($"Auto-saved session: {saved}");
        }

        private string ShowOpenFileDialog() {
            var fileDialog = new OpenFileDialog {
                DefaultExt = "*.*",
                Filter = "IR Files|*.txt;*.log;*.ir;*.irx|IR Explorer Session Files|*.irx|All Files|*.*"
            };

            var result = fileDialog.ShowDialog();

            if (result.HasValue && result.Value) {
                return fileDialog.FileName;
            }

            return null;
        }

        private void OpenNewDocumentExecuted(object sender, ExecutedRoutedEventArgs e) {
            string filePath = ShowOpenFileDialog();

            if (filePath != null) {
                if (!Utils.StartNewApplicationInstance(filePath)) {
                    using var centerForm = new DialogCenteringHelper(this);
                    MessageBox.Show("Failed to start new IR Explorer instance", "IR Explorer",
                                    MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void CloseDocumentExecuted(object sender, ExecutedRoutedEventArgs e) {
            EndSession(showStartPage: true);
        }

        private async void SaveDocumentExecuted(object sender, ExecutedRoutedEventArgs e) {
            if (!RequestSessionFilePath()) {
                return;
            }

            string filePath = sessionState_.Info.FilePath;
            bool loaded = await SaveSessionDocument(filePath);

            if (!loaded) {
                using var centerForm = new DialogCenteringHelper(this);
                MessageBox.Show($"Failed to save session file {filePath}", "IR Explorer",
                                MessageBoxButton.OK, MessageBoxImage.Exclamation);
            }
        }

        private async void SaveAsDocumentExecuted(object sender, ExecutedRoutedEventArgs e) {
            if (!RequestSessionFilePath(true)) {
                return;
            }

            string filePath = sessionState_.Info.FilePath;
            bool loaded = await SaveSessionDocument(filePath);

            if (!loaded) {
                using var centerForm = new DialogCenteringHelper(this);
                MessageBox.Show($"Failed to save session file {filePath}", "IR Explorer",
                                MessageBoxButton.OK, MessageBoxImage.Exclamation);
            }
        }

        private async void ReloadDocumentExecuted(object sender, ExecutedRoutedEventArgs e) {
            if (sessionState_ != null && !sessionState_.Info.IsFileSession && !sessionState_.Info.IsDebugSession) {
                await ReloadDocument(sessionState_.Info.FilePath);
            }
        }

        private void AutoReloadDocumentExecuted(object sender, ExecutedRoutedEventArgs e) {
            if (sessionState_ != null) {
                App.Settings.AutoReloadDocument = (e.OriginalSource as MenuItem).IsChecked;
                sessionState_.ChangeDocumentWatcherState(App.Settings.AutoReloadDocument);
            }
        }

        private void CanExecuteDocumentCommand(object sender, CanExecuteRoutedEventArgs e) {
            e.CanExecute = sessionState_ != null;
            e.Handled = true;
        }


        public void ReloadDocumentSettings(DocumentSettings newSettings, IRDocument document) {
            foreach (var docHostInfo in sessionState_.DocumentHosts) {
                if (docHostInfo.DocumentHost.TextView != document) {
                    docHostInfo.DocumentHost.Settings = newSettings;
                }
            }
        }

        public void ReloadRemarkSettings(RemarkSettings newSettings, IRDocument document) {
            foreach (var docHostInfo in sessionState_.DocumentHosts) {
                if (docHostInfo.DocumentHost.TextView != document) {
                    docHostInfo.DocumentHost.RemarkSettings = newSettings;
                }
            }
        }

        public Task<string> GetSectionTextAsync(IRTextSection section, IRDocument targetDiffDocument = null) {
            if (sessionState_.DiffState.IsEnabled && targetDiffDocument != null) {
                IRDocument diffDocument = null;

                if (targetDiffDocument == sessionState_.DiffState.LeftDocument.TextView) {
                    diffDocument = sessionState_.DiffState.LeftDocument.TextView;
                }
                else if (targetDiffDocument == sessionState_.DiffState.RightDocument.TextView) {
                    diffDocument = sessionState_.DiffState.RightDocument.TextView;
                }

                if (diffDocument != null) {
                    return Task.FromResult(diffDocument.Text);
                }
            }

            var docInfo = sessionState_.FindLoadedDocument(section);
            return Task.Run(() => docInfo.Loader.GetSectionText(section));
        }

        public Task<string> GetRawSectionTextAsync(IRTextSection section, IRDocument targetDiffDocument = null) {
            var docInfo = sessionState_.FindLoadedDocument(section);
            return Task.Run(() => docInfo.Loader.GetSectionText(section));
        }

        public Task<string> GetDocumentTextAsync(IRTextSection section) {
            var docInfo = sessionState_.FindLoadedDocument(section);
            return Task.Run(() => docInfo.Loader.GetDocumentText());
        }

        public async Task<SectionSearchResult> SearchSectionAsync(
            SearchInfo searchInfo, IRTextSection section, IRDocument document) {
            var docInfo = sessionState_.FindLoadedDocument(section);
            var searcher = new SectionTextSearcher(docInfo.Loader);

            if (searchInfo.SearchAll) {
                if (sessionState_.DiffState.IsEnabled) {
                    return new SectionSearchResult(section);
                }

                var sections = section.ParentFunction.Sections;
                var result = await searcher.SearchAsync(searchInfo.SearchedText, searchInfo.SearchKind, sections);

                var panelInfo = FindTargetPanel(document, ToolPanelKind.SearchResults);
                var searchPanel = panelInfo.Panel as SearchResultsPanel;
                searchPanel.UpdateSearchResults(result, searchInfo);

                if (result.Count > 0) {
                    panelInfo.Host.IsSelected = true;
                }

                return result.Find(item => item.Section == section);
            }

            // In diff mode, use the diff text being displayed, which may be different
            // than the original section text due to diff annotations.
            if (document.DiffModeEnabled) {
                return await searcher.SearchSectionWithTextAsync(document.Text, searchInfo.SearchedText,
                                                                 searchInfo.SearchKind, section);
            }

            return await searcher.SearchSectionAsync(searchInfo.SearchedText, searchInfo.SearchKind, section);
        }

        public bool SwitchToPreviousSection(IRTextSection section, IRDocument document) {
            int index = section.Number - 1;

            if (index > 0) {
                var prevSection = section.ParentFunction.Sections[index - 1];
                var docHost = FindDocumentHost(document);
                SectionPanel.SwitchToSection(prevSection, docHost);
                return true;
            }

            return false;
        }

        public bool SwitchToNextSection(IRTextSection section, IRDocument document) {
            int index = section.Number - 1;

            if (index < section.ParentFunction.SectionCount - 1) {
                var nextSection = section.ParentFunction.Sections[index + 1];
                var docHost = FindDocumentHost(document);
                SectionPanel.SwitchToSection(nextSection, docHost);
                return true;
            }

            return false;
        }

        public void SaveDocumentState(object stateObject, IRTextSection section) {
            if (sessionState_.DiffState.IsEnabled) {
                if (section == sessionState_.DiffState.LeftSection ||
                    section == sessionState_.DiffState.RightSection) {
                    return;
                }
            }

            sessionState_.SaveDocumentState(stateObject, section);
        }

        public object LoadDocumentState(IRTextSection section) {
            if (sessionState_.DiffState.IsEnabled) {
                if (section == sessionState_.DiffState.LeftSection ||
                    section == sessionState_.DiffState.RightSection) {
                    return null;
                }
            }

            return sessionState_.LoadDocumentState(section);
        }
    }
}
