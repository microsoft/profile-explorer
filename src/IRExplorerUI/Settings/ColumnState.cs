using ProtoBuf;

namespace IRExplorerUI;

[ProtoContract(SkipConstructor = true)]
public class ColumnState : SettingsBase {
  [ProtoMember(1)]
  public bool IsVisible { get; set; }
  [ProtoMember(2)]
  public int Width { get; set; }
  [ProtoMember(3)]
  public int Order { get; set; }

  public ColumnState() {
    Reset();
  }

  public override void Reset() {
    IsVisible = true;
    Order = int.MaxValue;
    Width = 50;
  }

  public ColumnState Clone() {
    byte[] serialized = StateSerializer.Serialize(this);
    return StateSerializer.Deserialize<ColumnState>(serialized);
  }

  public override bool Equals(object obj) {
    return obj is ColumnState other &&
           IsVisible == other.IsVisible &&
           Width == other.Width &&
           Order == other.Order;
  }

  public override string ToString() {
    return $"IsVisible: {IsVisible}, Width: {Width}, Order: {Order}";
  }
}
