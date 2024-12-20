// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ProfileExplorer.Core;
using ProfileExplorer.Core.Graph;
using ProfileExplorer.Core.IR;
using ProfileExplorer.UI.Binary;
using ProfileExplorer.UI.Compilers;
using ProfileExplorer.UI.Controls;
using ProfileExplorer.UI.Document;
using ProfileExplorer.UI.Profile;
using ProfileExplorer.UI.Query;

namespace ProfileExplorer.UI;

public enum DuplicatePanelKind {
  NewSetDockedLeft,
  NewSetDockedRight,
  SameSet,
  Floating
}

public interface ISession {
  IRDocument CurrentDocument { get; }
  IRTextSection CurrentDocumentSection { get; }
  List<IRDocument> OpenDocuments { get; }
  ICompilerInfoProvider CompilerInfo { get; }
  SessionStateManager SessionState { get; }
  bool IsSessionStarted { get; }
  bool IsInDiffMode { get; }
  bool IsInTwoDocumentsDiffMode { get; }
  bool IsInTwoDocumentsMode { get; }
  DiffModeInfo DiffModeInfo { get; }
  IRTextSummary MainDocumentSummary { get; }
  IRTextSummary DiffDocumentSummary { get; }
  ProfileData ProfileData { get; }
  ProfileFilterState ProfileFilter { get; set; }
  Task<bool> StartNewSession(string sessionName, SessionKind sessionKind, ICompilerInfoProvider compilerInfo);
  Task<bool> SetupNewSession(LoadedDocument mainDocument, List<LoadedDocument> otherDocuments, ProfileData profileData);
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
  Task<LoadedDocument> OpenSessionDocument(string filePath);

  Task<LoadedDocument> LoadProfileBinaryDocument(string filePath, string modulePath,
                                                 IDebugInfoProvider debugInfo = null);

  Task<IDebugInfoProvider> GetDebugInfoProvider(IRTextFunction function);

  //? TODO: Extract into an IProfilingSession, connecting all profile panels
  //? and sending the activity/timeline events to all of them.
  Task<bool> LoadProfileData(string profileFilePath, List<int> processIds,
                             ProfileDataProviderOptions options,
                             SymbolFileSourceSettings symbolSettings,
                             ProfileDataReport report,
                             ProfileLoadProgressHandler progressCallback,
                             CancelableTask cancelableTask);

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