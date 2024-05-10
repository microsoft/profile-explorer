using System;
using System.Windows.Media;
using ProtoBuf;

namespace IRExplorerUI;

[ProtoContract(SkipConstructor = true)]
[ProtoInclude(100, typeof(DocumentSettings))]
[ProtoInclude(200, typeof(SourceFileSettings))]
public abstract class TextViewSettingsBase : SettingsBase {
  [ProtoMember(1)]
  public ProfileDocumentMarkerSettings ProfileMarkerSettings { get; set; }
  [ProtoMember(2)]
  public OptionalColumnSettings ColumnSettings { get; set; }
  [ProtoMember(3), OptionValue("Consolas")]
  public string FontName { get; set; }
  [ProtoMember(4), OptionValue(12)]
  public double FontSize { get; set; }
  [ProtoMember(5), OptionValue(typeof(Color), "#FFFAFA")]
  public Color BackgroundColor { get; set; }
  [ProtoMember(6), OptionValue(typeof(Color), "#f5f5f5")]
  public Color AlternateBackgroundColor { get; set; }
  [ProtoMember(7), OptionValue(typeof(Color), "#FFFAFA")]
  public Color MarginBackgroundColor { get; set; }
  [ProtoMember(8), OptionValue(typeof(Color), "#000000")]
  public Color TextColor { get; set; }
  [ProtoMember(9), OptionValue(typeof(Color), "#C5DEEA")]
  public Color SelectedValueColor { get; set; }
  [ProtoMember(10), OptionValue(true)]
  public bool ShowBlockSeparatorLine { get; set; }
  [ProtoMember(11), OptionValue(typeof(Color), "#C0C0C0")]
  public Color BlockSeparatorColor { get; set; }
  [ProtoMember(12), OptionValue(typeof(Color), "#696969")]
  public Color CurrentLineBorderColor { get; set; }
  [ProtoMember(13), OptionValue(true)]
  public bool HighlightCurrentLine { get; set; }

  public TextViewSettingsBase() {
    Reset();
  }
  
  public override void Reset() {
    InitializeReferenceMembers();
    ResetAllOptions(this, typeof(TextViewSettingsBase));
  }

  [ProtoAfterDeserialization]
  private void InitializeReferenceMembers() {
    InitializeReferenceOptions(this);
  }

  public bool HasProfilingChanges(TextViewSettingsBase other) {
    return ProfileMarkerSettings.HasChanges(other.ProfileMarkerSettings) ||
           ColumnSettings.HasChanges(other.ColumnSettings) ||
           // Changing font means columns must be redrawn.
           FontName != other.FontName ||
           Math.Abs(FontSize - other.FontSize) >= double.Epsilon;
  }

  public override bool Equals(object obj) {
    return AreOptionsEqual(this, obj, typeof(TextViewSettingsBase));
  }

  public override string ToString() {
    return PrintOptions(this);
  }
}
