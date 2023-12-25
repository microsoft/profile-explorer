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

  public OptionalColumnSettings() {
    Reset();
  }

  //? TODO: Move definition of time/perc columns here
  //? same for perf counters, metrics

  public OptionalColumnStyle DefaultMetricsColumnStyle => null;
  public OptionalColumnStyle DefaultCounterColumnStyle => null;

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
    columnStyles_ = new Dictionary<string, OptionalColumnStyle>();
    hiddenColumns_ = new HashSet<string>();
  }

  public OptionalColumnSettings Clone() {
    byte[] serialized = StateSerializer.Serialize(this);
    return StateSerializer.Deserialize<OptionalColumnSettings>(serialized);
  }

  public override bool Equals(object obj) {
    return obj is OptionalColumnSettings settings;
  }
}

public class OptionalColumnStyle {
  public bool ShowPercentageBar { get; set; }
  public bool ShowMainColumnPercentageBar { get; set; }
  public Color PercentageBarBackColor { get; set; }
  public Color TextColor { get; set; }
  public bool ShowIcon { get; set; }
  public bool ShowMainColumnIcon { get; set; }
  public bool PickColorForPercentage { get; set; }
  public bool UseBackColor { get; set; }
  public bool UseMainColumnBackColor { get; set; }
  public ColorPalette BackColorPalette { get; set; }
  public bool InvertColorPalette { get; set; }
}