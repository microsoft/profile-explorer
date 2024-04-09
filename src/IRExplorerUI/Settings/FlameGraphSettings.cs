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
  [ProtoContract(SkipConstructor = true)]
  public class NodeMarkingStyle {
    private TextSearcher searcher_;
    private string name_;

    [ProtoMember(1)]
    public bool IsEnabled { get; set; }
    [ProtoMember(2)]
    public string Title { get; set; }
    
    [ProtoMember(3)]
    public string Name {
      get => name_;
      set {
        name_ = value;
        searcher_ = new TextSearcher(); // Caches precompiled Regex.
      }
    }

    [ProtoMember(4)]
    public Color Color { get; set; }
    [ProtoMember(5)]
    public bool IsRegex { get; set; }

    public NodeMarkingStyle(string name, Color color) {
      Name = name;
      Color = color;
      IsEnabled = true;
    }

    public bool NameMatches(string candidate) {
      if (!IsEnabled || candidate.Length <= 0 || Name.Length <= 0) {
        return false;
      }

      if (IsRegex) {
        return searcher_.Includes(candidate, Name, TextSearchKind.Regex);
      }
      else {
        return searcher_.Includes(candidate, Name, TextSearchKind.CaseInsensitive);
      }
    }

    protected bool Equals(NodeMarkingStyle other) {
      return Name == other.Name &&
             IsEnabled == other.IsEnabled &&
             Title == other.Title &&
             IsRegex == other.IsRegex &&
             Color.Equals(other.Color);
    }

    public override bool Equals(object obj) {
      if (ReferenceEquals(null, obj))
        return false;
      if (ReferenceEquals(this, obj))
        return true;
      if (obj.GetType() != this.GetType())
        return false;
      return Equals((NodeMarkingStyle)obj);
    }
  }

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
  public bool UseAutoModuleColors { get; set; }
  [ProtoMember(26)]
  public bool UseModuleColors { get; set; }
  [ProtoMember(27)]
  public List<NodeMarkingStyle> ModuleColors { get; set; }
  [ProtoMember(28)]
  public bool UseFunctionColors { get; set; }
  [ProtoMember(29)]
  public List<NodeMarkingStyle> FunctionColors { get; set; }
  [ProtoMember(30)]
  public string ModulesColorPalette { get; set; }

  public bool GetModuleColor(string name, out Color color) {
    return GetMarkingColor(name, ModuleColors, out color);
  }

  public bool GetModuleBrush(string name, out Brush brush) {
    if (GetMarkingColor(name, ModuleColors, out var color)) {
      brush = ColorBrushes.GetBrush(color);
      return true;
    }

    brush = Brushes.Transparent;
    return false;
  }

  public Brush GetAutoModuleBrush(string name) {
    CachePalettes();

    if (modulesPalette_ != null) {
      int hash = name.GetStableHashCode();
      return modulesPalette_.PickBrush(hash % modulesPalette_.Count);
    }

    return Brushes.Transparent;
  }

  public Brush GetNodeDefaultBrush(FlameGraphNode node) {
    CachePalettes();
    var palette = node.HasFunction ? palettes_[node.CallTreeNode.Kind] : palettes_[ProfileCallTreeNodeKind.Unset];
    int colorIndex = node.Depth % palette.Count;
    return palette.PickBrush(palette.Count - colorIndex - 1);
  }

  public bool GetFunctionColor(string name, out Color color) {
    return GetMarkingColor(name, FunctionColors, out color);
  }

  private bool GetMarkingColor(string name, List<NodeMarkingStyle> markingColors, out Color color) {
    foreach (var pair in markingColors) {
      if (pair.NameMatches(name)) {
        color = pair.Color;
        return true;
      }
    }

    color = default(Color);
    return false;
  }

  public void AddModuleColor(string moduleName, Color color) {
    AddMarkingColor(moduleName, color, ModuleColors);
  }

  public void AddFunctionColor(string functionName, Color color) {
    AddMarkingColor(functionName, color, FunctionColors);
  }

  private void AddMarkingColor(string name, Color color, List<NodeMarkingStyle> markingColors) {
    foreach (var pair in markingColors) {
      if (pair.Name.Length > 0 &&
          pair.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) {
        pair.Color = color;

        return;
      }
    }

    markingColors.Add(new NodeMarkingStyle(name, color));
  }

  public override void Reset() {
    InitializeReferenceMembers();
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
    ModulesColorPalette = ColorPalette.LightPastels.Name;
    SearchResultMarkingColor = Colors.Khaki;
    NodeTextColor = Colors.DarkBlue;
    NodeBorderColor = Colors.Black;
    NodeModuleColor = Colors.DimGray;
    NodeWeightColor = Colors.Maroon;
    NodePercentageColor = Colors.Black;
    SelectedNodeColor = Utils.ColorFromString("#D0E3F1");
    SelectedNodeBorderColor = Colors.Black;
    SearchedNodeColor = Utils.ColorFromString("#c3ebbc");
    SearchedNodeBorderColor = Colors.Black;
  }

  [ProtoAfterDeserialization]
  private void InitializeReferenceMembers() {
    ModuleColors ??= new List<NodeMarkingStyle>();
    FunctionColors ??= new List<NodeMarkingStyle>();
  }

  public FlameGraphSettings Clone() {
    byte[] serialized = StateSerializer.Serialize(this);
    return StateSerializer.Deserialize<FlameGraphSettings>(serialized);
  }

  public void CachePalettes() {
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

    if (modulesPalette_ == null && UseAutoModuleColors) {
      modulesPalette_ = ColorPalette.GetPalette(ModulesColorPalette);
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
           ModulesColorPalette == settings.ModulesColorPalette &&
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
           UseAutoModuleColors == settings.UseAutoModuleColors &&
           UseModuleColors == settings.UseModuleColors &&
           ModuleColors.AreEqual(settings.ModuleColors) &&
           UseFunctionColors == settings.UseFunctionColors &&
           FunctionColors.AreEqual(settings.FunctionColors);
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
           $"ModulesColorPalette: {ModulesColorPalette}\n" +
           $"UseKernelColorPalette: {UseKernelColorPalette}\n" +
           $"UseManagedColorPalette: {UseManagedColorPalette}\n" +
           $"SelectedNodeColor: {SelectedNodeColor}\n" +
           $"SearchResultMarkingColor: {SearchResultMarkingColor}\n" +
           $"SelectedNodeBorderColor: {SelectedNodeBorderColor}\n" +
           $"SearchedNodeBorderColor: {SearchedNodeBorderColor}\n" +
           $"NodeBorderColor: {NodeBorderColor}\n" +
           $"NodeTextColor: {NodeTextColor}\n" +
           $"NodeModuleColor: {NodeModuleColor}\n" +
           $"NodeWeightColor: {NodeWeightColor}\n" +
           $"NodePercentageColor: {NodePercentageColor}\n" +
           $"SearchedNodeColor: {SearchedNodeColor}\n" +
           $"UseAutoModuleColors: {UseAutoModuleColors}\n" +
           $"UseModuleColors: {UseModuleColors}\n" +
           $"ModuleColors: {ModuleColors}\n" +
           $"UseFunctionColors: {UseFunctionColors}\n" +
           $"FunctionColors: {FunctionColors}";
  }
}