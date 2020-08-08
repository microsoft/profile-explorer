// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using IRExplorerCore.IR;

namespace IRExplorerUI {
    public enum DocumentActionKind {
        SelectElement,
        MarkElement,
        MarkBlock,
        GoToDefinition,
        ShowReferences,
        MarkReferences,
        ShowUses,
        MarkUses,
        MarkExpression,
        ClearMarker,
        ClearAllMarkers,
        ClearBlockMarkers,
        ClearInstructionMarkers,
        ClearTemporaryMarkers,
        UndoAction,
        VerticalScroll,
        ShowExpressionGraph
    }

    public class DocumentAction {
        public DocumentAction(DocumentActionKind actionKind, IRElement element = null,
                              object optionalData = null) {
            ActionKind = actionKind;
            Element = element;
            OptionalData = optionalData;
        }

        public DocumentActionKind ActionKind { get; set; }
        public IRElement Element { get; set; }
        public object OptionalData { get; set; }

        public DocumentAction WithNewElement(IRElement newElement) {
            return new DocumentAction(ActionKind, newElement, OptionalData);
        }

        public override string ToString() {
            return $"action: {ActionKind}, element: {Element}";
        }
    }

    public class MarkActionData {
        public bool IsTemporary { get; set; }
        public PairHighlightingStyle Style { get; set; }
    }

    public class ReversibleDocumentAction {
        public ReversibleDocumentAction(DocumentAction action, Action<DocumentAction> undoAction) {
            Action = action;
            UndoAction = undoAction;
        }

        private DocumentAction Action { get; set; }
        public Action<DocumentAction> UndoAction { get; set; }

        public void Undo() {
            UndoAction?.Invoke(Action);
        }

        public override string ToString() {
            return Action.ToString();
        }
    }
}
