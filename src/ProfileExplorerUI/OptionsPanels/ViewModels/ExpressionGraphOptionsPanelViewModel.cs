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
public partial class ExpressionGraphOptionsPanelViewModel : OptionsPanelBaseViewModel<ExpressionGraphSettings> {
  [ObservableProperty]
  private bool printVariableNames_;

  [ObservableProperty]
  private bool printSSANumbers_;

  [ObservableProperty]
  private bool skipCopyInstructions_;

  [ObservableProperty]
  private bool groupInstructions_;

  [ObservableProperty]
  private bool printBottomUp_;

  [ObservableProperty]
  private int maxExpressionDepth_;

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
  private Color nodeBorderColor_;

  [ObservableProperty]
  private Color predecessorNodeBorderColor_;

  [ObservableProperty]
  private Color successorNodeBorderColor_;

  [ObservableProperty]
  private Color edgeColor_;

  [ObservableProperty]
  private Color unaryInstructionNodeColor_;

  [ObservableProperty]
  private Color binaryInstructionNodeColor_;

  [ObservableProperty]
  private Color copyInstructionNodeColor_;

  [ObservableProperty]
  private Color loadStoreInstructionNodeColor_;

  [ObservableProperty]
  private Color callInstructionNodeColor_;

  [ObservableProperty]
  private Color operandNodeColor_;

  [ObservableProperty]
  private Color numberOperandNodeColor_;

  [ObservableProperty]
  private Color indirectionOperandNodeColor_;

  [ObservableProperty]
  private Color addressOperandNodeColor_;

  [ObservableProperty]
  private Color loopPhiBackedgeColor_;

  public override void Initialize(FrameworkElement parent, ExpressionGraphSettings settings, IUISession session) {
    base.Initialize(parent, settings, session);

    // Populate view model properties from settings
    PopulateFromSettings(settings);
  }

  private void PopulateFromSettings(ExpressionGraphSettings settings) {
    PrintVariableNames_ = settings.PrintVariableNames;
    PrintSSANumbers_ = settings.PrintSSANumbers;
    SkipCopyInstructions_ = settings.SkipCopyInstructions;
    GroupInstructions_ = settings.GroupInstructions;
    PrintBottomUp_ = settings.PrintBottomUp;
    MaxExpressionDepth_ = settings.MaxExpressionDepth;
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
    NodeBorderColor_ = settings.NodeBorderColor;
    PredecessorNodeBorderColor_ = settings.PredecessorNodeBorderColor;
    SuccessorNodeBorderColor_ = settings.SuccessorNodeBorderColor;
    EdgeColor_ = settings.EdgeColor;
    UnaryInstructionNodeColor_ = settings.UnaryInstructionNodeColor;
    BinaryInstructionNodeColor_ = settings.BinaryInstructionNodeColor;
    CopyInstructionNodeColor_ = settings.CopyInstructionNodeColor;
    LoadStoreInstructionNodeColor_ = settings.LoadStoreInstructionNodeColor;
    CallInstructionNodeColor_ = settings.CallInstructionNodeColor;
    OperandNodeColor_ = settings.OperandNodeColor;
    NumberOperandNodeColor_ = settings.NumberOperandNodeColor;
    IndirectionOperandNodeColor_ = settings.IndirectionOperandNodeColor;
    AddressOperandNodeColor_ = settings.AddressOperandNodeColor;
    LoopPhiBackedgeColor_ = settings.LoopPhiBackedgeColor;
  }

  [RelayCommand]
  private void SetLongCallStackPopupDuration() {

  }

  public override void SaveSettings() {
    if (Settings_ != null) {
      Settings_.PrintVariableNames = PrintVariableNames_;
      Settings_.PrintSSANumbers = PrintSSANumbers_;
      Settings_.SkipCopyInstructions = SkipCopyInstructions_;
      Settings_.GroupInstructions = GroupInstructions_;
      Settings_.PrintBottomUp = PrintBottomUp_;
      Settings_.MaxExpressionDepth = MaxExpressionDepth_;
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
      Settings_.NodeBorderColor = NodeBorderColor_;
      Settings_.PredecessorNodeBorderColor = PredecessorNodeBorderColor_;
      Settings_.SuccessorNodeBorderColor = SuccessorNodeBorderColor_;
      Settings_.EdgeColor = EdgeColor_;
      Settings_.UnaryInstructionNodeColor = UnaryInstructionNodeColor_;
      Settings_.BinaryInstructionNodeColor = BinaryInstructionNodeColor_;
      Settings_.CopyInstructionNodeColor = CopyInstructionNodeColor_;
      Settings_.LoadStoreInstructionNodeColor = LoadStoreInstructionNodeColor_;
      Settings_.CallInstructionNodeColor = CallInstructionNodeColor_;
      Settings_.OperandNodeColor = OperandNodeColor_;
      Settings_.NumberOperandNodeColor = NumberOperandNodeColor_;
      Settings_.IndirectionOperandNodeColor = IndirectionOperandNodeColor_;
      Settings_.AddressOperandNodeColor = AddressOperandNodeColor_;
      Settings_.LoopPhiBackedgeColor = LoopPhiBackedgeColor_;
    }
  }

  /// <summary>
  /// Gets the current settings with all changes applied
  /// </summary>
  public ExpressionGraphSettings GetCurrentSettings() {
    SaveSettings(); // Ensure all changes are saved to the settings object
    return Settings_;
  }

  /// <summary>
  /// Resets the settings to default values
  /// </summary>
  public void ResetSettings() {
    if (Settings_ != null) {
      // Reset to defaults - create a new default settings object
      var defaultSettings = new ExpressionGraphSettings();
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