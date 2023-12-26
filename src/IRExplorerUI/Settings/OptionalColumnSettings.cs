// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System.Collections.Generic;
using System.Windows.Media;
using IRExplorerCore.Utilities;
using ProtoBuf;

namespace IRExplorerUI;

[ProtoContract(SkipConstructor = true)]
public class OptionalColumnSettings : SettingsBase {
  [ProtoMember(1)]
  private Dictionary<string, OptionalColumnStyle> columnStyles_;
  [ProtoMember(2)]
  private HashSet<string> hiddenColumns_;

  //? TODO: Column order, width

  public OptionalColumnSettings() {
    Reset();
  }

  [ProtoAfterDeserialization]
  private void InitializeReferenceMembers() {
    columnStyles_ ??= new Dictionary<string, OptionalColumnStyle>();
    hiddenColumns_ ??= new HashSet<string>();
  }

  public static OptionalColumnStyle DefaultTimePercentageColumnStyle =>
    new OptionalColumnStyle(1) {
      ShowPercentageBar = true,
      ShowMainColumnPercentageBar = true,
      UseBackColor = true,
      UseMainColumnBackColor = true,
      ShowIcon = true,
      BackColorPalette = ColorPalette.Profile,
      InvertColorPalette = false,
      PickColorForPercentage = true
    };
  public static OptionalColumnStyle DefaultTimeColumnStyle =>
    new OptionalColumnStyle(0) {
      ShowPercentageBar = false,
      ShowMainColumnPercentageBar = false,
      UseBackColor = true,
      UseMainColumnBackColor = true,
      ShowMainColumnIcon = true,
      BackColorPalette = ColorPalette.Profile,
      InvertColorPalette = false,
      PickColorForPercentage = true
    };

  public static OptionalColumnStyle DefaultMetricsColumnStyle(int k) {
    return new OptionalColumnStyle(k + 2) {
      ShowPercentageBar = true,
      ShowMainColumnPercentageBar = true,
      UseBackColor = true,
      UseMainColumnBackColor = true,
      PickColorForPercentage = false,
      ShowIcon = false,
      ShowMainColumnIcon = true,
      BackColorPalette = ColorPalette.Profile,
      InvertColorPalette = true,
      TextColor = ColorPalette.DarkHue.PickColor(k),
      PercentageBarBackColor = ColorPalette.DarkHue.PickColor(k)
    };
  }

  public static OptionalColumnStyle DefaultCounterColumnStyle(int k) {
    return new OptionalColumnStyle(k + 2) {
      ShowPercentageBar = true,
      ShowMainColumnPercentageBar = true,
      UseBackColor = false,
      UseMainColumnBackColor = true,
      PickColorForPercentage = false,
      ShowIcon = false,
      ShowMainColumnIcon = true,
      BackColorPalette = ColorPalette.Profile,
      InvertColorPalette = true,
      TextColor = ColorPalette.DarkHue.PickColor(k),
      PercentageBarBackColor = ColorPalette.DarkHue.PickColor(k)
    };
  }

  public OptionalColumnStyle GetColumnStyle(OptionalColumn column) {
    return columnStyles_.GetValueOrNull(column.ColumnName);
  }

  public bool IsColumnVisible(OptionalColumn column) {
    return !hiddenColumns_.Contains(column.ColumnName);
  }

  public void SetColumnVisibility(OptionalColumn column, bool visible) {
    if (visible) {
      hiddenColumns_.Add(column.ColumnName);
    }
    else {
      hiddenColumns_.Remove(column.ColumnName);
    }
  }

  public override void Reset() {
    InitializeReferenceMembers();
  }

  public OptionalColumnSettings Clone() {
    byte[] serialized = StateSerializer.Serialize(this);
    return StateSerializer.Deserialize<OptionalColumnSettings>(serialized);
  }

  public override bool Equals(object obj) {
    return obj is OptionalColumnSettings settings;
  }
}

[ProtoContract(SkipConstructor = true)]
public class OptionalColumnStyle {
  public OptionalColumnStyle() : this(int.MaxValue) {
  }

  public OptionalColumnStyle(int order) {
    Order = order;
    IsVisible = true;
    Width = 50;
  }

  [ProtoMember(1)]
  public bool IsVisible { get; set; }
  [ProtoMember(2)]
  public int Order { get; set; }
  [ProtoMember(3)]
  public double Width { get; set; }
  [ProtoMember(4)]
  public string Abbreviation { get; set; }
  [ProtoMember(5)]
  public bool ShowPercentageBar { get; set; }
  [ProtoMember(6)]
  public bool ShowMainColumnPercentageBar { get; set; }
  [ProtoMember(7)]
  public Color PercentageBarBackColor { get; set; }
  [ProtoMember(8)]
  public Color TextColor { get; set; }
  [ProtoMember(9)]
  public bool ShowIcon { get; set; }
  [ProtoMember(10)]
  public bool ShowMainColumnIcon { get; set; }
  [ProtoMember(11)]
  public bool PickColorForPercentage { get; set; }
  [ProtoMember(12)]
  public bool UseBackColor { get; set; }
  [ProtoMember(13)]
  public bool UseMainColumnBackColor { get; set; }
  [ProtoMember(14)]
  public ColorPalette BackColorPalette { get; set; }
  [ProtoMember(15)]
  public bool InvertColorPalette { get; set; }
}
