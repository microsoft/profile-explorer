using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using IRExplorerCore;
using IRExplorerCore.Utilities;

namespace IRExplorerUI.Profile;

public partial class FlameGraphPanel : ToolPanelControl, IFunctionProfileInfoProvider, INotifyPropertyChanged {
    private FlameGraphSettings settings_;
    private bool dragging_;
    private Point draggingStart_;
    private Point draggingViewStart_;
    private bool panelVisible_;
    private ProfileCallTree callTree_;
    private ProfileCallTree pendingCallTree_; // Tree to show when panel becomes visible.
    private FlameGraphNode enlargedNode_;
    private DateTime lastWheelZoomTime_;
    private List<FlameGraphNode> searchResultNodes_;
    private int searchResultIndex_;
    

    public FlameGraphPanel() {
        InitializeComponent();
        settings_ = App.Settings.FlameGraphSettings;
        SetupEvents();
        DataContext = this;
        ShowNodePanel = true;
    }

    public override ToolPanelKind PanelKind => ToolPanelKind.FlameGraph;
  
    public override ISession Session {
        get => base.Session;
        set {
            base.Session = value;
            GraphHost.Session = value;
            NodeDetailsPanel.Initialize(value, this);
        }
    }

    public bool PrependModuleToFunction {
        get => settings_.PrependModuleToFunction;
        set {
            if (value != settings_.PrependModuleToFunction) {
                settings_.PrependModuleToFunction = value;
                GraphHost.GraphViewer.SettingsUpdated(settings_);
                OnPropertyChanged();
            }
        }
    }

    public bool SyncSourceFile {
        get => settings_.SyncSourceFile;
        set {
            if (value != settings_.SyncSourceFile) {
                settings_.SyncSourceFile = value;
                OnPropertyChanged();
            }
        }
    }

    public bool UseCompactMode {
        get => settings_.UseCompactMode;
        set {
            if (value != settings_.UseCompactMode) {
                settings_.UseCompactMode = value;
                GraphHost.GraphViewer.SettingsUpdated(settings_);
                OnPropertyChanged();
            }
        }
    }

    private bool showSearchSection_;
    public bool ShowSearchSection {
        get => showSearchSection_;
        set {
            if (showSearchSection_ != value) {
                showSearchSection_ = value;
                OnPropertyChanged();
            }
        }
    }

    private string searchResultText_;
    public string SearchResultText {
        get => searchResultText_;
        set {
            if (searchResultText_ != value) {
                searchResultText_ = value;
                OnPropertyChanged();
            }
        }
    }

    public override void OnShowPanel() {
        base.OnShowPanel();
        panelVisible_ = true;
        InitializePendingCallTree();
    }

    private void SchedulePendingCallTree(ProfileCallTree callTree) {
        // Display flame graph once the panel is visible and visible area is valid.
        if (pendingCallTree_ == null) {
            pendingCallTree_ = callTree;
            InitializePendingCallTree();
        }
    }

    private void InitializePendingCallTree() {
        if (pendingCallTree_ != null && panelVisible_) {
            InitializeCallTree(pendingCallTree_);
            pendingCallTree_ = null;
        }
    }

    private void InitializeCallTree(ProfileCallTree callTree) {
        callTree_ = callTree;
        GraphHost.InitializeFlameGraph(callTree);
    }

    public override void OnSessionStart() {
        base.OnSessionStart();
        InitializePendingCallTree();
    }

    private void SetupEvents() {
        GraphHost.NodeSelected += GraphHost_NodeSelected;
        GraphHost.NodesDeselected += GraphHost_NodesDeselected;

        // Setup events for the node details view.
        NodeDetailsPanel.NodeInstanceChanged += NodeDetailsPanel_NodeInstanceChanged;
        NodeDetailsPanel.BacktraceNodeClick += NodeDetailsPanel_NodeClick;
        NodeDetailsPanel.BacktraceNodeDoubleClick += NodeDetailsPanel_NodeDoubleClick;
        NodeDetailsPanel.InstanceNodeClick += NodeDetailsPanel_NodeClick;
        NodeDetailsPanel.InstanceNodeDoubleClick += NodeDetailsPanel_NodeDoubleClick;
        NodeDetailsPanel.FunctionNodeClick += NodeDetailsPanel_NodeClick;
        NodeDetailsPanel.FunctionNodeDoubleClick += NodeDetailsPanel_NodeDoubleClick;
        NodeDetailsPanel.NodesSelected += NodeDetailsPanel_NodesSelected;
    }

    private void GraphHost_NodesDeselected(object sender, EventArgs e) {
        
    }

    private async void GraphHost_NodeSelected(object sender, FlameGraphNode node) {
        if (node.HasFunction) {
            await NodeDetailsPanel.ShowWithDetailsAsync(node.CallTreeNode);

            if (settings_.SyncSourceFile) {
                // Load the source file and scroll to the hottest line.
                await Session.OpenProfileSourceFile(node.CallTreeNode);
            }
        }
    }

