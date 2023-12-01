// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using IRExplorerCore;

namespace IRExplorerUI;

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
  CallTree,
  CallerCallee,
  FlameGraph,
  Timeline,
  PassOutput,
  SearchResults,
  Remarks,
  Scripting,
  Developer,
  Help,
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
  string TitlePrefix { get; }
  string TitleSuffix { get; }
  string TitleToolTip { get; }
  HandledEventKind HandledEvents { get; }
  bool SavesStateToFile { get; }
  bool IsUnloaded { get; }
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
  void OnRedrawPanel();
  void OnSessionStart();
  void OnSessionEnd();
  void OnSessionSave();
  void OnDocumentSectionLoaded(IRTextSection section, IRDocument document);
  void OnDocumentSectionUnloaded(IRTextSection section, IRDocument document);
  void OnElementSelected(IRElementEventArgs e);
  void OnElementHighlighted(IRHighlightingEventArgs e);
  void ClonePanel(IToolPanel basePanel);
}
