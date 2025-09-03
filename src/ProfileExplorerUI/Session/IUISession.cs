// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ProfileExplorer.Core;
using ProfileExplorer.Core.Graph;
using ProfileExplorer.Core.IR;
using ProfileExplorer.Core.Utilities;
using ProfileExplorer.Core.Session;
using ProfileExplorer.Core.Profile.Data;
using ProfileExplorer.Core.Profile.Processing;
using ProfileExplorer.Core.Profile.CallTree;
using ProfileExplorer.UI.Controls;
using ProfileExplorer.Core.Binary;
using ProfileExplorer.Core.Settings;
using ProfileExplorer.Core.Profile.Timeline;
using ProfileExplorer.UI.Query;
using ProfileExplorer.UI.Document;
using ProfileExplorer.Core.Providers;
using ProfileExplorerUI.Session;
using ProfileExplorer.UI.Providers;

namespace ProfileExplorer.UI;

public enum DuplicatePanelKind {
  NewSetDockedLeft,
  NewSetDockedRight,
  SameSet,
  Floating
}

public interface IUISession : ISession {
  ISectionStyleProvider SectionStyleProvider { get; }
  IRRemarkProvider RemarkProvider { get; }
  IBlockFoldingStrategyProvider BlockFoldingStrategyProvider { get; }
  ILoadedSectionHandler LoadedSectionHandler { get; }
  IRDocument CurrentDocument { get; }
  IRTextSection CurrentDocumentSection { get; }
  List<IRDocument> OpenDocuments { get; }
  SessionStateManager SessionState { get; }
  bool IsSessionStarted { get; }
  bool IsInDiffMode { get; }
  bool IsInTwoDocumentsDiffMode { get; }
  bool IsInTwoDocumentsMode { get; }
  DiffModeInfo DiffModeInfo { get; }
  IRTextSummary MainDocumentSummary { get; }
  IRTextSummary DiffDocumentSummary { get; }
  ProfileFilterState ProfileFilter { get; set; }
  IRTextSummary GetDocumentSummary(IRTextSection section);
  IRTextFunction FindFunctionWithId(int funcNumber, Guid summaryId);
  IRDocument FindAssociatedDocument(IToolPanel panel);
  IRDocumentHost FindAssociatedDocumentHost(IToolPanel panel);
  void PopulateBindMenu(IToolPanel panel, BindMenuItemsArgs args);
  void BindToDocument(IToolPanel panel, BindMenuItem args);
  void DuplicatePanel(IToolPanel panel, DuplicatePanelKind duplicateKind);
  void DisplayFloatingPanel(IToolPanel panel);
  void ShowAllReferences(IRElement element, IRDocument document);
  void ShowSSAUses(IRElement element, IRDocument document);
  object LoadDocumentState(IRTextSection section);
  object LoadPanelState(IToolPanel panel, IRTextSection section, IRDocument document = null);
  void SaveDocumentState(object stateObject, IRTextSection section);

  void SavePanelState(object stateObject, IToolPanel panel,
                      IRTextSection section, IRDocument document = null);

  void RedrawPanels(params ToolPanelKind[] kinds);
  IToolPanel FindPanel(ToolPanelKind kind);
  Task<IToolPanel> ShowPanel(ToolPanelKind kind);
  void ActivatePanel(IToolPanel panel);
  Task<IRDocumentHost> SwitchDocumentSectionAsync(OpenSectionEventArgs args);
  Task<IRDocumentHost> OpenDocumentSectionAsync(OpenSectionEventArgs args);
  bool SwitchToPreviousSection(IRTextSection section, IRDocument document);
  bool SwitchToNextSection(IRTextSection section, IRDocument document);
  void SetSectionAnnotationState(IRTextSection section, bool hasAnnotations);
  IRTextSection GetPreviousSection(IRTextSection section);
  IRTextSection GetNextSection(IRTextSection section);
  Task<ParsedIRTextSection> LoadAndParseSection(IRTextSection section);
  Task<string> GetSectionOutputTextAsync(IRPassOutput output, IRTextSection section);
  Task<List<string>> GetSectionOutputTextLinesAsync(IRPassOutput output, IRTextSection section);
  Task<string> GetSectionTextAsync(IRTextSection section, IRDocument targetDiffDocument = null);
  Task<string> GetDocumentTextAsync(IRTextSummary summary);
  Task SwitchGraphsAsync(GraphPanel flowGraphPanel, IRTextSection section, IRDocument document);

