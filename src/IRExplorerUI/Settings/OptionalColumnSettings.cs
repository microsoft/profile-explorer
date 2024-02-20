// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Media;
using IRExplorerCore.Utilities;
using ProtoBuf;

namespace IRExplorerUI;

[ProtoContract(SkipConstructor = true)]
public class OptionalColumnSettings : SettingsBase {
  [ProtoMember(1)]
  private Dictionary<string, OptionalColumnStyle> columnStyles_;
  [ProtoMember(2)]
  private Dictionary<string, OptionalColumnState> columnStates_;
  [ProtoMember(3)]  public bool RemoveEmptyColumns { get; set; }
  [ProtoMember(4)]  public bool ShowPerformanceCounterColumns { get; set; }
  [ProtoMember(5)]  public bool ShowPerformanceMetricColumns { get; set; }
  
  public OptionalColumnSettings() {
    Reset();
  }

  [ProtoAfterDeserialization]
  private void InitializeReferenceMembers() {
    columnStyles_ ??= new Dictionary<string, OptionalColumnStyle>();
    columnStates_ ??= new Dictionary<string, OptionalColumnState>();
  }

  public static OptionalColumnStyle DefaultTimePercentageColumnStyle =>
    new OptionalColumnStyle() {
      ShowPercentageBar = OptionalColumnStyle.PartVisibility.Always,
      UseBackColor = OptionalColumnStyle.PartVisibility.Always,
      ShowIcon = OptionalColumnStyle.PartVisibility.Always,
      //BackColorPalette = ColorPalette.Profile,
      InvertColorPalette = false,
      PickColorForPercentage = true
    };
  public static OptionalColumnStyle DefaultTimeColumnStyle =>
    new OptionalColumnStyle() {
      ShowPercentageBar = OptionalColumnStyle.PartVisibility.IfActiveColumn,
      UseBackColor = OptionalColumnStyle.PartVisibility.Always,
      ShowIcon = OptionalColumnStyle.PartVisibility.IfActiveColumn,
      //BackColorPalette = ColorPalette.Profile,
      InvertColorPalette = false,
      PickColorForPercentage = true
    };

  public static OptionalColumnStyle DefaultMetricsColumnStyle(int k) {
    return new OptionalColumnStyle() {
      ShowPercentageBar = OptionalColumnStyle.PartVisibility.Always,
      UseBackColor = OptionalColumnStyle.PartVisibility.Always,
      ShowIcon = OptionalColumnStyle.PartVisibility.IfActiveColumn,
      PickColorForPercentage = false,
      //BackColorPalette = ColorPalette.Profile,
      InvertColorPalette = false,
      TextColor = ColorPalette.DarkHue.PickColor(k),
      PercentageBarBackColor = ColorPalette.DarkHue.PickColor(k)
    };
  }

  public static OptionalColumnStyle DefaultCounterColumnStyle(int k) {
    return new OptionalColumnStyle() {
      ShowPercentageBar = OptionalColumnStyle.PartVisibility.Always,
      UseBackColor = OptionalColumnStyle.PartVisibility.IfActiveColumn,
      ShowIcon = OptionalColumnStyle.PartVisibility.IfActiveColumn,
      PickColorForPercentage = false,
      //BackColorPalette = ColorPalette.Profile,
      InvertColorPalette = false,
      TextColor = ColorPalette.DarkHue.PickColor(k),
      PercentageBarBackColor = ColorPalette.DarkHue.PickColor(k)
    };
  }

  public OptionalColumnStyle GetColumnStyle(OptionalColumn column) {
    return columnStyles_.GetValueOrNull(column.ColumnName);
  }

  public void AddColumnStyle(OptionalColumn column, OptionalColumnStyle style) {
    columnStyles_[column.ColumnName] = style;
  }

  private OptionalColumnState GetOrCreateColumnState(OptionalColumn column) {
    if(!columnStates_.TryGetValue(column.ColumnName, out var state)) {
      state = new OptionalColumnState();
      columnStates_[column.ColumnName] = state;
      NumberColumnStates();
    }

    return state;
  }

  private void NumberColumnStates() {
    var stateList = columnStates_.ToValueList();
    stateList.Sort((a, b) => a.Order.CompareTo(b.Order));

    for (int i = 0; i < stateList.Count; ++i) {
      stateList[i].Order = i;
    }
  }

  public bool IsColumnVisible(OptionalColumn column) {
    return GetOrCreateColumnState(column).IsVisible;
  }

  public void SetColumnVisibility(OptionalColumn column, bool visible) {
    GetOrCreateColumnState(column).IsVisible = visible;
  }

  public List<OptionalColumn> FilterAndSortColumns(List<OptionalColumn> columns) {
    NumberColumnStates();
    var sortedColumns = new List<OptionalColumn>();

    foreach (var column in columns) {
      // Filter out columns.
      if ((column.IsPerformanceCounter && !ShowPerformanceCounterColumns) ||
          (column.IsPerformanceMetric && !ShowPerformanceMetricColumns)) {
        continue;
      }

      GetOrCreateColumnState(column); // Ensure column state exists.
      sortedColumns.Add(column);
    }

    sortedColumns.Sort((a, b) => {
      var aState = columnStates_[a.ColumnName];
      var bState = columnStates_[b.ColumnName];
      return aState.Order.CompareTo(bState.Order);
    });

    return sortedColumns;
  }

