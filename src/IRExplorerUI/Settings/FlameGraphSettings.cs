// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System.Collections.Generic;
using System.Windows.Media;
using IRExplorerUI.Profile;
using ProtoBuf;

namespace IRExplorerUI;

[ProtoContract(SkipConstructor = true)]
public class FlameGraphSettings : SettingsBase {
  public static readonly int DefaultNodePopupDuration = (int)HoverPreview.HoverDuration.TotalMilliseconds;
  public Dictionary<ProfileCallTreeNodeKind, ColorPalette> Palettes { get; set; }
  public ColorPalette ModulesPalette { get; set; }

  public FlameGraphSettings() {
    Reset();
  }

  [ProtoMember(1), OptionValue(true)]
  public bool PrependModuleToFunction { get; set; }
  [ProtoMember(2), OptionValue(true)]
  public bool ShowDetailsPanel { get; set; }
  [ProtoMember(3), OptionValue(false)]
  public bool SyncSourceFile { get; set; }
  [ProtoMember(4), OptionValue(true)]
  public bool SyncSelection { get; set; }
  [ProtoMember(5), OptionValue(false)]
  public bool UseCompactMode { get; set; }
  [ProtoMember(6), OptionValue(true)]
  public bool ShowNodePopup { get; set; }
  [ProtoMember(7), OptionValue(true)]
  public bool AppendPercentageToFunction { get; set; }
  [ProtoMember(8), OptionValue(true)]
  public bool AppendDurationToFunction { get; set; }
  [ProtoMember(9), OptionValue(0)]
  public int NodePopupDuration { get; set; }
  [ProtoMember(10), OptionValue("Profile")]
  public string DefaultColorPalette { get; set; }
  [ProtoMember(11), OptionValue("ProfileKernel")]
  public string KernelColorPalette { get; set; }
  [ProtoMember(12), OptionValue("ProfileManaged")]
  public string ManagedColorPalette { get; set; }
  [ProtoMember(13), OptionValue(true)]
  public bool UseKernelColorPalette { get; set; }
  [ProtoMember(14), OptionValue(true)]
  public bool UseManagedColorPalette { get; set; }
  [ProtoMember(15), OptionValue("#D0E3F1")]
  public Color SelectedNodeColor { get; set; }
  [ProtoMember(16), OptionValue("#F0E68C")]
  public Color SearchResultMarkingColor { get; set; }
  [ProtoMember(17), OptionValue("#000000")]
  public Color SelectedNodeBorderColor { get; set; }
  [ProtoMember(18), OptionValue("#000000")]
  public Color SearchedNodeBorderColor { get; set; }
  [ProtoMember(19), OptionValue("#000000")]
  public Color NodeBorderColor { get; set; }
  [ProtoMember(20), OptionValue("#000000")]
  public Color NodeTextColor { get; set; }
  [ProtoMember(21), OptionValue("#696969")]
  public Color NodeModuleColor { get; set; }
  [ProtoMember(22), OptionValue("#191970")]
  public Color NodeWeightColor { get; set; }
  [ProtoMember(23), OptionValue("#800000")]
  public Color NodePercentageColor { get; set; }
  [ProtoMember(24), OptionValue("#c3ebbc")]
  public Color SearchedNodeColor { get; set; }
  [ProtoMember(25), OptionValue("#00008B")]
  public Color KernelNodeBorderColor { get; set; }
  [ProtoMember(26), OptionValue("#4B0082")]
  public Color ManagedNodeBorderColor { get; set; }
  [ProtoMember(27), OptionValue("#00009B")]
  public Color KernelNodeTextColor { get; set; }
  [ProtoMember(28), OptionValue("#800080")]
  public Color ManagedNodeTextColor { get; set; }

  public Brush GetNodeDefaultBrush(FlameGraphNode node) {
    CachePalettes();
    var palette = node.HasFunction ? Palettes[node.CallTreeNode.Kind] : Palettes[ProfileCallTreeNodeKind.Unset];
    int colorIndex = node.Depth % palette.Count;
    return palette.PickBrush(palette.Count - colorIndex - 1);
  }

  public override void Reset() {
    ResetAllOptions(this);
    NodePopupDuration = DefaultNodePopupDuration;
  }

  public FlameGraphSettings Clone() {
    byte[] serialized = StateSerializer.Serialize(this);
    return StateSerializer.Deserialize<FlameGraphSettings>(serialized);
  }

  public void ResedCachedPalettes() {
    Palettes = null;
  }

  private void CachePalettes() {
    if (Palettes == null) {
      Palettes = new Dictionary<ProfileCallTreeNodeKind, ColorPalette> {
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
    return AreOptionsEqual(this, obj);
  }

  public override string ToString() {
    return PrintOptions(this);
  }
}
