// Copyright(c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AvalonDock.Layout;
using IRExplorerUI.DebugServer;
using IRExplorerUI.Diff;
using IRExplorerUI.OptionsPanels;
using IRExplorerCore;
using IRExplorerCore.Analysis;
using IRExplorerCore.Graph;
using IRExplorerCore.IR;
using IRExplorerCore.IR.Tags;
using IRExplorerUI.Controls;
using AvalonDock.Controls;
using System.Linq;
using AvalonDock.Layout.Serialization;
using IRExplorerUI.Profile;

namespace IRExplorerUI {
    public partial class MainWindow : Window, ISession {
        private void RegisterDefaultToolPanels() {
            RegisterPanel(SectionPanel, SectionPanelHost);
            RegisterPanel(FlowGraphPanel, FlowGraphPanelHost);
            RegisterPanel(DominatorTreePanel, DominatorTreePanelHost);
            RegisterPanel(PostDominatorTreePanel, PostDominatorTreePanelHost);
            RegisterPanel(DefinitionPanel, DefinitionPanelHost);
            RegisterPanel(IRInfoPanel, IRInfoPanelHost);
            RegisterPanel(SourceFilePanel, SourceFilePanelHost);
            RegisterPanel(BookmarksPanel, BookmarksPanelHost);
            RegisterPanel(ReferencesPanel, ReferencesPanelHost);
            RegisterPanel(NotesPanel, NotesPanelHost);
            RegisterPanel(PassOutputPanel, PassOutputHost);
            RegisterPanel(SearchResultsPanel, SearchResultsPanelHost);
            RegisterPanel(ScriptingPanel, ScriptingPanelHost);
            RegisterPanel(ExpressionGraphPanel, ExpressionGraphPanelHost);
            RegisterPanel(CallTreePanel, CallTreePanelHost);
            RegisterPanel(CallerCalleePanel, CallerCalleePanelHost);
            RegisterPanel(FlameGraphPanel, FlameGraphPanelHost);
            RenameAllPanels();
        }

        private void NotifyPanelsOfElementEvent(HandledEventKind eventKind, IRDocument document,
                                                Action<IToolPanel> action) {
            foreach (var (kind, list) in panelHostSet_) {
                foreach (var panelHost in list) {
                    var panel = panelHost.Panel;

                    if (!panel.IsPanelEnabled) {
                        continue;
                    }

                    // Accept enabled panels handling the event.
                    if (eventKind != HandledEventKind.None &&
                        (panel.HandledEvents & eventKind) == 0) {
                        continue;
                    }

                    // Don't notify panels bound to another document
                    // or with pinned content.
                    if (!ShouldNotifyPanel(panel, document)) {
                        continue;
                    }

                    // Sometimes the selection event is triggered before the
                    // section-switched event, for ex. when clicking directly on
                    // an element in another document, not first on the document
                    // tab header. Ensure that the panel is connected properly.
                    if (panel.IsUnloaded || panel.Document != document) {
                        if (!panel.IsUnloaded) {
                            panel.OnDocumentSectionUnloaded(panel.Document.Section, panel.Document);
                        }

                        panel.OnDocumentSectionLoaded(document.Section, document);

                        // After this action, the load/unload events for the panel
                        // will trigger, but since it was done here already,
                        // don't do it again, slows down UI for no reason.
                        panel.IgnoreNextLoadEvent = true;
                        panel.IgnoreNextUnloadEvent = true;
                    }

                    action(panel);
                }
            }
        }

        private void NotifyPanelsOfSessionStart() {
            ForEachPanel(panel => { panel.OnSessionStart(); });
        }

        private void NotifyPanelsOfSessionEnd() {
            ForEachPanel(panel => {
                panel.OnSessionEnd();
                panel.BoundDocument = null;
            });

            // Hide other panels that are not dockable.
            CloseDocumentSearchPanel();
            CloseDetachedPanels();
        }

        private void NotifyPanelsOfSessionSave() {
            ForEachPanel(panel => { panel.OnSessionSave(); });
        }

        private void NotifyDocumentsOfSessionSave() {
            sessionState_.DocumentHosts.ForEach(document => { document.DocumentHost.OnSessionSave(); });
        }

        private void NotifyPanelsOfSectionLoad(IRTextSection section, IRDocumentHost document, bool notifyAll) {
            ForEachPanel(panel => {
                // See comments in NotifyPanelsOfElementEvent about this check.
                if (panel.IgnoreNextLoadEvent) {
                    panel.IgnoreNextLoadEvent = false;
                }
                else if (ShouldNotifyPanel(panel, document.TextView, notifyAll)) {
                    panel.OnDocumentSectionLoaded(section, document.TextView);
                }
            });
        }

        private void NotifyPanelsOfSectionUnload(IRTextSection section, IRDocumentHost document,
                                                 bool notifyAll, bool ignoreBoundPanels = false) {
            ForEachPanel(panel => {
                // See comments in NotifyPanelsOfElementEvent about this check.
                if (panel.IgnoreNextUnloadEvent) {
                    panel.IgnoreNextUnloadEvent = false;
                }
                else if (ShouldNotifyPanel(panel, document.TextView, notifyAll, ignoreBoundPanels)) {
                    panel.OnDocumentSectionUnloaded(section, document.TextView);
                }
            });
        }