  public override void Reset() {
    InitializeReferenceMembers();
    columnStyles_.Clear();
    columnStates_.Clear();
    RemoveEmptyColumns = true;
    ShowPerformanceCounterColumns = true;
    ShowPerformanceMetricColumns = true;
  }

  public OptionalColumnSettings Clone() {
    byte[] serialized = StateSerializer.Serialize(this);
    return StateSerializer.Deserialize<OptionalColumnSettings>(serialized);
  }

  public override bool Equals(object obj) {
    return obj is OptionalColumnSettings other &&
           RemoveEmptyColumns == other.RemoveEmptyColumns &&
           ShowPerformanceCounterColumns == other.ShowPerformanceCounterColumns &&
           ShowPerformanceMetricColumns == other.ShowPerformanceMetricColumns &&
           columnStyles_.AreEqual(other.columnStyles_) &&
           columnStates_.AreEqual(other.columnStates_);
  }

  public override string ToString() {
    return $"RemoveEmptyColumns: {RemoveEmptyColumns}\n" +
           $"ShowPerformanceCounterColumns: {ShowPerformanceCounterColumns}\n" +
           $"ShowPerformanceMetricColumns: {ShowPerformanceMetricColumns}\n" +
           $"columnStyles_: {columnStyles_}\n" +
           $"columnStates_: {columnStates_}";
  
  }
}

[ProtoContract(SkipConstructor = true)]
public class OptionalColumnState : SettingsBase {
  [ProtoMember(1)]
  public bool IsVisible { get; set; }
  [ProtoMember(2)]
  public int Width { get; set; }
  [ProtoMember(3)]
  public int Order { get; set; }

  public OptionalColumnState() {
    Reset();
  }

  public override void Reset() {
    IsVisible = true;
    Order = int.MaxValue;
    Width = 50;
  }

  public OptionalColumnState Clone() {
    byte[] serialized = StateSerializer.Serialize(this);
    return StateSerializer.Deserialize<OptionalColumnState>(serialized);
  }

  public override bool Equals(object obj) {
    return obj is OptionalColumnState other &&
           IsVisible == other.IsVisible &&
           Width == other.Width &&
           Order == other.Order;
  }

  public override string ToString() {
    return $"IsVisible: {IsVisible}, Width: {Width}, Order: {Order}";
  }
}

[ProtoContract(SkipConstructor = true)]
public class OptionalColumnStyle : SettingsBase {
  public OptionalColumnStyle() {
    Reset();
  }

  public enum PartVisibility {
    Always,
    IfActiveColumn,
    Never
  }

  [ProtoMember(1)]
  public string AlternateTitle { get; set; }
  [ProtoMember(2)]
  public PartVisibility ShowPercentageBar { get; set; }
  [ProtoMember(3)]
  public Color PercentageBarBackColor { get; set; }
  [ProtoMember(4)]
  public Color TextColor { get; set; }
  [ProtoMember(5)]
  public PartVisibility ShowIcon { get; set; }
  [ProtoMember(6)]
  public bool PickColorForPercentage { get; set; }
  [ProtoMember(7)]
  public PartVisibility UseBackColor { get; set; }
  [ProtoMember(8)]
  public string BackColorPalette { get; set; }
  [ProtoMember(9)]
  public bool InvertColorPalette { get; set; }
  [ProtoMember(10)]
  public Color BackgroundColor { get; set; }

  public override void Reset() {
  }

  public OptionalColumnStyle Clone() {
    byte[] serialized = StateSerializer.Serialize(this);
    return StateSerializer.Deserialize<OptionalColumnStyle>(serialized);
  }

  public override bool Equals(object obj) {
    return obj is OptionalColumnStyle other &&
           AlternateTitle == other.AlternateTitle &&
           ShowPercentageBar == other.ShowPercentageBar &&
           PercentageBarBackColor.Equals(other.PercentageBarBackColor) &&
           TextColor.Equals(other.TextColor) &&
           ShowIcon == other.ShowIcon &&
           PickColorForPercentage == other.PickColorForPercentage && UseBackColor == other.UseBackColor &&
           Equals(BackColorPalette, other.BackColorPalette) &&
           InvertColorPalette == other.InvertColorPalette &&
           BackgroundColor == other.BackgroundColor;
  }

  public override string ToString() {
    return $"AlternateTitle: {AlternateTitle}\n" +
           $"ShowPercentageBar: {ShowPercentageBar}\n" +
           $"PercentageBarBackColor: {PercentageBarBackColor}\n" +
           $"TextColor: {TextColor}\n" +
           $"ShowIcon: {ShowIcon}\n" +
           $"PickColorForPercentage: {PickColorForPercentage}\n" +
           $"UseBackColor: {UseBackColor}\n" +
           $"BackColorPalette: {BackColorPalette}\n" +
           $"InvertColorPalette: {InvertColorPalette}\n" +
           $"BackgroundColor: {BackgroundColor}";
  }
}