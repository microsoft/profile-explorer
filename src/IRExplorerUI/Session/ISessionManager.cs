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

namespace IRExplorerUI {
    public enum DuplicatePanelKind {
        NewSetDockedLeft,
        NewSetDockedRight,
        SameSet,
        Floating
    }

    public interface ISessionManager {
        IRDocument CurrentDocument { get; }
        IRTextSection CurrentDocumentSection { get; }
        List<IRDocument> OpenDocuments { get; }
        ICompilerInfoProvider CompilerInfo { get; }
        SessionStateManager SessionState { get; }
        bool IsInDiffMode { get; }
        bool IsInTwoDocumentsDiffMode { get; }

        IRTextSummary MainDocumentSummary { get; }
        IRTextSummary DiffDocumentSummary { get; }

        IRTextSummary GetDocumentSummary(IRTextSection section);
        IRDocument FindAssociatedDocument(IToolPanel panel);
        IRDocumentHost FindAssociatedDocumentHost(IToolPanel panel);
        void BindToDocument(IToolPanel panel, BindMenuItem args);
        void DuplicatePanel(IToolPanel panel, DuplicatePanelKind duplicateKind);
        void ShowAllReferences(IRElement element, IRDocument document);
        void ShowSSAUses(IRElement element, IRDocument document);
        object LoadDocumentState(IRTextSection section);
        object LoadPanelState(IToolPanel panel, IRTextSection section);
        void PopulateBindMenu(IToolPanel panel, BindMenuItemsArgs args);
        void SaveDocumentState(object stateObject, IRTextSection section);
        void SavePanelState(object stateObject, IToolPanel panel, IRTextSection section);
        Task SwitchDocumentSection(OpenSectionEventArgs args, IRDocument document);
        bool SwitchToPreviousSection(IRTextSection section, IRDocument document);
        bool SwitchToNextSection(IRTextSection section, IRDocument document);
        void SetSectionAnnotationState(IRTextSection section, bool hasAnnotations);

        Task<string> GetSectionPassOutputAsync(IRPassOutput output, IRTextSection section);
        Task<string> GetSectionTextAsync(IRTextSection section, IRDocument targetDiffDocument = null);
        Task<string> GetDocumentTextAsync(IRTextSection section);

        Task SwitchGraphsAsync(GraphPanel flowGraphPanel, IRTextSection section, IRDocument document);

        Task<SectionSearchResult> SearchSectionAsync(SearchInfo searchInfo, IRTextSection section,
                                                     IRDocument document);

        void ReloadDocumentSettings(DocumentSettings newSettings, IRDocument document);
        void ReloadRemarkSettings(RemarkSettings newSettings, IRDocument document);

        void RegisterDetachedPanel(DraggablePopup panel);
        void UnregisterDetachedPanel(DraggablePopup panel);
        void LoadDocumentQuery(QueryDefinition query, IRDocument document);

        Task<bool> SaveSessionDocument(string filePath);
        Task<bool> OpenSessionDocument(string filePath);
    }
}