  Task<Graph> ComputeGraphAsync(GraphKind kind, IRTextSection section,
                                IRDocument document, CancelableTask loadTask = null,
                                object options = null);

  Task<SectionSearchResult> SearchSectionAsync(SearchInfo searchInfo, IRTextSection section,
                                               IRDocument document);

  Task SwitchActiveFunction(IRTextFunction function, bool handleProfiling = true);
  Task ReloadDocumentSettings(DocumentSettings newSettings, IRDocument document);
  Task ReloadRemarkSettings(RemarkSettings newSettings, IRDocument document);
  Task ReloadSettings();
  void RegisterDetachedPanel(DraggablePopup panel);
  void UnregisterDetachedPanel(DraggablePopup panel);
  Task<bool> SaveSessionDocument(string filePath);
  Task<ILoadedDocument> OpenSessionDocument(string filePath);


  Task<IDebugInfoProvider> GetDebugInfoProvider(IRTextFunction function);

  Task<bool> LoadProfileData(RawProfileData data, List<int> processIds,
                             ProfileDataProviderOptions options,
                             SymbolFileSourceSettings symbolSettings,
                             ProfileDataReport report,
                             ProfileLoadProgressHandler progressCallback,
                             CancelableTask cancelableTask);

  Task<bool> FilterProfileSamples(ProfileFilterState filter);
  Task<bool> RemoveProfileSamplesFilter();

  Task<bool> OpenProfileFunction(ProfileCallTreeNode node, OpenSectionKind openMode,
                                 ProfileSampleFilter instanceFilter = null,
                                 IRDocumentHost targetDocument = null);

  Task<bool> OpenProfileFunction(IRTextFunction function, OpenSectionKind openMode,
                                 ProfileSampleFilter instanceFilter = null,
                                 IRDocumentHost targetDocument = null);

  Task<bool> SwitchActiveProfileFunction(ProfileCallTreeNode node);
  Task<bool> SelectProfileFunctionInPanel(ProfileCallTreeNode node, ToolPanelKind panelKind);
  Task<bool> SelectProfileFunctionInPanel(IRTextFunction node, ToolPanelKind panelKind);
  Task<bool> OpenProfileSourceFile(ProfileCallTreeNode node, ProfileSampleFilter profileFilter = null);
  Task<bool> OpenProfileSourceFile(IRTextFunction function, ProfileSampleFilter profileFilter = null);
  Task<bool> ProfileSampleRangeSelected(SampleTimeRangeInfo range);
  Task<bool> ProfileSampleRangeDeselected();
  Task<bool> ProfileFunctionSelected(ProfileCallTreeNode node, ToolPanelKind sourcePanelKind);

  Task<bool> MarkProfileFunction(ProfileCallTreeNode node, ToolPanelKind sourcePanelKind,
                                 HighlightingStyle style);

  Task<bool> ProfileFunctionSelected(IRTextFunction function, ToolPanelKind sourcePanelKind);
  Task<bool> ProfileFunctionDeselected();
  Task<bool> FunctionMarkingChanged(ToolPanelKind sourcePanelKind);
  bool SaveFunctionTaskOptions(FunctionTaskInfo taskInfo, IFunctionTaskOptions options);
  IFunctionTaskOptions LoadFunctionTaskOptions(FunctionTaskInfo taskInfo);
  void SetApplicationStatus(string text, string tooltip = "");
  void SetApplicationProgress(bool visible, double percentage, string title = null);
  void UpdatePanelTitles();
  void UpdateDocumentTitles();
}