        private bool ShouldNotifyPanel(IToolPanel panel, IRDocument document,
                                       bool notifyAll = false,
                                       bool ignoreBoundPanels = false) {
            // Don't notify panels bound to another document or with pinned content.
            // If all panels should be notified, pinning in ignored.
            if (panel.BoundDocument == null) {
                if (notifyAll) {
                    return true;
                }
                else {
                    return !panel.HasPinnedContent;
                }
            }
            else {
                if (ignoreBoundPanels) {
                    return false;
                }

                return panel.BoundDocument == document;
            }
        }

        private void NotifyOfSectionUnload(IRDocumentHost document, bool notifyAll,
                                         bool ignoreBoundPanels = false,
                                         bool switchingActiveDocument = false) {
            var section = document.Section;

            if (section != null) {
                document.UnloadSection(section, switchingActiveDocument);
                NotifyPanelsOfSectionUnload(section, document, notifyAll, ignoreBoundPanels);
            }
        }

        private void NotifyPanelsOfElementHighlight(IRHighlightingEventArgs e, IRDocument document) {
            NotifyPanelsOfElementEvent(HandledEventKind.ElementHighlighting, document,
                                       panel => panel.OnElementHighlighted(e));
        }

        private void NotifyPanelsOfElementSelection(IRElementEventArgs e, IRDocument document) {
            NotifyPanelsOfElementEvent(HandledEventKind.ElementSelection, document,
                                       panel => panel.OnElementSelected(e));
        }

        private void SetupPanelEvents(PanelHostInfo panelHost) {
            panelHost.Host.Hiding += PanelHost_Hiding;
            panelHost.Host.Closing += PanelHost_Hiding;

            switch (panelHost.PanelKind) {
                case ToolPanelKind.FlowGraph:
                case ToolPanelKind.DominatorTree:
                case ToolPanelKind.PostDominatorTree: {
                    var flowGraphPanel = panelHost.Panel as GraphPanel;
                    flowGraphPanel.GraphViewer.BlockSelected += GraphViewer_GraphNodeSelected;
                    flowGraphPanel.GraphViewer.BlockMarked += GraphViewer_BlockMarked;
                    flowGraphPanel.GraphViewer.BlockUnmarked += GraphViewer_BlockUnmarked;
                    flowGraphPanel.GraphViewer.GraphLoaded += GraphViewer_GraphLoaded;
                    break;
                }
                case ToolPanelKind.ExpressionGraph: {
                    var flowGraphPanel = panelHost.Panel as GraphPanel;
                    flowGraphPanel.GraphViewer.BlockSelected += GraphViewer_GraphNodeSelected;
                    flowGraphPanel.GraphViewer.GraphLoaded += GraphViewer_GraphLoaded;
                    break;
                }
                case ToolPanelKind.CallGraph: {
                    var callGraphPanel = panelHost.Panel as CallGraphPanel;
                    callGraphPanel.GraphViewer.NodeSelected += GraphViewer_NodeSelected;
                    break;
                }
            }
        }

        private void ResetPanelEvents(PanelHostInfo panelHost) {
            panelHost.Host.Hiding -= PanelHost_Hiding;
            panelHost.Host.Closing -= PanelHost_Hiding;

            switch (panelHost.PanelKind) {
                case ToolPanelKind.FlowGraph:
                case ToolPanelKind.DominatorTree:
                case ToolPanelKind.PostDominatorTree: {
                    var flowGraphPanel = panelHost.Panel as GraphPanel;
                    flowGraphPanel.GraphViewer.BlockSelected -= GraphViewer_GraphNodeSelected;
                    flowGraphPanel.GraphViewer.BlockMarked -= GraphViewer_BlockMarked;
                    flowGraphPanel.GraphViewer.BlockUnmarked -= GraphViewer_BlockUnmarked;
                    flowGraphPanel.GraphViewer.GraphLoaded -= GraphViewer_GraphLoaded;
                    break;
                }
                case ToolPanelKind.ExpressionGraph: {
                    var flowGraphPanel = panelHost.Panel as GraphPanel;
                    flowGraphPanel.GraphViewer.BlockSelected -= GraphViewer_GraphNodeSelected;
                    flowGraphPanel.GraphViewer.GraphLoaded -= GraphViewer_GraphLoaded;
                    break;
                }
                case ToolPanelKind.CallGraph: {
                    var callGraphPanel = panelHost.Panel as CallGraphPanel;
                    callGraphPanel.GraphViewer.NodeSelected -= GraphViewer_NodeSelected;
                    break;
                }
            }
        }

        private async void DocumentHost_Closed(object sender, EventArgs e) {
            if (!(sender is LayoutDocument docHost)) {
                return;
            }

            var docHostInfo = FindDocumentHostPair(docHost);
            var document = docHostInfo.DocumentHost;

            // If the document is part of the active diff mode,
            // exit diff mode before removing it.
            if (IsDiffModeDocument(document)) {
                await ExitDocumentDiffState();
            }

            NotifyOfSectionUnload(document, true);
            UnbindPanels(docHostInfo.DocumentHost);
            RenameAllPanels();
            ResetDocumentEvents(docHostInfo.DocumentHost);
            sessionState_.DocumentHosts.Remove(docHostInfo);

            if (sessionState_.DocumentHosts.Count > 0) {
                await SetActiveDocument(sessionState_.DocumentHosts[0]);
            }
            else {
                ResetActiveDocument();
            }
        }

