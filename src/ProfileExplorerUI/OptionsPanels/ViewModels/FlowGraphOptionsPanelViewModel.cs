// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Diagnostics.Runtime;
using ProfileExplorer.Core.Session;
using ProfileExplorer.Core.Settings;
using ProfileExplorer.UI.Controls;
using ProfileExplorer.UI.Services;
using ProfileExplorer.UI.Windows;

namespace ProfileExplorer.UI.OptionsPanels;
public partial class FlowGraphOptionsPanelViewModel : OptionsPanelBaseViewModel<FlowGraphSettings> {
  [ObservableProperty]
  private bool syncSelectedNodes_;

  [ObservableProperty]
  private bool syncMarkedNodes_;

  [ObservableProperty]
  private bool showImmDominatorEdges_;

  [ObservableProperty]
  private bool bringNodesIntoView_;

  [ObservableProperty]
  private bool showPreviewPopup_;

  [ObservableProperty]
  private bool showPreviewPopupWithModifier_;

  [ObservableProperty]
  private bool colorizeNodes_;

  [ObservableProperty]
  private bool colorizeEdges_;

  [ObservableProperty]
  private bool highlightConnectedNodesOnHover_;

  [ObservableProperty]
  private bool highlightConnectedNodesOnSelection_;

  [ObservableProperty]
  private Color textColor_;

  [ObservableProperty]
  private Color backgroundColor_;

  [ObservableProperty]
  private Color nodeColor_;

  [ObservableProperty]
  private Color selectedNodeColor_;

  [ObservableProperty]
  private Color edgeColor_;

  [ObservableProperty]
  private Color nodeBorderColor_;

  [ObservableProperty]
  private Color predecessorNodeBorderColor_;

  [ObservableProperty]
  private Color successorNodeBorderColor_;

  [ObservableProperty]
  private Color emptyNodeColor_;

  [ObservableProperty]
  private Color branchNodeBorderColor_;

  [ObservableProperty]
  private Color switchNodeBorderColor_;

  [ObservableProperty]
  private Color returnNodeBorderColor_;

  [ObservableProperty]
  private Color loopNodeBorderColor_;

  [ObservableProperty]
  private Color dominatorEdgeColor_;

  [ObservableProperty]
  private bool markLoopBlocks_;

  [ObservableProperty]
  private Color loopNodeColor1_;

  [ObservableProperty]
  private Color loopNodeColor2_;

  [ObservableProperty]
  private Color loopNodeColor3_;

  [ObservableProperty]
  private Color loopNodeColor4_;

  public override void Initialize(FrameworkElement parent, FlowGraphSettings settings, IUISession session) {
    base.Initialize(parent, settings, session);

    // Populate view model properties from settings
    PopulateFromSettings(settings);
  }

  private void PopulateFromSettings(FlowGraphSettings settings) {
    SyncSelectedNodes_ = settings.SyncSelectedNodes;
    SyncMarkedNodes_ = settings.SyncMarkedNodes;
    ShowImmDominatorEdges_ = settings.ShowImmDominatorEdges;
    BringNodesIntoView_ = settings.BringNodesIntoView;
    ShowPreviewPopup_ = settings.ShowPreviewPopup;
    ShowPreviewPopupWithModifier_ = settings.ShowPreviewPopupWithModifier;
    ColorizeNodes_ = settings.ColorizeNodes;
    ColorizeEdges_ = settings.ColorizeEdges;
    HighlightConnectedNodesOnHover_ = settings.HighlightConnectedNodesOnHover;
    HighlightConnectedNodesOnSelection_ = settings.HighlightConnectedNodesOnSelection;
    TextColor_ = settings.TextColor;
    BackgroundColor_ = settings.BackgroundColor;
    NodeColor_ = settings.NodeColor;
    SelectedNodeColor_ = settings.SelectedNodeColor;
    EdgeColor_ = settings.EdgeColor;
    NodeBorderColor_ = settings.NodeBorderColor;
    PredecessorNodeBorderColor_ = settings.PredecessorNodeBorderColor;
    SuccessorNodeBorderColor_ = settings.SuccessorNodeBorderColor;
    EmptyNodeColor_ = settings.EmptyNodeColor;
    BranchNodeBorderColor_ = settings.BranchNodeBorderColor;
    SwitchNodeBorderColor_ = settings.SwitchNodeBorderColor;
    ReturnNodeBorderColor_ = settings.ReturnNodeBorderColor;
    LoopNodeBorderColor_ = settings.LoopNodeBorderColor;
    DominatorEdgeColor_ = settings.DominatorEdgeColor;
    MarkLoopBlocks_ = settings.MarkLoopBlocks;
    var loopColors = settings.LoopNodeColors;
    if (loopColors != null && loopColors.Length >= 4) {
      LoopNodeColor1_ = loopColors[0];
      LoopNodeColor2_ = loopColors[1];
      LoopNodeColor3_ = loopColors[2];
      LoopNodeColor4_ = loopColors[3];
    }
  }

