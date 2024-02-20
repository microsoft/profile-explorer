// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using ProtoBuf;

namespace IRExplorerUI;

[ProtoContract(SkipConstructor = true)]
public class TimelineSettings : SettingsBase {
  public TimelineSettings() {
    Reset();
  }

  //? TODO: Options for
  //? - grouping by thread name
  //? - custom colors for thread names
  //? - auto-pick colors by name + select palette
  //? - show backtrace preview on hover
  //?    - max depth
  //?    - hover time
  //?    - same settings in SectionPanel
  public static readonly int DefaultCallStackPopupDuration = (int)HoverPreview.ExtraLongHoverDuration.TotalMilliseconds;

  [ProtoMember(1)] public bool SyncSelection { get; set; }
  [ProtoMember(2)] public bool ShowCallStackPopup { get; set; }
  [ProtoMember(3)] public int CallStackPopupDuration { get; set; }
  [ProtoMember(4)] public bool GroupThreads { get; set; }

  public override void Reset() {
    SyncSelection = true;
    ShowCallStackPopup = true;
    CallStackPopupDuration = DefaultCallStackPopupDuration;
  }

  public TimelineSettings Clone() {
    byte[] serialized = StateSerializer.Serialize(this);
    return StateSerializer.Deserialize<TimelineSettings>(serialized);
  }

  public override bool Equals(object obj) {
    return obj is TimelineSettings settings &&
           SyncSelection == settings.SyncSelection &&
           ShowCallStackPopup == settings.ShowCallStackPopup &&
           CallStackPopupDuration == settings.CallStackPopupDuration &&
           GroupThreads == settings.GroupThreads;
  }

  public override string ToString() {
      return $"SyncSelection: {SyncSelection}\n" +
              $"ShowCallStackPopup: {ShowCallStackPopup}\n" +
              $"CallStackPopupDuration: {CallStackPopupDuration}\n" +
              $"GroupThreads: {GroupThreads}";
  }
}