        private async Task SetActiveDocument(DocumentHostInfo newActivePanel, bool updateUI = true) {
            activeDocumentPanel_ = newActivePanel.HostParent;

            foreach (var item in sessionState_.DocumentHosts) {
                item.IsActiveDocument = false;
            }

            newActivePanel.IsActiveDocument = true;

            if (updateUI) {
                var docHost = newActivePanel.DocumentHost;

                if (docHost.Section != null) {
                    await SectionPanel.SelectSection(docHost.Section, false);
                    NotifyPanelsOfSectionLoad(docHost.Section, docHost, false);
                }
            }
        }

        private void ResetActiveDocument() {
            activeDocumentPanel_ = null;
        }

        private void UnbindPanels(IRDocumentHost document) {
            foreach (var (kind, list) in panelHostSet_) {
                foreach (var panelInfo in list) {
                    if (panelInfo.Panel.BoundDocument == document.TextView) {
                        panelInfo.Panel.BoundDocument = null;
                    }
                }
            }
        }

        private async void DocumentHost_IsActiveChanged(object sender, EventArgs e) {
            if (!(sender is LayoutDocument docHost)) {
                return;
            }

            if (!(docHost.Content is IRDocumentHost document)) {
                return;
            }

            if (docHost.IsSelected) {
                var activeDocument = FindActiveDocumentHost();

                if (activeDocument == document) {
                    return; // Already active one.
                }

                if (activeDocument != null) {
                    NotifyOfSectionUnload(activeDocument, false, true, true);
                }

                var hostDocPair = FindDocumentHostPair(document);
                await SetActiveDocument(hostDocPair);
            }
        }

        private DocumentHostInfo FindDocumentHostPair(IRDocumentHost document) {
            return sessionState_.DocumentHosts.Find(item => item.DocumentHost == document);
        }

        private DocumentHostInfo FindDocumentHostPair(LayoutDocument host) {
            return sessionState_.DocumentHosts.Find(item => item.Host == host);
        }

        private IRDocumentHost FindDocumentHost(IRDocument document) {
            if (document == null) {
                return null;
            }

            return sessionState_.DocumentHosts.Find(item => item.DocumentHost.TextView == document).DocumentHost;
        }

        private IRDocumentHost FindSameSummaryDocumentHost(IRTextSection section) {
            var summary = section.ParentFunction.ParentSummary;

            var results = sessionState_.DocumentHosts.FindAll(
                item => item.DocumentHost.Section != null &&
                        item.DocumentHost.Section.ParentFunction.ParentSummary ==
                        summary);

            // Try to pick the active document out of the list.
            foreach (var result in results) {
                if (result.IsActiveDocument) {
                    return result.DocumentHost;
                }
            }

            if (results.Count > 0) {
                return results[0].DocumentHost;
            }

            return null;
        }

        private IRDocumentHost FindActiveDocumentHost() {
            if (sessionState_ == null) {
                return null;
            }

            var result = sessionState_.DocumentHosts.Find(item => item.IsActiveDocument);
            return result?.DocumentHost;
        }

        private IRDocument FindActiveDocumentView() {
            if (sessionState_ == null) {
                return null;
            }

            var result = sessionState_.DocumentHosts.Find(item => item.IsActiveDocument);
            return result?.DocumentHost?.TextView;
        }

        private bool IsActiveDocument(IRDocumentHost document) {
            return FindActiveDocumentHost() == document;
        }

        private PanelHostInfo FindActivePanel(ToolPanelKind kind) {
            return panelHostSet_.TryGetValue(kind, out var list)
                ? list.Find(item => item.Panel.HasCommandFocus)
                : null;
        }

        private T FindActivePanel<T>(ToolPanelKind kind) where T : class {
            var panelHost = FindActivePanel(kind);
            return panelHost?.Panel as T;
        }

        private List<PanelHostInfo> FindTargetPanels(IRDocument document, ToolPanelKind kind) {
            var panelList = new List<PanelHostInfo>();

            if (panelHostSet_.TryGetValue(kind, out var list)) {
                // Add every panel not bound to another document.
                foreach (var panelInfo in list) {
                    if (panelInfo.Panel.BoundDocument == null ||
                        panelInfo.Panel.BoundDocument == document) {
                        panelList.Add(panelInfo);
                    }
                }
            }

            return panelList;
        }

        private PanelHostInfo FindTargetPanel(IRDocument document, ToolPanelKind kind) {
            if (panelHostSet_.TryGetValue(kind, out var list)) {
                // Use the panel bound to the document.
                var boundPanel = list.Find(item => item.Panel.BoundDocument == document);

                if (boundPanel != null) {
                    return boundPanel;
                }

                // Otherwise use, in order of preference:
                // - the last active panel that is unbound
                // - the last unbound panel
                // - the last active panel
                // - the last panel
                PanelHostInfo unboundPanelInfo = null;
                PanelHostInfo commandFocusPanelInfo = null;

                foreach (var item in list) {
                    if (item.Panel.HasCommandFocus) {
                        if (item.Panel.BoundDocument == null) {
                            return item;
                        }
                        else {
                            commandFocusPanelInfo = item;
                        }
                    }
                    else if (item.Panel.BoundDocument == null) {
                        unboundPanelInfo = item;
                    }
                }

                if (unboundPanelInfo != null) {
                    return unboundPanelInfo;
                }
                else if (commandFocusPanelInfo != null) {
                    return commandFocusPanelInfo;
                }

                return list[^1];
            }

            return null;
        }

        private T FindTargetPanel<T>(IRDocument document, ToolPanelKind kind) where T : class {
            var panelInfo = FindTargetPanel(document, kind);
            return panelInfo?.Panel as T;
        }

