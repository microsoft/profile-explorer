// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Media;
using IRExplorerUI.Profile;
using ProtoBuf;

namespace IRExplorerUI;

[ProtoContract(SkipConstructor = true)]
public class FlameGraphSettings : SettingsBase {
  public static readonly int DefaultNodePopupDuration = (int)HoverPreview.HoverDuration.TotalMilliseconds;
  private Dictionary<ProfileCallTreeNodeKind, ColorPalette> palettes_;
  private ColorPalette modulesPalette_;

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
  public bool UseCompactMode { get; set; }
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
  public Color KernelNodeBorderColor { get; set; }
  [ProtoMember(26)]
  public Color ManagedNodeBorderColor { get; set; }
  [ProtoMember(27)]
  public Color KernelNodeTextColor { get; set; }
  [ProtoMember(28)]
  public Color ManagedNodeTextColor { get; set; }

  public Brush GetNodeDefaultBrush(FlameGraphNode node) {
    CachePalettes();
    var palette = node.HasFunction ? palettes_[node.CallTreeNode.Kind] : palettes_[ProfileCallTreeNodeKind.Unset];
    int colorIndex = node.Depth % palette.Count;
    return palette.PickBrush(palette.Count - colorIndex - 1);
  }

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
    NodeTextColor = Colors.MidnightBlue;
    KernelNodeTextColor = Colors.MediumBlue;
    ManagedNodeTextColor = Colors.Purple;
    NodeBorderColor = Colors.Black;
    KernelNodeBorderColor = Colors.DarkBlue;
    ManagedNodeBorderColor = Colors.Indigo;
    NodeModuleColor = Colors.DimGray;
    NodeWeightColor = Colors.Maroon;
    NodePercentageColor = Colors.Black;
    SelectedNodeColor = Utils.ColorFromString("#D0E3F1");
    SelectedNodeBorderColor = Colors.Black;
    SearchedNodeColor = Utils.ColorFromString("#c3ebbc");
    SearchedNodeBorderColor = Colors.Black;
  }

  public FlameGraphSettings Clone() {
    byte[] serialized = StateSerializer.Serialize(this);
    return StateSerializer.Deserialize<FlameGraphSettings>(serialized);
  }

  public void ResedCachedPalettes() {
    palettes_ = null;
  }

  private void CachePalettes() {
    if (palettes_ == null) {
      palettes_ = new Dictionary<ProfileCallTreeNodeKind, ColorPalette> {
        [ProfileCallTreeNodeKind.Unset] = ColorPalette.GetPalette(DefaultColorPalette),
        [ProfileCallTreeNodeKind.NativeUser] = ColorPalette.GetPalette(DefaultColorPalette),
        [ProfileCallTreeNodeKind.NativeKernel] = UseKernelColorPalette ?
          ColorPalette.GetPalette(KernelColorPalette) :
          ColorPalette.GetPalette(DefaultColorPalette),
        [ProfileCallTreeNodeKind.Managed] = UseManagedColorPalette ?
          ColorPalette.GetPalette(ManagedColorPalette) :
          ColorPalette.GetPalette(DefaultColorPalette)
      };
    }
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
           KernelNodeBorderColor == settings.KernelNodeBorderColor &&
           ManagedNodeBorderColor == settings.ManagedNodeBorderColor &&
           KernelNodeTextColor == settings.KernelNodeTextColor &&
           ManagedNodeTextColor == settings.ManagedNodeTextColor;
  }

  public override string ToString() {
    return $"PrependModuleToFunction: {PrependModuleToFunction}\n" +
           $"ShowDetailsPanel: {ShowDetailsPanel}\n" +
           $"SyncSelection: {SyncSelection}\n" +
           $"SyncSourceFile: {SyncSourceFile}\n" +
           $"UseCompactMode: {UseCompactMode}\n" +
           $"ShowNodePopup: {ShowNodePopup}\n" +
           $"AppendPercentageToFunction: {AppendPercentageToFunction}\n" +
           $"AppendDurationToFunction: {AppendDurationToFunction}\n" +
           $"NodePopupDuration: {NodePopupDuration}\n" +
           $"DefaultColorPalette: {DefaultColorPalette}\n" +
           $"KernelColorPalette: {KernelColorPalette}\n" +
           $"ManagedColorPalette: {ManagedColorPalette}\n" +
           $"UseKernelColorPalette: {UseKernelColorPalette}\n" +
           $"UseManagedColorPalette: {UseManagedColorPalette}\n" +
           $"SelectedNodeColor: {SelectedNodeColor}\n" +
           $"SearchResultMarkingColor: {SearchResultMarkingColor}\n" +
           $"SelectedNodeBorderColor: {SelectedNodeBorderColor}\n" +
           $"SearchedNodeBorderColor: {SearchedNodeBorderColor}\n" +
           $"NodeBorderColor: {NodeBorderColor}\n" +
           $"KernelNodeBorderColor: {KernelNodeBorderColor}\n" +
           $"ManagedNodeBorderColor: {ManagedNodeBorderColor}\n" +
           $"NodeTextColor: {NodeTextColor}\n" +
           $"KernelNodeTextColor: {KernelNodeTextColor}\n" +
           $"ManagedNodeTextColor: {ManagedNodeTextColor}\n" +
           $"NodeModuleColor: {NodeModuleColor}\n" +
           $"NodeWeightColor: {NodeWeightColor}\n" +
           $"NodePercentageColor: {NodePercentageColor}\n" +
           $"SearchedNodeColor: {SearchedNodeColor}\n";
  }
}