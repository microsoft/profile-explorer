using ProtoBuf;

namespace IRExplorerUI;

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