  public override void SaveSettings() {
    if (Settings_ != null) {
      Settings_.SyncSelectedNodes = SyncSelectedNodes_;
      Settings_.SyncMarkedNodes = SyncMarkedNodes_;
      Settings_.ShowImmDominatorEdges = ShowImmDominatorEdges_;
      Settings_.BringNodesIntoView = BringNodesIntoView_;
      Settings_.ShowPreviewPopup = ShowPreviewPopup_;
      Settings_.ShowPreviewPopupWithModifier = ShowPreviewPopupWithModifier_;
      Settings_.ColorizeNodes = ColorizeNodes_;
      Settings_.ColorizeEdges = ColorizeEdges_;
      Settings_.HighlightConnectedNodesOnHover = HighlightConnectedNodesOnHover_;
      Settings_.HighlightConnectedNodesOnSelection = HighlightConnectedNodesOnSelection_;
      Settings_.TextColor = TextColor_;
      Settings_.BackgroundColor = BackgroundColor_;
      Settings_.NodeColor = NodeColor_;
      Settings_.SelectedNodeColor = SelectedNodeColor_;
      Settings_.EdgeColor = EdgeColor_;
      Settings_.NodeBorderColor = NodeBorderColor_;
      Settings_.PredecessorNodeBorderColor = PredecessorNodeBorderColor_;
      Settings_.SuccessorNodeBorderColor = SuccessorNodeBorderColor_;
      Settings_.EmptyNodeColor = EmptyNodeColor_;
      Settings_.BranchNodeBorderColor = BranchNodeBorderColor_;
      Settings_.SwitchNodeBorderColor = SwitchNodeBorderColor_;  
      Settings_.ReturnNodeBorderColor = ReturnNodeBorderColor_;
      Settings_.LoopNodeBorderColor = LoopNodeBorderColor_;
      Settings_.DominatorEdgeColor = DominatorEdgeColor_;
      Settings_.MarkLoopBlocks = MarkLoopBlocks_;
      Settings_.LoopNodeColors = new Color[] {
        LoopNodeColor1_, LoopNodeColor2_, LoopNodeColor3_, LoopNodeColor4_
      };
    }
  }

  /// <summary>
  /// Gets the current settings with all changes applied
  /// </summary>
  public FlowGraphSettings GetCurrentSettings() {
    SaveSettings(); // Ensure all changes are saved to the settings object
    return Settings_;
  }

  /// <summary>
  /// Resets the settings to default values
  /// </summary>
  public void ResetSettings() {
    if (Settings_ != null) {
      // Reset to defaults - create a new default settings object
      var defaultSettings = new FlowGraphSettings();
      Settings_ = defaultSettings;
      PopulateFromSettings(Settings_);
    }
  }

  /// <summary>
  /// Called when the panel is about to close
  /// </summary>
  public void PanelClosing() {
    // Ensure settings are saved before closing
    SaveSettings();
    // Any other cleanup can be added here
  }
}