        private PanelHostInfo FindPanelHost(IToolPanel panel) {
            if (panelHostSet_.TryGetValue(panel.PanelKind, out var list)) {
                return list.Find(item => item.Panel == panel);
            }

            return null;
        }

        private PanelHostInfo FindPanel(LayoutAnchorable panelHost) {
            foreach (var (kind, list) in panelHostSet_) {
                var result = list.Find(item => item.Host == panelHost);

                if (result != null) {
                    return result;
                }
            }

            return null;
        }

        private string GetDefaultPanelName(ToolPanelKind kind) {
            return kind switch {
                ToolPanelKind.Bookmarks => "Bookmarks",
                ToolPanelKind.Definition => "Definition",
                ToolPanelKind.FlowGraph => "Flow Graph",
                ToolPanelKind.DominatorTree => "Dominator Tree",
                ToolPanelKind.PostDominatorTree => "Post-Dominator Tree",
                ToolPanelKind.ExpressionGraph => "Expression Graph",
                ToolPanelKind.CallGraph => "Call Graph",
                ToolPanelKind.CallTree => "Call Tree",
                ToolPanelKind.CallerCallee => "Caller/Callee",
                ToolPanelKind.FlameGraph => "Flame Graph",
                ToolPanelKind.Timeline => "Timeline",
                ToolPanelKind.Developer => "Developer",
                ToolPanelKind.Notes => "Notes",
                ToolPanelKind.References => "References",
                ToolPanelKind.Section => "Summary",
                ToolPanelKind.Source => "Source File",
                ToolPanelKind.PassOutput => "Pass Output",
                ToolPanelKind.SearchResults => "Search Results",
                ToolPanelKind.Scripting => "Scripting",
                _ => ""
            };
        }

        private void RenamePanels(ToolPanelKind kind) {
            if (panelHostSet_.TryGetValue(kind, out var list)) {
                RenamePanels(kind, list);
            }
        }

        private string GetPanelName(ToolPanelKind kind, IToolPanel panel) {
            string name = GetDefaultPanelName(kind);

            if(!string.IsNullOrEmpty(panel.TitlePrefix)) {
                name = $"{panel.TitlePrefix}{name}";
            }

            if(!string.IsNullOrEmpty(panel.TitleSuffix)) {
                name = $"{name}{panel.TitleSuffix}";
            }

            if (panel.BoundDocument != null) {
                name = $"{name} - Bound to S{panel.BoundDocument.Section.Number} ";
            }

            return name;
        }

        private void RenamePanels(ToolPanelKind kind, List<PanelHostInfo> list) {
            if (list.Count == 1) {
                list[0].Host.Title = GetPanelName(kind, list[0].Panel);
                list[0].Host.ToolTip = list[0].Panel.TitleToolTip;
            }
            else {
                for (int i = 0; i < list.Count; i++) {
                    string name = GetPanelName(kind, list[i].Panel);
                    list[i].Host.Title = $"{name}:{i + 1}";
                    list[i].Host.ToolTip = list[i].Panel.TitleToolTip;
                }
            }
        }

        private void RenameAllPanels() {
            foreach (var (kind, list) in panelHostSet_) {
                RenamePanels(kind, list);
            }
        }

        public void UpdatePanelTitles() {
            RenameAllPanels();
        }

        private IToolPanel CreateNewPanel(ToolPanelKind kind) {
            return kind switch {
                ToolPanelKind.Definition => new DefinitionPanel(),
                ToolPanelKind.References => new ReferencesPanel(),
                ToolPanelKind.Notes => new NotesPanel(),
                ToolPanelKind.PassOutput => new PassOutputPanel(),
                ToolPanelKind.FlowGraph => new GraphPanel(),
                ToolPanelKind.DominatorTree => new GraphPanel(),
                ToolPanelKind.PostDominatorTree => new GraphPanel(),
                ToolPanelKind.ExpressionGraph => new ExpressionGraphPanel(),
                ToolPanelKind.SearchResults => new SearchResultsPanel(),
                ToolPanelKind.Scripting => new ScriptingPanel(),
                _ => throw new InvalidOperationException()
            };
        }

        private T CreateNewPanel<T>(ToolPanelKind kind) where T : class {
            return CreateNewPanel(kind) as T;
        }

        public void DisplayFloatingPanel(IToolPanel panel) {
            var panelHost = DisplayNewPanel(panel, null, DuplicatePanelKind.Floating);
            panelHost.Host.CanClose = true;
            panelHost.Host.CanAutoHide = false;
            panelHost.Host.CanHide = false;
            RenameAllPanels();
        }

