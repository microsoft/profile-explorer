// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading.Tasks;
using Core;
using Core.Analysis;
using Core.IR;

namespace Client {
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
        ICompilerIRInfoProvider CompilerInfo { get; }
        SessionStateManager SessionState { get; }

        IRTextSummary GetDocumentSummary(IRTextSection section);
        IRDocument FindAssociatedDocument(IToolPanel panel);
        IRDocumentHost FindAssociatedDocumentHost(IToolPanel panel);
        void BindToDocument(IToolPanel panel, BindMenuItem args);
        void DuplicatePanel(IToolPanel panel, DuplicatePanelKind duplicateKind);
        List<Reference> FindAllReferences(IRElement element, IRDocument document);
        List<Reference> FindSSAUses(IRElement element, IRDocument document);
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
        Task SwitchGraphsAsync(GraphPanel flowGraphPanel, IRTextSection section, IRDocument document);
        Task<SectionSearchResult> SearchSectionAsync(Document.SearchInfo searchInfo,
                                                     IRTextSection section, IRDocument document);
        void ReloadDocumentSettings(DocumentSettings newSettings, IRDocument document);
    }
}
