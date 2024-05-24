// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System.Collections.Generic;
using System.Windows.Controls;
using ProtoBuf;

namespace IRExplorerUI;

[ProtoContract(SkipConstructor = true)]
public class ColumnSettings : SettingsBase {
  [ProtoMember(1), OptionValue()]
  public Dictionary<string, ColumnState> ColumnStates { get; set; }

  public ColumnSettings() {
    Reset();
  }

  [ProtoAfterDeserialization]
  private void InitializeReferenceMembers() {
    InitializeReferenceOptions(this);
  }

  public void SaveColumnsState(ListView listView) {
    if (listView.View is GridView gridView) {
      SaveColumnsState(gridView);
    }
  }

  public void SaveColumnsState(GridView gridView) {
    foreach (var column in gridView.Columns) {
      if (column.Header is GridViewColumnHeader header &&
          !string.IsNullOrEmpty(header.Name)) {
        var state = GetOrCreateColumnState(header.Name);
        state.IsVisible = true; //? TODO: Support saving visibility
        state.Width = (int)column.ActualWidth;
        state.Order = gridView.Columns.IndexOf(column);
      }
    }
  }

  public void RestoreColumnsState(ListView listView) {
    if (listView.View is GridView gridView) {
      RestoreColumnsState(gridView);
    }
  }

  public void RestoreColumnsState(GridView gridView) {
    List<(GridViewColumn, ColumnState)> pairs = new();

    foreach (var column in gridView.Columns) {
      if (column.Header is not GridViewColumnHeader header ||
          string.IsNullOrEmpty(header.Name)) continue;

      if (ColumnStates.TryGetValue(header.Name, out var state)) {
        column.Width = state.Width;
        pairs.Add((column, state));
      }
      else {
        pairs.Add((column, null));
      }
    }

    // Restore column order.
    pairs.Sort((a, b) => {
      if (a.Item2 == null || b.Item2 == null) return 0;
      return a.Item2.Order.CompareTo(b.Item2.Order);
    });
    gridView.Columns.Clear();

    foreach (var (column, _) in pairs) {
      gridView.Columns.Add(column);
    }
  }

  private ColumnState GetOrCreateColumnState(string columnName) {
    if (!ColumnStates.TryGetValue(columnName, out var state)) {
      state = new ColumnState();
      ColumnStates[columnName] = state;
    }

    return state;
  }

  public override void Reset() {
    InitializeReferenceMembers();
    ResetAllOptions(this);
  }

  public ColumnSettings Clone() {
    byte[] serialized = StateSerializer.Serialize(this);
    return StateSerializer.Deserialize<ColumnSettings>(serialized);
  }

  public override bool Equals(object obj) {
    return AreOptionsEqual(this, obj);
  }

  public override string ToString() {
    return PrintOptions(this);
  }
}

[ProtoContract(SkipConstructor = true)]
public class ColumnState : SettingsBase {
  [ProtoMember(1), OptionValue(true)]
  public bool IsVisible { get; set; }
  [ProtoMember(2), OptionValue(50)]
  public int Width { get; set; }
  [ProtoMember(3), OptionValue(int.MaxValue)]
  public int Order { get; set; }

  public ColumnState() {
    Reset();
  }

  public override void Reset() {
    ResetAllOptions(this);
  }

  public ColumnState Clone() {
    byte[] serialized = StateSerializer.Serialize(this);
    return StateSerializer.Deserialize<ColumnState>(serialized);
  }

  public override bool Equals(object obj) {
    return AreOptionsEqual(this, obj);
  }

  public override string ToString() {
    return PrintOptions(this);
  }
}