        private PanelHostInfo DisplayNewPanel(IToolPanel newPanel, IToolPanel relativePanel,
                                              DuplicatePanelKind duplicateKind) {
            var panelHost = AddNewPanel(newPanel);
            bool attached = false;

            switch (duplicateKind) {
                case DuplicatePanelKind.Floating: {
                    DisplayFloatingWindow(panelHost.Host);
                    attached = true;
                    break;
                }
                case DuplicatePanelKind.NewSetDockedLeft: {
                    panelHost.Host.AddToLayout(DockManager, AnchorableShowStrategy.Right);

                    var baseHost = FindPanelHost(relativePanel).Host;
                    var baseGroup = baseHost.FindParent<LayoutAnchorablePaneGroup>();

                    if (baseGroup == null) {
                        break;
                    }

                    baseGroup.Children.Insert(0, new LayoutAnchorablePane(panelHost.Host));
                    attached = true;
                    break;
                }
                case DuplicatePanelKind.NewSetDockedRight: {
                    panelHost.Host.AddToLayout(DockManager, AnchorableShowStrategy.Right);

                    var baseHost = FindPanelHost(relativePanel).Host;
                    var baseGroup = baseHost.FindParent<LayoutAnchorablePaneGroup>();

                    if (baseGroup == null) {
                        break;
                    }

                    baseGroup.Children.Add(new LayoutAnchorablePane(panelHost.Host));
                    attached = true;
                    break;
                }
                case DuplicatePanelKind.SameSet: {
                    // Insert the new panel on the right of the cloned one.
                    panelHost.Host.AddToLayout(DockManager, AnchorableShowStrategy.Right);

                    var relativePanelHost = FindPanelHost(relativePanel);
                    var relativeLayoutPane = relativePanelHost.Host.FindParent<LayoutAnchorablePane>();

                    if (relativeLayoutPane == null) {
                        break;
                    }

                    int basePaneIndex = relativeLayoutPane.Children.IndexOf(relativePanelHost.Host);
                    relativeLayoutPane.Children.Insert(basePaneIndex + 1, panelHost.Host);
                    attached = true;
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(duplicateKind), duplicateKind, null);
            }

            // Docking can fail if the target panel is hidden, make it a floating panel.
            //? TODO: Should try to make the hidden panel visible
            if (!attached) {
                panelHost.Host.Float();
            }

            panelHost.Host.IsSelected = true;
            return panelHost;
        }

        private void DisplayFloatingWindow(LayoutContent host) {
            //? TODO: Use saved position settings
            host.FloatingLeft = Left + 100;
            host.FloatingTop = Top + 100;
            host.FloatingWidth = 800;
            host.FloatingHeight = 600;

            var window = DockManager.CreateFloatingWindow(host, false);
            window.Show();
        }

        private PanelHostInfo AddNewPanel(IToolPanel panel) {
            return RegisterPanel(panel, new LayoutAnchorable {
                Content = panel
            });
        }

        private PanelHostInfo RegisterPanel(IToolPanel panel, LayoutAnchorable host) {
            if (!panelHostSet_.TryGetValue(panel.PanelKind, out var list)) {
                list = new List<PanelHostInfo>();
            }

            var panelHost = new PanelHostInfo(panel, host);
            list.Add(panelHost);
            panelHostSet_[panel.PanelKind] = list;

            // Setup events.
            SetupPanelEvents(panelHost);
            panel.OnRegisterPanel();
            panel.Session = this;

            // Make it the active panel in the group.
            SwitchCommandFocusToPanel(panelHost);
            return panelHost;
        }

        private void UnregisterPanel(PanelHostInfo panelHost) {
            if (panelHostSet_.TryGetValue(panelHost.PanelKind, out var list)) {
                list.Remove(panelHost);
                panelHost.Panel.OnUnregisterPanel();
                panelHost.Panel.Session = null;
            }
        }

        private void ForEachPanel(ToolPanelKind panelKind, Action<IToolPanel> action) {
            if (panelHostSet_.TryGetValue(panelKind, out var list)) {
                list.ForEach(item => action(item.Panel));
            }
        }

        private void ForEachPanel(Action<IToolPanel> action) {
            foreach (var (kind, list) in panelHostSet_) {
                list.ForEach(item => action(item.Panel));
            }
        }

        public void RedrawPanels(params ToolPanelKind[] kinds) {
            foreach (var (kind, list) in panelHostSet_) {
                list.ForEach(item => {
                    if (kinds.Length == 0 || kinds.Contains(item.PanelKind)) {
                        item.Panel.OnRedrawPanel();
                    }
                });
            }
        }

        private void SwitchCommandFocusToPanel(PanelHostInfo panelHost) {
            var activePanel = FindActivePanel(panelHost.PanelKind);

            if (activePanel != null) {
                activePanel.Panel.HasCommandFocus = false;
            }

            panelHost.Panel.HasCommandFocus = true;
        }

        private void PickCommandFocusPanel(ToolPanelKind kind) {
            // Pick last panel without pinned content.
            if (!panelHostSet_.TryGetValue(kind, out var list)) {
                return;
            }

            if (list.Count == 0) {
                return;
            }

            var lastPanel = list.FindLast(item => !item.Panel.HasPinnedContent);

            if (lastPanel == null) {
                // If not found, just pick the last panel.
                lastPanel = list[^1];
            }

            lastPanel.Panel.HasCommandFocus = true;
        }

        private void PanelHost_Hiding(object sender, CancelEventArgs e) {
            var panelHost = sender as LayoutAnchorable;
            var panelInfo = FindPanel(panelHost);
            panelInfo.Panel.HasCommandFocus = false;

            // panelInfo.Panel.IsPanelEnabled = false;
            UnregisterPanel(panelInfo);
            ResetPanelEvents(panelInfo);
            RenamePanels(panelInfo.PanelKind);
            PickCommandFocusPanel(panelInfo.PanelKind);
        }

        private void LayoutAnchorable_IsActiveChanged(object sender, EventArgs e) {
            if (!(sender is LayoutAnchorable panelHost)) {
                return;
            }

            if (!(panelHost.Content is IToolPanel toolPanel)) {
                return;
            }

            if (panelHost.IsActive) {
                toolPanel.OnActivatePanel();
            }
            else {
                toolPanel.OnDeactivatePanel();
            }
        }

