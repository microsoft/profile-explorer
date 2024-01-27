// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System.Windows.Media;
using ProtoBuf;

namespace IRExplorerUI;

[ProtoContract(SkipConstructor = true)]
public class FlameGraphSettings : SettingsBase {
  public static readonly int DefaultNodePopupDuration = HoverPreview.HoverDuration.Milliseconds;

  public FlameGraphSettings() {
    Reset();
  }

  [ProtoMember(1)]
  public bool PrependModuleToFunction { get; set; }
  [ProtoMember(2)]
  public bool ShowDetailsPanel { get; set; }
  [ProtoMember(3)]
  public bool SyncSourceFile { get; set; }
  [ProtoMember(4)]
  public bool SyncSelection { get; set; }
  [ProtoMember(5)]
  public bool UseCompactMode { get; set; } // font size, node height
  [ProtoMember(6)]
  public bool ShowNodePopup { get; set; }
  [ProtoMember(7)]
  public bool AppendPercentageToFunction { get; set; }
  [ProtoMember(8)]
  public bool AppendDurationToFunction { get; set; }
  [ProtoMember(9)]
  public int NodePopupDuration { get; set; }
  [ProtoMember(10)]
  public string DefaultColorPalette { get; set; }
  [ProtoMember(11)]
  public string KernelColorPalette { get; set; }
  [ProtoMember(12)]
  public string ManagedColorPalette { get; set; }
  [ProtoMember(13)]
  public bool UseKernelColorPalette { get; set; }
  [ProtoMember(14)]
  public bool UseManagedColorPalette { get; set; }
  [ProtoMember(15)]
  public Color SelectedNodeColor { get; set; }
  [ProtoMember(16)]
  public Color SearchResultNodeColor { get; set; }
  [ProtoMember(17)]
  public Color SelectedNodeBorderColor { get; set; }
  [ProtoMember(18)]
  public Color SearchResultNodeBorderColor { get; set; }

  //? TODO: Options for
  //? - diff color scheme for kernel/managed
  //?      - enabled or not
  //?      - pick builtin color scheme
  //? - custom color scheme for module
  //? - auto-colors for functs using TextSearcher
  //? - show node percentage
  //?      - text color

  public override void Reset() {
    PrependModuleToFunction = true;
    SyncSelection = true;
    SyncSourceFile = false;
    ShowDetailsPanel = true;
    ShowNodePopup = true;
    AppendPercentageToFunction = true;
    AppendDurationToFunction = true;
    NodePopupDuration = DefaultNodePopupDuration;
    UseKernelColorPalette = true;
    UseManagedColorPalette = true;
    DefaultColorPalette = ColorPalette.Profile.Name;
    KernelColorPalette = ColorPalette.ProfileKernel.Name;
    ManagedColorPalette = ColorPalette.ProfileManaged.Name;

    //? TODO: Define SelectedNodeColor
    //? Use paletter in flame graph
  }

  public FlameGraphSettings Clone() {
    byte[] serialized = StateSerializer.Serialize(this);
    return StateSerializer.Deserialize<FlameGraphSettings>(serialized);
  }

  public override bool Equals(object obj) {
    return obj is FlameGraphSettings settings &&
           PrependModuleToFunction == settings.PrependModuleToFunction &&
           ShowDetailsPanel == settings.ShowDetailsPanel &&
           SyncSelection == settings.SyncSelection &&
           SyncSourceFile == settings.SyncSourceFile &&
           UseCompactMode == settings.UseCompactMode &&
           ShowNodePopup == settings.ShowNodePopup &&
           AppendPercentageToFunction == settings.AppendPercentageToFunction &&
           AppendDurationToFunction == settings.AppendDurationToFunction &&
           NodePopupDuration == settings.NodePopupDuration &&
           DefaultColorPalette == settings.DefaultColorPalette &&
           KernelColorPalette == settings.KernelColorPalette &&
           ManagedColorPalette == settings.ManagedColorPalette &&
           UseKernelColorPalette == settings.UseKernelColorPalette &&
           UseManagedColorPalette == settings.UseManagedColorPalette &&
           SelectedNodeColor == settings.SelectedNodeColor &&
           SearchResultNodeColor == settings.SearchResultNodeColor &&
           SelectedNodeBorderColor == settings.SelectedNodeBorderColor &&
           SearchResultNodeBorderColor == settings.SearchResultNodeBorderColor;
  }
}