    private void NodeDetailsPanel_NodesSelected(object sender, List<ProfileCallTreeNode> e) {
        var nodes = GraphHost.GraphViewer.SelectNodes(e);
        if (nodes.Count > 0) {
            GraphHost.BringNodeIntoView(nodes[0], false);
        }
    }

    private void NodeDetailsPanel_NodeInstanceChanged(object sender, ProfileCallTreeNode e) {
        var node = GraphHost.GraphViewer.SelectNode(e);
        GraphHost.BringNodeIntoView(node);
    }

    private async void NodeDetailsPanel_NodeClick(object sender, ProfileCallTreeNode e) {
        var nodes = GraphHost.GraphViewer.SelectNodes(e);
        if (nodes.Count > 0) {
            GraphHost.BringNodeIntoView(nodes[0], false);
        }
    }

    private async void NodeDetailsPanel_NodeDoubleClick(object sender, ProfileCallTreeNode e) {
        await OpenFunction(e);
    }
    
    private async Task OpenFunction(ProfileCallTreeNode node) {
        if (node != null && node.Function.HasSections) {
            var openMode = Utils.IsShiftModifierActive() ? OpenSectionKind.NewTabDockRight : OpenSectionKind.ReplaceCurrent;
            await Session.OpenProfileFunction(node, openMode);
        }
    }
    
    private bool showNodePanel_;
    public bool ShowNodePanel {
        get => showNodePanel_;
        set => SetField(ref showNodePanel_, value);
    }
    
    private void ExecuteGraphResetWidth(object sender, ExecutedRoutedEventArgs e) {
        //? TODO: Buttons should be disabled
        GraphHost.ResetWidth();
    }

    private void ExecuteGraphZoomIn(object sender, ExecutedRoutedEventArgs e) {
        GraphHost.ZoomIn();
    }
    
    private void ExecuteGraphZoomOut(object sender, ExecutedRoutedEventArgs e) {
        GraphHost.ZoomOut();
    }
    
    private void PanelToolbarTray_SettingsClicked(object sender, EventArgs e) {
        throw new NotImplementedException();
    }

    private void ToolBar_Loaded(object sender, RoutedEventArgs e) {
        Utils.PatchToolbarStyle(sender as ToolBar);
    }
    
    private async void UndoButtoon_Click(object sender, RoutedEventArgs e) {
        await GraphHost.RestorePreviousState();
    }

    private async void SelectParent_Click(object sender, RoutedEventArgs e) {
        await GraphHost.NavigateToParentNode();
    }
    
    public async Task DisplayFlameGraph() {
        var callTree = Session.ProfileData.CallTree;
        SchedulePendingCallTree(callTree);
    }

    public override void OnSessionEnd() {
        base.OnSessionEnd();
        GraphHost.Reset();
        callTree_ = null;
        pendingCallTree_ = null;
    }
    
    public List<ProfileCallTreeNode> GetBacktrace(ProfileCallTreeNode node) {
        return callTree_.GetBacktrace(node);
    }

    public List<ModuleProfileInfo> GetTopModules(ProfileCallTreeNode node) {
        return callTree_.GetTopModules(node);
    }

    public List<ProfileCallTreeNode> GetTopFunctions(ProfileCallTreeNode node) {
        return callTree_.GetTopFunctions(node);
    }