        private void LayoutAnchorable_IsSelectedChanged(object sender, EventArgs e) {
            if (!(sender is LayoutAnchorable panelHost)) {
                return;
            }

            if (!(panelHost.Content is IToolPanel toolPanel)) {
                return;
            }

            if (panelHost.IsSelected) {
                toolPanel.OnShowPanel();
            }
            else {
                toolPanel.OnHidePanel();
            }
        }

        private async Task<DocumentHostInfo> AddNewDocument(OpenSectionKind kind) {
            var document = new IRDocumentHost(this);
            var host = new LayoutDocument {
                Content = document
            };

            // The document group must be found at runtime since when restoring
            // the dock layout to a previous state, it creates another layout tree
            // that is different than the initial layout defined in the XAML file.
            var documentGroup = DockManager.Layout.Descendents().
                OfType<LayoutDocumentPaneGroup>().FirstOrDefault();

            switch (kind) {
                case OpenSectionKind.ReplaceCurrent:
                case OpenSectionKind.NewTab: {
                    if (activeDocumentPanel_ == null) {
                        activeDocumentPanel_ = new LayoutDocumentPane(host);
                        documentGroup.Children.Add(activeDocumentPanel_);
                    }
                    else {
                        activeDocumentPanel_.Children.Add(host);
                    }

                    break;
                }
                case OpenSectionKind.NewTabDockLeft:
                case OpenSectionKind.ReplaceLeft: {
                    activeDocumentPanel_ = new LayoutDocumentPane(host);
                    documentGroup.Children.Insert(0, activeDocumentPanel_);
                    break;
                }
                case OpenSectionKind.NewTabDockRight:
                case OpenSectionKind.ReplaceRight: {
                    activeDocumentPanel_ = new LayoutDocumentPane(host);
                    documentGroup.Children.Add(activeDocumentPanel_);
                    break;
                }
                default: {
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
                }
            }

            var documentHost = new DocumentHostInfo(document, host);
            documentHost.HostParent = activeDocumentPanel_;
            sessionState_.DocumentHosts.Add(documentHost);

            await SetActiveDocument(documentHost, false);
            host.IsActiveChanged += DocumentHost_IsActiveChanged;
            host.Closed += DocumentHost_Closed;
            host.IsSelected = true;
            return documentHost;
        }

        public void PopulateBindMenu(IToolPanel panel, BindMenuItemsArgs args) {
            foreach (var doc in sessionState_.DocumentHosts) {
                var menuItem = new BindMenuItem {
                    Header = compilerInfo_.NameProvider.GetSectionName(doc.DocumentHost.Section, true),
                    ToolTip = doc.DocumentHost.Section.ParentFunction.Name,
                    Tag = doc.DocumentHost.TextView,
                    IsChecked = panel.BoundDocument == doc.DocumentHost.TextView
                };

                args.MenuItems.Add(menuItem);
            }
        }

        public void BindToDocument(IToolPanel panel, BindMenuItem args) {
            var document = args.Tag as IRDocument;

            if (panel.BoundDocument == document) {
                // Unbind on second click.
                panel.BoundDocument = null;
            }
            else {
                panel.BoundDocument = document;
            }
        }

        public IRDocument FindAssociatedDocument(IToolPanel panel) {
            return panel.BoundDocument ?? FindActiveDocumentView();
        }

        public IRDocumentHost FindAssociatedDocumentHost(IToolPanel panel) {
            if (panel.BoundDocument != null) {
                return FindDocumentHost(panel.BoundDocument);
            }

            return FindActiveDocumentHost();
        }

        public void SavePanelState(object stateObject, IToolPanel panel,
                                   IRTextSection section, IRDocument document) {
            //? TODO: Find a way to at least temporarily save state for the two diffed docs
            //? Issue is that in diff mode a section can have a different FunctionIR depending
            //? on the other section is compared with
            if (IsInDiffMode) {
                if(document == null) {
                    return;
                }

                var docHost = FindDocumentHost(document);

                if(sessionState_.SectionDiffState.IsDiffDocument(docHost)) {
                    sessionState_.SaveDiffModePanelState(stateObject, panel, section);
                    return;
                }
            }

            sessionState_.SavePanelState(stateObject, panel, section);
        }

        public object LoadPanelState(IToolPanel panel, IRTextSection section, IRDocument document) {
            if (IsInDiffMode) {
                if (document == null) {
                    return null;
                }

                var docHost = FindDocumentHost(document);

                if (sessionState_.SectionDiffState.IsDiffDocument(docHost)) {
                    return sessionState_.LoadDiffModePanelState(panel, section);
                }
            }

            return sessionState_.LoadPanelState(panel, section);
        }

        public void DuplicatePanel(IToolPanel panel, DuplicatePanelKind duplicateKind) {
            var newPanel = CreateNewPanel(panel.PanelKind);

            if (newPanel != null) {
                DisplayNewPanel(newPanel, panel, duplicateKind);
                RenamePanels(newPanel.PanelKind);
                newPanel.ClonePanel(panel);
            }
        }

        private async Task SetupPanels() {
            NotifyPanelsOfSessionStart();
            await SetupSectionPanel();
        }

