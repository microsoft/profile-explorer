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
  public Color SearchResultMarkingColor { get; set; }
  [ProtoMember(17)]
  public Color SelectedNodeBorderColor { get; set; }
  [ProtoMember(18)]
  public Color SearchedNodeBorderColor { get; set; }
  [ProtoMember(19)]
  public Color NodeBorderColor { get; set; }
  [ProtoMember(20)]
  public Color NodeTextColor { get; set; }
  [ProtoMember(21)]
  public Color NodeModuleColor { get; set; }
  [ProtoMember(22)]
  public Color NodeWeightColor { get; set; }
  [ProtoMember(23)]
  public Color NodePercentageColor { get; set; }
  [ProtoMember(24)]
  public Color SearchedNodeColor { get; set; }
  [ProtoMember(25)]
  public bool PickColorByModule { get; set; }

  //? TODO: Options for
  //? - custom color scheme for module
  //? - auto-colors for functs using TextSearcher

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
    SearchResultMarkingColor = Colors.Khaki;
    NodeTextColor = Colors.DarkBlue;
    NodeBorderColor = Colors.Black;
    NodeModuleColor = Colors.DimGray;
    NodeWeightColor = Colors.Maroon;
    NodePercentageColor = Colors.DarkSlateBlue;
    SelectedNodeColor = Utils.ColorFromString("#D0E3F1");
    SelectedNodeBorderColor = Colors.Black;
    SearchedNodeColor = Utils.ColorFromString("#c3ebbc");
    SearchedNodeBorderColor = Colors.Black;
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
           SearchResultMarkingColor == settings.SearchResultMarkingColor &&
           SelectedNodeBorderColor == settings.SelectedNodeBorderColor &&
           SearchedNodeBorderColor == settings.SearchedNodeBorderColor &&
           NodeBorderColor == settings.NodeBorderColor &&
           NodeTextColor == settings.NodeTextColor &&
           NodeModuleColor == settings.NodeModuleColor &&
           NodeWeightColor == settings.NodeWeightColor &&
           NodePercentageColor == settings.NodePercentageColor &&
           SearchedNodeColor == settings.SearchedNodeColor &&
           PickColorByModule == settings.PickColorByModule;
  }
}
