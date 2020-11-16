// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using IRExplorerCore;

namespace IRExplorerUI {
    public enum ToolPanelKind {
        Section,
        References,
        Notes,
        Definition,
        Bookmarks,
        Source,
        FlowGraph,
        DominatorTree,
        PostDominatorTree,
        ExpressionGraph,
        CallGraph,
        PassOutput,
        SearchResults,
        Remarks,
        Scripting,
        Developer,
        Other
    }

    [Flags]
    public enum HandledEventKind {
        None,
        ElementSelection,
        ElementHighlighting
    }

    public interface IToolPanel {
        ToolPanelKind PanelKind { get; }
        HandledEventKind HandledEvents { get; }
        bool SavesStateToFile { get; }

        ISession Session { get; set; }
        IRDocument Document { get; set; }
        IRDocument BoundDocument { get; set; }
        bool IsPanelEnabled { get; set; }
        bool HasCommandFocus { get; set; }
        bool HasPinnedContent { get; set; }
        bool IgnoreNextLoadEvent { get; set; }
        bool IgnoreNextUnloadEvent { get; set; }

        void OnRegisterPanel();
        void OnUnregisterPanel();

        void OnShowPanel();
        void OnHidePanel();
        void OnActivatePanel();
        void OnDeactivatePanel();

        void OnSessionStart();
        void OnSessionEnd();
        void OnSessionSave();

        void OnDocumentSectionLoaded(IRTextSection section, IRDocument document);
        void OnDocumentSectionUnloaded(IRTextSection section, IRDocument document);

        void OnElementSelected(IRElementEventArgs e);
        void OnElementHighlighted(IRHighlightingEventArgs e);

        void ClonePanel(IToolPanel basePanel);
    }
}
