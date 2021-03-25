// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading.Tasks;
using IRExplorerUI.Document;
using IRExplorerCore;
using IRExplorerCore.Analysis;
using IRExplorerCore.IR;
using IRExplorerUI.Controls;
using IRExplorerUI.Query;
using IRExplorerCore.Graph;
using IRExplorerUI.Profile;
using IRExplorerUI.Utilities;

namespace IRExplorerUI {
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
        bool IsInDiffMode { get; }
        bool IsInTwoDocumentsDiffMode { get; }
        DiffModeInfo DiffModeInfo { get; }

        IRTextSummary MainDocumentSummary { get; }
        IRTextSummary DiffDocumentSummary { get; }
        ProfileData ProfileData { get; }

        IRTextSummary GetDocumentSummary(IRTextSection section);
        IRDocument FindAssociatedDocument(IToolPanel panel);
        IRDocumentHost FindAssociatedDocumentHost(IToolPanel panel);
        void PopulateBindMenu(IToolPanel panel, BindMenuItemsArgs args);
        void BindToDocument(IToolPanel panel, BindMenuItem args);
        void DuplicatePanel(IToolPanel panel, DuplicatePanelKind duplicateKind);
        void ShowAllReferences(IRElement element, IRDocument document);
        void ShowSSAUses(IRElement element, IRDocument document);
        object LoadDocumentState(IRTextSection section);
        object LoadPanelState(IToolPanel panel, IRTextSection section, IRDocument document = null);
        void SaveDocumentState(object stateObject, IRTextSection section);
        void SavePanelState(object stateObject, IToolPanel panel, 
                            IRTextSection section, IRDocument document = null);
        Task SwitchDocumentSectionAsync(OpenSectionEventArgs args, IRDocument document);
        bool SwitchToPreviousSection(IRTextSection section, IRDocument document);
        bool SwitchToNextSection(IRTextSection section, IRDocument document);
        void SetSectionAnnotationState(IRTextSection section, bool hasAnnotations);

        IRTextSection GetPreviousSection(IRTextSection section);
        IRTextSection GetNextSection(IRTextSection section);
        ParsedIRTextSection LoadAndParseSection(IRTextSection section);
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

        void ReloadDocumentSettings(DocumentSettings newSettings, IRDocument document);
        void ReloadRemarkSettings(RemarkSettings newSettings, IRDocument document);

        void RegisterDetachedPanel(DraggablePopup panel);
        void UnregisterDetachedPanel(DraggablePopup panel);

        Task<bool> SaveSessionDocument(string filePath);
        Task<bool> OpenSessionDocument(string filePath);

        Task<bool> LoadProfileData(string profileFilePath, string binaryFilePath, string debugFilePath,
                                ProfileLoadProgressHandler progressCallback,
                                CancelableTask cancelableTask);

        bool SaveFunctionTaskOptions(FunctionTaskInfo taskInfo, IFunctionTaskOptions options);
        IFunctionTaskOptions LoadFunctionTaskOptions(FunctionTaskInfo taskInfo);
        void SetApplicationStatus(string text, string tooltip = "");
    }
}
