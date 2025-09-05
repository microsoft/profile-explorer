// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using ProfileExplorer.Core;
using ProfileExplorer.Core.Session;
using ProfileExplorer.UI.Profile;
using ProfileExplorer.UI;
using ProtoBuf;

namespace ProfileExplorerUI.Session;

public class PanelObjectPair {
  public PanelObjectPair(IToolPanel panel, object stateObject) {
    Panel = panel;
    StateObject = stateObject;
  }

  public IToolPanel Panel { get; set; }
  public object StateObject { get; set; }
}

public class PanelStateManager {
  // Maps document ID -> section ID -> list of panel states
  private Dictionary<Guid, Dictionary<int, List<PanelObjectPair>>> documentPanelStates_;
  
  // Maps section group (for diff mode) -> list of panel states  
  private Dictionary<BaseDiffSectionGroup, List<PanelObjectPair>> diffPanelStates_;

  public PanelStateManager() {
    documentPanelStates_ = new Dictionary<Guid, Dictionary<int, List<PanelObjectPair>>>();
    diffPanelStates_ = new Dictionary<BaseDiffSectionGroup, List<PanelObjectPair>>();
  }

  public void SavePanelState(object stateObject, IToolPanel panel, ILoadedDocument document, IRTextSection section) {
    if (document == null || section == null) {
      return;
    }

    var documentId = document.Id;
    var sectionId = section.Id;

    if (!documentPanelStates_.TryGetValue(documentId, out var sectionMap)) {
      sectionMap = new Dictionary<int, List<PanelObjectPair>>();
      documentPanelStates_[documentId] = sectionMap;
    }

    if (!sectionMap.TryGetValue(sectionId, out var list)) {
      list = new List<PanelObjectPair>();
      sectionMap[sectionId] = list;
    }

    var state = list.Find(item => item.Panel == panel);
    if (state != null) {
      state.StateObject = stateObject;
    }
    else {
      list.Add(new PanelObjectPair(panel, stateObject));
    }
  }

  public object LoadPanelState(IToolPanel panel, ILoadedDocument document, IRTextSection section) {
    if (document == null || section == null) {
      return null;
    }

    var documentId = document.Id;
    var sectionId = section.Id;

    if (documentPanelStates_.TryGetValue(documentId, out var sectionMap) &&
        sectionMap.TryGetValue(sectionId, out var list)) {
      var state = list.Find(item => item.Panel == panel);
      return state?.StateObject;
    }

    return null;
  }

  public void SaveDiffModePanelState(object stateObject, IToolPanel panel, IRTextSection section) {
    if (section == null) {
      return;
    }

    // Create a diff section group - this requires the base section, diff section, and state section
    // For now, we'll use the section as all three (this may need adjustment based on actual diff mode logic)
    var group = new BaseDiffSectionGroup(section, section, section);

    if (!diffPanelStates_.TryGetValue(group, out var list)) {
      list = new List<PanelObjectPair>();
      diffPanelStates_[group] = list;
    }

    var state = list.Find(item => item.Panel == panel);
    if (state != null) {
      state.StateObject = stateObject;
    }
    else {
      list.Add(new PanelObjectPair(panel, stateObject));
    }
  }

  public object LoadDiffModePanelState(IToolPanel panel, IRTextSection section) {
    if (section == null) {
      return null;
    }

    var group = new BaseDiffSectionGroup(section, section, section);
    if (diffPanelStates_.TryGetValue(group, out var list)) {
      var state = list.Find(item => item.Panel == panel);
      return state?.StateObject;
    }

    return null;
  }

  public void ClearDiffModePanelState() {
    diffPanelStates_.Clear();
  }

  public List<Tuple<int, PanelObjectPairState>> SerializePanelStatesForDocument(ILoadedDocument document) {
    var result = new List<Tuple<int, PanelObjectPairState>>();
    
    if (!documentPanelStates_.TryGetValue(document.Id, out var sectionMap)) {
      return result;
    }

    foreach (var sectionEntry in sectionMap) {
      var sectionId = sectionEntry.Key;
      var panelStates = sectionEntry.Value;

      foreach (var panelState in panelStates) {
        if (panelState.Panel.SavesStateToFile) {
          result.Add(new Tuple<int, PanelObjectPairState>(
            sectionId,
            new PanelObjectPairState(panelState.Panel.PanelKind, panelState.StateObject)));
        }
      }
    }

    return result;
  }

  public void LoadPanelStatesForDocument(ILoadedDocument document, List<Tuple<int, PanelObjectPairState>> panelStates) {
    // This method would be called during session restoration
    // It requires access to panel instances, so it's implemented in SessionStateManager
  }

  public void RemoveDocumentPanelStates(Guid documentId) {
    documentPanelStates_.Remove(documentId);
  }
}

public class BaseDiffSectionGroup {
  public BaseDiffSectionGroup(IRTextSection baseSection,
                              IRTextSection diffSection,
                              IRTextSection stateSection) {
    BaseSection = baseSection;
    DiffSection = diffSection;
    StateSection = stateSection;
  }

  public IRTextSection BaseSection { get; set; }
  public IRTextSection DiffSection { get; set; }
  public IRTextSection StateSection { get; set; }

  public override bool Equals(object obj) {
    if (obj is BaseDiffSectionGroup other) {
      return BaseSection?.Id == other.BaseSection?.Id &&
             DiffSection?.Id == other.DiffSection?.Id &&
             StateSection?.Id == other.StateSection?.Id;
    }
    return false;
  }

  public override int GetHashCode() {
    return HashCode.Combine(BaseSection?.Id, DiffSection?.Id, StateSection?.Id);
  }
}