        private async Task SetupSectionPanel() {
            if (SectionPanel.MainSummary == null) {
                SectionPanel.CompilerInfo = compilerInfo_;
                
                foreach (var doc in sessionState_.Documents) {
                    if (doc != sessionState_.MainDocument &&
                        doc != sessionState_.DiffDocument) {
                        // Add optional modules, usually used for profiling.
                        SectionPanel.AddModuleSummary(doc.Summary);
                    }
                }

                await SectionPanel.SetMainSummary(sessionState_.MainDocument.Summary);
                SectionPanel.MainTitle = sessionState_.MainDocument.ModuleName;
                SectionPanel.OnSessionStart();
            }

            if (sessionState_.IsInTwoDocumentsDiffMode) {
                await SectionPanel.SetDiffSummary(sessionState_.DiffDocument.Summary);
                SectionPanel.DiffTitle = sessionState_.DiffDocument.ModuleName;
            }

            //? TODO: not neeeded await SectionPanel.Update();

            if (sessionState_.IsInTwoDocumentsDiffMode) {
                await ShowSectionPanelDiffs(sessionState_.DiffDocument);
            }
        }

        public void RegisterDetachedPanel(DraggablePopup panel) {
            if (!detachedPanels_.Contains(panel)) {
                detachedPanels_.Add(panel);
            }
        }

        public void UnregisterDetachedPanel(DraggablePopup panel) {
            if (panel.IsDetached) {
                detachedPanels_.Remove(panel);
            }
        }

        private bool RestoreDockLayout() {
            var dockLayoutFile = App.GetDefaultDockLayoutFilePath();

            if (!File.Exists(dockLayoutFile)) {
                return false;
            }

            return RestoreDockLayout(dockLayoutFile);
        }

        private bool RestoreDockLayout(string dockLayoutFile) {
            try {
                var serializer = new XmlLayoutSerializer(DockManager);
                var visiblePanels = new List<IToolPanel>();

                serializer.LayoutSerializationCallback += (s, args) => {
                    if (args.Model is LayoutDocument) {
                        args.Cancel = true; // Don't recreate any document panels.
                    }
                    else {
                        args.Content = args.Content;

                        if (args.Content is not IToolPanel panel) {
                            args.Cancel = true;
                            return;
                        }

                        if (panel.PanelKind == ToolPanelKind.Other) {
                            args.Cancel = true;
                            return;
                        }

                        var panelHost = (LayoutAnchorable)args.Model;
                        panelHost.IsActiveChanged += LayoutAnchorable_IsActiveChanged;
                        panelHost.IsSelectedChanged += LayoutAnchorable_IsSelectedChanged;

                        if (panelHost.IsVisible && panelHost.IsSelected) {
                            visiblePanels.Add(panel);
                        }

                        switch (panel.PanelKind) {
                            case ToolPanelKind.CallTree: {
                                CallTreePanel = (CallTreePanel)panel;
                                CallTreePanelHost = (LayoutAnchorable)args.Model;
                                RegisterPanel(CallTreePanel, CallTreePanelHost);
                                break;
                            }
                            case ToolPanelKind.CallerCallee: {
                                CallerCalleePanel = (CallerCalleePanel)panel;
                                CallerCalleePanelHost = (LayoutAnchorable)args.Model;
                                RegisterPanel(CallerCalleePanel, CallerCalleePanelHost);
                                break;
                            }
                            case ToolPanelKind.FlameGraph: {
                                FlameGraphPanel = (FlameGraphPanel)panel;
                                FlameGraphPanelHost= (LayoutAnchorable)args.Model;
                                RegisterPanel(FlameGraphPanel, FlameGraphPanelHost);
                                break;
                            }
                            case ToolPanelKind.Timeline: {
                                TimelinePanel = (TimelinePanel)panel;
                                TimelinePanelHost = (LayoutAnchorable)args.Model;
                                RegisterPanel(TimelinePanel, TimelinePanelHost);
                                break;
                            }
                            case ToolPanelKind.Bookmarks: {
                                BookmarksPanel = (BookmarksPanel)panel;
                                BookmarksPanelHost = (LayoutAnchorable)args.Model;
                                break;
                            }
                            case ToolPanelKind.Definition: {
                                DefinitionPanel = (DefinitionPanel)panel;
                                DefinitionPanelHost = (LayoutAnchorable)args.Model;
                                RegisterPanel(DefinitionPanel, DefinitionPanelHost);
                                break;
                            }
                            case ToolPanelKind.FlowGraph: {
                                FlowGraphPanel = (GraphPanel)panel;
                                FlowGraphPanelHost = (LayoutAnchorable)args.Model;
                                RegisterPanel(FlowGraphPanel, FlowGraphPanelHost);
                                break;
                            }
                            case ToolPanelKind.DominatorTree: {
                                DominatorTreePanel = (DominatorTreePanel)panel;
                                DominatorTreePanelHost = (LayoutAnchorable)args.Model;
                                RegisterPanel(DominatorTreePanel, DominatorTreePanelHost);
                                break;
                            }
                            case ToolPanelKind.PostDominatorTree: {
                                PostDominatorTreePanel = (PostDominatorTreePanel)panel;
                                PostDominatorTreePanelHost = (LayoutAnchorable)args.Model;
                                RegisterPanel(PostDominatorTreePanel, PostDominatorTreePanelHost);
                                break;
                            }
                            case ToolPanelKind.ExpressionGraph: {
                                ExpressionGraphPanel = (ExpressionGraphPanel)panel;
                                ExpressionGraphPanelHost = (LayoutAnchorable)args.Model;
                                RegisterPanel(ExpressionGraphPanel, ExpressionGraphPanelHost);
                                break;
                            }
                            case ToolPanelKind.Developer: {
                                IRInfoPanel = (IRInfoPanel)panel;
                                IRInfoPanelHost = (LayoutAnchorable)args.Model;
                                RegisterPanel(IRInfoPanel, IRInfoPanelHost);
                                break;
                            }
                            case ToolPanelKind.Notes: {
                                NotesPanel = (NotesPanel)panel;
                                NotesPanelHost = (LayoutAnchorable)args.Model;
                                RegisterPanel(NotesPanel, NotesPanelHost);
                                break;
                            }
                            case ToolPanelKind.References: {
                                ReferencesPanel = (ReferencesPanel)panel;
                                ReferencesPanelHost = (LayoutAnchorable)args.Model;
                                RegisterPanel(ReferencesPanel, ReferencesPanelHost);
                                break;
                            }
                            case ToolPanelKind.Section: {
                                SectionPanel = (SectionPanelPair)panel;
                                SectionPanelHost = (LayoutAnchorable)args.Model;
                                RegisterPanel(SectionPanel, SectionPanelHost);
                                break;
                            }
                            case ToolPanelKind.Source: {
                                SourceFilePanel = (SourceFilePanel)panel;
                                SourceFilePanelHost = (LayoutAnchorable)args.Model;
                                RegisterPanel(SourceFilePanel, SourceFilePanelHost);
                                break;
                            }
                            case ToolPanelKind.PassOutput: {
                                PassOutputPanel = (PassOutputPanel)panel;
                                PassOutputHost = (LayoutAnchorable)args.Model;
                                RegisterPanel(PassOutputPanel, PassOutputHost);
                                break;
                            }
                            case ToolPanelKind.SearchResults: {
                                SearchResultsPanel = (SearchResultsPanel)panel;
                                SearchResultsPanelHost = (LayoutAnchorable)args.Model;
                                RegisterPanel(SearchResultsPanel, SearchResultsPanelHost);
                                break;
                            }
                            case ToolPanelKind.Scripting: {
                                ScriptingPanel = (ScriptingPanel)panel;
                                ScriptingPanelHost = (LayoutAnchorable)args.Model;
                                RegisterPanel(ScriptingPanel, ScriptingPanelHost);
                                break;
                            }
                        }

                        // Manually invoke the events, they are not triggered automatically.
                        foreach (var visiblePanel in visiblePanels) {
                            visiblePanel.OnShowPanel();
                            visiblePanel.OnActivatePanel();
                        }
                    }
                };

                serializer.Deserialize(dockLayoutFile);
                return true;
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed to load dock layout: {ex}");
                return false;
            }
        }

