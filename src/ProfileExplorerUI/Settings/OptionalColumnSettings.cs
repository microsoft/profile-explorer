// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Collections.Generic;
using System.Windows.Media;
using ProfileExplorer.Core.Settings;
using ProfileExplorer.Core.Utilities;
using ProtoBuf;

namespace ProfileExplorer.UI;

[ProtoContract(SkipConstructor = true)]
public class OptionalColumnSettings : SettingsBase {
  public OptionalColumnSettings() {
    Reset();
  }

  [ProtoMember(1)][OptionValue()]
  public Dictionary<string, OptionalColumnStyle> ColumnStyles { get; set; }
  [ProtoMember(2)][OptionValue()]
  public Dictionary<string, ColumnState> ColumnStates { get; set; }
  [ProtoMember(3)][OptionValue(false)]
  public bool RemoveEmptyColumns { get; set; }
  [ProtoMember(4)][OptionValue(true)]
  public bool ShowPerformanceCounterColumns { get; set; }
  [ProtoMember(5)][OptionValue(true)]
  public bool ShowPerformanceMetricColumns { get; set; }
  public static OptionalColumnStyle DefaultTimePercentageColumnStyle =>
    new() {
      ShowPercentageBar = OptionalColumnStyle.PartVisibility.IfActiveColumn,
      UseBackColor = OptionalColumnStyle.PartVisibility.Always,
      ShowIcon = OptionalColumnStyle.PartVisibility.Always,
      //BackColorPalette = ColorPalette.Profile,
      InvertColorPalette = false,
      PickColorForPercentage = true
    };
  public static OptionalColumnStyle DefaultTimeColumnStyle =>
    new() {
      ShowPercentageBar = OptionalColumnStyle.PartVisibility.IfActiveColumn,
      UseBackColor = OptionalColumnStyle.PartVisibility.Always,
      ShowIcon = OptionalColumnStyle.PartVisibility.IfActiveColumn,
      //BackColorPalette = ColorPalette.Profile,
      InvertColorPalette = false,
      PickColorForPercentage = true
    };

  [ProtoAfterDeserialization]
  private void InitializeReferenceMembers() {
    InitializeReferenceOptions(this);
  }

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
    return ColumnStyles.GetValueOrNull(column.ColumnName);
  }

  public void AddColumnStyle(OptionalColumn column, OptionalColumnStyle style) {
    ColumnStyles[column.ColumnName] = style;
  }

  private ColumnState GetOrCreateColumnState(OptionalColumn column) {
    if (!ColumnStates.TryGetValue(column.ColumnName, out var state)) {
      state = new ColumnState();
      ColumnStates[column.ColumnName] = state;
      NumberColumnStates();
    }

    return state;
  }

  private void NumberColumnStates() {
    var stateList = ColumnStates.ToValueList();
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
      if (column.IsPerformanceCounter && !ShowPerformanceCounterColumns ||
          column.IsPerformanceMetric && !ShowPerformanceMetricColumns) {
        continue;
      }

      GetOrCreateColumnState(column); // Ensure column state exists.
      sortedColumns.Add(column);
    }

    sortedColumns.Sort((a, b) => {
      var aState = ColumnStates[a.ColumnName];
      var bState = ColumnStates[b.ColumnName];
      return aState.Order.CompareTo(bState.Order);
    });

    return sortedColumns;
  }

  public override void Reset() {
    InitializeReferenceMembers();
    ResetAllOptions(this);
  }

  public OptionalColumnSettings Clone() {
    byte[] serialized = StateSerializer.Serialize(this);
    return StateSerializer.Deserialize<OptionalColumnSettings>(serialized);
  }

  public override bool Equals(object obj) {
    return AreOptionsEqual(this, obj);
  }

  public override string ToString() {
    return PrintOptions(this);
  }
}

[ProtoContract(SkipConstructor = true)]
public class OptionalColumnStyle : SettingsBase {
  public enum PartVisibility {
    Always,
    IfActiveColumn,
    Never
  }

  public OptionalColumnStyle() {
    Reset();
  }

  [ProtoMember(1)][OptionValue("")]
  public string AlternateTitle { get; set; }
  [ProtoMember(2)][OptionValue(PartVisibility.Never)]
  public PartVisibility ShowPercentageBar { get; set; }
  [ProtoMember(3)]
  public Color PercentageBarBackColor { get; set; }
  [ProtoMember(4)][OptionValue("#000000")]
  public Color TextColor { get; set; }
  [ProtoMember(5)][OptionValue(PartVisibility.Never)]
  public PartVisibility ShowIcon { get; set; }
  [ProtoMember(6)][OptionValue(false)]
  public bool PickColorForPercentage { get; set; }
  [ProtoMember(7)][OptionValue(PartVisibility.Never)]
  public PartVisibility UseBackColor { get; set; }
  [ProtoMember(8)][OptionValue("")]
  public string BackColorPalette { get; set; }
  [ProtoMember(9)][OptionValue(false)]
  public bool InvertColorPalette { get; set; }
  [ProtoMember(10)][OptionValue("#00FFFFFF")]
  public Color BackgroundColor { get; set; }

  public override void Reset() {
    ResetAllOptions(this);
  }

  public OptionalColumnStyle Clone() {
    byte[] serialized = StateSerializer.Serialize(this);
    return StateSerializer.Deserialize<OptionalColumnStyle>(serialized);
  }

  public override bool Equals(object obj) {
    return AreOptionsEqual(this, obj);
  }

  public override string ToString() {
    return PrintOptions(this);
  }
}