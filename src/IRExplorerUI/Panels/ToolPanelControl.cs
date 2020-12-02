// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Windows.Controls;
using IRExplorerCore;

namespace IRExplorerUI {
    public class ToolPanelControl : UserControl, IToolPanel {
        public virtual ToolPanelKind PanelKind => ToolPanelKind.Other;
        public virtual string TitlePrefix { get; set; }
        public virtual string TitleSuffix { get; set; }
        public virtual HandledEventKind HandledEvents => HandledEventKind.None;
        public virtual IRDocument Document { get; set; }
        public virtual IRDocument BoundDocument { get; set; }
        public virtual ISession Session { get; set; }

        public virtual bool SavesStateToFile => false;
        public virtual bool IsPanelEnabled { get; set; }
        public virtual bool HasCommandFocus { get; set; }
        public virtual bool HasPinnedContent { get; set; }
        public virtual bool IgnoreNextLoadEvent { get; set; }
        public virtual bool IgnoreNextUnloadEvent { get; set; }

        public virtual void OnRegisterPanel() {
            IsPanelEnabled = true;
        }

        public virtual void OnUnregisterPanel() { }

        public virtual void OnElementSelected(IRElementEventArgs e) { }

        public virtual void OnElementHighlighted(IRHighlightingEventArgs e) { }

        public virtual void OnDocumentSectionLoaded(IRTextSection section, IRDocument document) {
            Document = Document;
        }

        public virtual void OnDocumentSectionUnloaded(IRTextSection section, IRDocument document) {
            Document = null;
        }

        public virtual void OnActivatePanel() { }

        public virtual void OnDeactivatePanel() { }

        public virtual void OnHidePanel() { }

        public virtual void OnShowPanel() { }

        public virtual void ClonePanel(IToolPanel basePanel) { }

        public virtual void OnSessionStart() {
            Utils.EnableControl(this);
        }

        public virtual void OnSessionSave() { }

        public virtual void OnSessionEnd() {
            Document = null;
            Utils.DisableControl(this, 0.75);
        }

        public virtual void OnDocumentLoaded(IRDocument document) { }
    }
}