    public event PropertyChangedEventHandler PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null) {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null) {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private async void FunctionFilter_OnTextChanged(object sender, TextChangedEventArgs e) {
        var text = FunctionFilter.Text.Trim();
        await SearchFlameGraph(text);
    }

    private async Task SearchFlameGraph(string text) {
        var prevSearchResultNodes = searchResultNodes_;
        searchResultNodes_ = null;

        if (prevSearchResultNodes != null) {
            bool redraw = text.Length <= 1; // Prevent flicker by redrawing once when search is done.
            GraphHost.GraphViewer.ResetSearchResultNodes(prevSearchResultNodes, redraw);
        }

        if (text.Length > 1) {
            searchResultNodes_ = await Task.Run(() => GraphHost.GraphViewer.FlameGraph.SearchNodes(text));
            GraphHost.GraphViewer.MarkSearchResultNodes(searchResultNodes_);

            searchResultIndex_ = -1;
            SelectNextSearchResult();
            ShowSearchSection = true;
        }
        else {
            ShowSearchSection = false;
        }
    }

    private void UpdateSearchResultText() {
        SearchResultText = searchResultNodes_ is { Count: > 0 } ? $"{searchResultIndex_ + 1} / {searchResultNodes_.Count}" : "Not found";
    }

    private void SelectPreviousSearchResult() {
        if (searchResultNodes_ != null && searchResultIndex_ > 0) {
            searchResultIndex_--;
            UpdateSearchResultText();
            GraphHost.BringNodeIntoView(searchResultNodes_[searchResultIndex_]);
        }
    }

    private void SelectNextSearchResult() {
        if (searchResultNodes_ != null && searchResultIndex_ < searchResultNodes_.Count - 1) {
            searchResultIndex_++;
            UpdateSearchResultText();
            GraphHost.BringNodeIntoView(searchResultNodes_[searchResultIndex_]);
        }
    }

    private void PreviousSearchResultExecuted(object sender, ExecutedRoutedEventArgs e) {
        SelectPreviousSearchResult();
    }

    private void NextSearchResultExecuted(object sender, ExecutedRoutedEventArgs e) {
        SelectNextSearchResult();
    }

    private Dictionary<int, List<SampleIndex>>
        FindFunctionSamples(ProfileCallTreeNode node, ProfileData profile) {
        var sw = Stopwatch.StartNew();
        var allThreadsList = new List<SampleIndex>();
        var threadListMap = new Dictionary<int, List<SampleIndex>>();
        threadListMap[-1] = allThreadsList;

        if (node.Function == null) {
            return threadListMap;
        }

        int sampleStartIndex = 0;
        int sampleEndIndex = profile.Samples.Count;
        var funcProfile = profile.GetFunctionProfile(node.Function);

        if (funcProfile != null && funcProfile.SampleStartIndex != int.MaxValue) {
            sampleStartIndex = funcProfile.SampleStartIndex;
            sampleEndIndex = funcProfile.SampleEndIndex;
        }

        int index = 0;

        //? Also here - Abstract parallel run chunks to take action per sample

        for (int i = sampleStartIndex; i < sampleEndIndex; i++) {
            var (sample, stack) = profile.Samples[i];
            foreach (var stackFrame in stack.StackFrames) {
                if (stackFrame.IsUnknown) continue;

                if (stackFrame.Info.Function.Value.Equals(node.Function)) {
                    var threadList = threadListMap.GetOrAddValue(stack.Context.ThreadId);
                    threadList.Add(new SampleIndex(index, sample.Time));
                    allThreadsList.Add(new SampleIndex(index, sample.Time));

                    break;
                }
            }

            index++;
        }

        Trace.WriteLine($"FindSamples took: {sw.ElapsedMilliseconds} for {allThreadsList.Count} samples");
        return threadListMap;
    }

    private HashSet<IRTextFunction> FindFunctionsForSamples(int sampleStartIndex, int sampleEndIndex, int threadId, ProfileData profile) {
        var funcSet = new HashSet<IRTextFunction>();

        //? Abstract parallel run chunks to take action per sample (ComputeFunctionProfile)
        for (int i = sampleStartIndex; i < sampleEndIndex; i++) {
            var (sample, stack) = profile.Samples[i];

            if (threadId != -1 && stack.Context.ThreadId != threadId) {
                continue;
            }

            foreach (var stackFrame in stack.StackFrames) {
                if (stackFrame.IsUnknown)
                    continue;
                funcSet.Add(stackFrame.Info.Function);
            }
        }

        return funcSet;
    }

    private List<ProfileCallTreeNode> FindCallTreeNodesForSamples(int sampleStartIndex, int sampleEndIndex, int threadId, ProfileData profile) {
        var sw = Stopwatch.StartNew();
        var funcs = FindFunctionsForSamples(sampleStartIndex, sampleEndIndex, threadId, profile);
        var callNodes = new List<ProfileCallTreeNode>(funcs.Count);

        foreach (var func in funcs) {
            var nodes = profile.CallTree.GetCallTreeNodes(func);
            if (nodes != null) {
                callNodes.AddRange(nodes);
            }
        }

        Trace.WriteLine($"FindCallTreeNodesForSamples took: {sw.ElapsedMilliseconds} for {callNodes.Count} call nodes");
        return callNodes;
    }

    private async void SelectFunctionExecuted(object sender, ExecutedRoutedEventArgs e) {
        var selectedNode = GraphHost.GraphViewer.SelectedNode;
        if (selectedNode != null && selectedNode.HasFunction) {
            await Session.SwitchActiveProfileFunction(selectedNode.CallTreeNode);
        }
    }

    private async void OpenFunctionExecuted(object sender, ExecutedRoutedEventArgs e) {
        await OpenFunction(GraphHost.GraphViewer.SelectedNode, OpenSectionKind.ReplaceCurrent);
    }
    
    private async Task OpenFunction(FlameGraphNode node, OpenSectionKind openMode) {
        if (node != null && node.HasFunction) {
            await Session.OpenProfileFunction(node.CallTreeNode, openMode);
        }
    }

    private async void OpenFunctionInNewTabExecuted(object sender, ExecutedRoutedEventArgs e) {
        await OpenFunction(GraphHost.GraphViewer.SelectedNode, OpenSectionKind.NewTabDockRight);
    }

    private async void ChangeRootNodeExecuted(object sender, ExecutedRoutedEventArgs e) {
        if (GraphHost.GraphViewer.SelectedNode != null) {
            await GraphHost.ChangeRootNode(GraphHost.GraphViewer.SelectedNode);
        }
    }
}