// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Windows.Media;
using ProfileExplorer.UI.Profile;
using ProtoBuf;

namespace ProfileExplorer.UI;

[ProtoContract(SkipConstructor = true)]
public class TimelineSettings : SettingsBase {
  public static readonly int DefaultCallStackPopupDuration = (int)HoverPreview.ExtraLongHoverDuration.TotalMilliseconds;

  public TimelineSettings() {
    Reset();
  }

  [ProtoMember(1)][OptionValue(true)]
  public bool SyncSelection { get; set; }
  [ProtoMember(2)][OptionValue(true)]
  public bool ShowCallStackPopup { get; set; }
  [ProtoMember(3)][OptionValue(0)]
  public int CallStackPopupDuration { get; set; }
  [ProtoMember(4)][OptionValue(false)]
  public bool GroupThreads { get; set; }
  [ProtoMember(5)][OptionValue(true)]
  public bool UseThreadColors { get; set; }

  public (Brush Margin, Brush Samples)
    GetThreadBackgroundColors(ProfileThread threadInfo, int threadId) {
    if (!UseThreadColors) {
      return (Brushes.WhiteSmoke, Brushes.LightSkyBlue);
    }

    // Set thread color based on name, if available,
    // otherwise on the thread ID. The picked color is stable
    // between different sessions loading the same trace.
    uint colorIndex = 0;

    if (threadInfo != null && threadInfo.HasName) {
      colorIndex = (uint)threadInfo.Name.GetStableHashCode();
    }
    else {
      colorIndex = (uint)threadId;
    }

    return (ColorBrushes.GetBrush(ColorUtils.GenerateLightPastelColor(colorIndex)),
            ColorBrushes.GetBrush(ColorUtils.GeneratePastelColor(colorIndex)));
  }

  public override void Reset() {
    ResetAllOptions(this);
    CallStackPopupDuration = DefaultCallStackPopupDuration;
  }

  public TimelineSettings Clone() {
    byte[] serialized = StateSerializer.Serialize(this);
    return StateSerializer.Deserialize<TimelineSettings>(serialized);
  }

  public override bool Equals(object obj) {
    return AreOptionsEqual(this, obj);
  }

  public override string ToString() {
    return PrintOptions(this);
  }
}