        private void ShowPanelMenuClicked(object sender, RoutedEventArgs e) {
            //? TODO: Panel hosts must be found at runtime because of deserialization
            LayoutAnchorable panelHost = null;

            switch (((MenuItem)sender).Tag) {
                case "Section": {
                    panelHost = SectionPanelHost;
                    break;
                }
                case "Definition": {
                    panelHost = DefinitionPanelHost;
                    break;
                }
                case "References": {
                    panelHost = ReferencesPanelHost;
                    break;
                }
                case "Bookmarks": {
                    panelHost = SectionPanelHost;
                    break;
                }
                case "SourceFile": {
                    panelHost = SourceFilePanelHost;
                    break;
                }
                case "PassOutput": {
                    panelHost = PassOutputHost;
                    break;
                }
                case "SearchResults": {
                    panelHost = SearchResultsPanelHost;
                    break;
                }
                case "Notes": {
                    panelHost = NotesPanelHost;
                    break;
                }
                case "Scripting": {
                    panelHost = ScriptingPanelHost;
                    break;
                }
                case "Developer": {
                    panelHost = IRInfoPanelHost;
                    break;
                }
                case "FlowGraph": {
                    panelHost = FlowGraphPanelHost;
                    break;
                }
                case "DominatorTree": {
                    panelHost = DominatorTreePanelHost;
                    break;
                }
                case "PostDominatorTree": {
                    panelHost = PostDominatorTreePanelHost;
                    break;
                }
                case "ExpressionGraph": {
                    panelHost = ExpressionGraphPanelHost;
                    break;
                }
                case "CallTree": {
                    panelHost = CallTreePanelHost;
                    break;
                }
                case "CallerCallee": {
                    panelHost = CallerCalleePanelHost;
                    break;
                }
                case "FlameGraph": {
                    panelHost = FlameGraphPanelHost;
                    break;
                }
                case "Timeline": {
                    panelHost = TimelinePanelHost;
                    break;
                }
            }

            if(panelHost != null) {
                panelHost.Show();
                panelHost.IsActive = true;
            }
        }

        private void MenuItem_OnClick(object sender, RoutedEventArgs e) {
            Trace.Flush();
            var file = App.GetTraceFilePath();

            if (File.Exists(file)) {
                try {
                    var psi = new ProcessStartInfo(file) {
                        UseShellExecute = true
                    };

                    Process.Start(psi);
                }
                catch (Exception ex) {
                    MessageBox.Show($"Failed to open log file {file}\n{ex.Message}", "IR Explorer",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else {
                MessageBox.Show($"No log file found: {file}", "IR Explorer",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void UnloadAllPDBsClicked(object sender, RoutedEventArgs e) {
            if (ProfileData != null) {
                foreach (var (module, debugInfo) in ProfileData.ModuleDebugInfo) {
                    debugInfo.Unload();
                }
            }
        }
    }
}