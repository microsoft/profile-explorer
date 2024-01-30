// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using ProtoBuf;

namespace IRExplorerUI;

[ProtoContract(SkipConstructor = true)]
//? TODO: options ofr
//? - show node preview on hover
//?      - hover time
//? - color list item based on module name
public class CallTreeSettings : SettingsBase {
  public static readonly int DefaultNodePopupDuration = (int)HoverPreview.HoverDuration.TotalMilliseconds;

  public CallTreeSettings() {
    Reset();
  }

  [ProtoMember(1)]
  public bool CombineInstances { get; set; }
  [ProtoMember(2)]
  public bool PrependModuleToFunction { get; set; }
  [ProtoMember(3)]
  public bool ShowTimeAfterPercentage { get; set; }
  [ProtoMember(4)]
  public bool ShowDetailsPanel { get; set; }
  [ProtoMember(5)]
  public bool SyncSourceFile { get; set; }
  [ProtoMember(6)]
  public bool SyncSelection { get; set; }
  [ProtoMember(7)]
  public bool ShowNodePopup { get; set; }
  [ProtoMember(8)]
  public int NodePopupDuration { get; set; }

  public override void Reset() {
    CombineInstances = true;
    PrependModuleToFunction = true;
    ShowTimeAfterPercentage = true;
    SyncSelection = true;
    ShowNodePopup = true;
    NodePopupDuration = DefaultNodePopupDuration;
  }

  public CallTreeSettings Clone() {
    byte[] serialized = StateSerializer.Serialize(this);
    return StateSerializer.Deserialize<CallTreeSettings>(serialized);
  }

  public override bool Equals(object obj) {
    return obj is CallTreeSettings settings &&
           CombineInstances == settings.CombineInstances &&
           PrependModuleToFunction == settings.PrependModuleToFunction &&
           ShowTimeAfterPercentage == settings.ShowTimeAfterPercentage &&
           ShowDetailsPanel == settings.ShowDetailsPanel &&
           SyncSourceFile == settings.SyncSourceFile &&
           SyncSelection == settings.SyncSelection &&
           ShowNodePopup == settings.ShowNodePopup &&
           NodePopupDuration == settings.NodePopupDuration;
  }
}