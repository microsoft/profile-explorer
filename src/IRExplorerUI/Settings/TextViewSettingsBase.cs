using System;
using System.Windows.Media;
using ProtoBuf;

namespace IRExplorerUI;

[ProtoContract(SkipConstructor = true)]
[ProtoInclude(100, typeof(DocumentSettings))]
[ProtoInclude(200, typeof(SourceFileSettings))]
public class TextViewSettingsBase : SettingsBase {
  [ProtoMember(1)]
  public ProfileDocumentMarkerSettings ProfileMarkerSettings { get; set; }
  [ProtoMember(2)]
  public OptionalColumnSettings ColumnSettings { get; set; }
  [ProtoMember(3)] public string FontName { get; set; }
  [ProtoMember(4)] public double FontSize { get; set; }
  [ProtoMember(5)] public Color BackgroundColor { get; set; }
  [ProtoMember(6)] public Color AlternateBackgroundColor { get; set; }
  [ProtoMember(7)] public Color MarginBackgroundColor { get; set; }
  [ProtoMember(8)] public Color TextColor { get; set; }
  [ProtoMember(9)] public Color SelectedValueColor { get; set; }
  [ProtoMember(10)] public bool ShowBlockSeparatorLine { get; set; }
  [ProtoMember(11)] public Color BlockSeparatorColor { get; set; }
  [ProtoMember(12)] public Color CurrentLineBorderColor { get; set; }
  [ProtoMember(13)] public bool HighlightCurrentLine { get; set; }

  public TextViewSettingsBase() {
    Reset();
  }

  public override void Reset() {
    InitializeReferenceMembers();
    FontName = "Consolas";
    FontSize = 12;
    BackgroundColor = Utils.ColorFromString("#FFFAFA");
    AlternateBackgroundColor = Utils.ColorFromString("#f5f5f5");
    TextColor = Colors.Black;
    MarginBackgroundColor = Colors.Gainsboro;
    SelectedValueColor = Utils.ColorFromString("#C5DEEA");
    BlockSeparatorColor = Colors.Silver;
    CurrentLineBorderColor = Colors.DimGray;
    HighlightCurrentLine = true;
    ShowBlockSeparatorLine = true;
    ProfileMarkerSettings.Reset();
    ColumnSettings.Reset();
  }

  [ProtoAfterDeserialization]
  private void InitializeReferenceMembers() {
    ProfileMarkerSettings ??= new();
    ColumnSettings ??= new();
  }

  public bool HasProfilingChanges(TextViewSettingsBase other) {
    return ProfileMarkerSettings.HasChanges(other.ProfileMarkerSettings) ||
           ColumnSettings.HasChanges(other.ColumnSettings) ||
           // Changing font means columns must be redrawn.
           FontName != other.FontName ||
           Math.Abs(FontSize - other.FontSize) >= double.Epsilon;
  }

  public override bool Equals(object obj) {
    return obj is TextViewSettingsBase settings &&
           FontName == settings.FontName &&
           Math.Abs(FontSize - settings.FontSize) < double.Epsilon &&
           BackgroundColor == settings.BackgroundColor &&
           AlternateBackgroundColor == settings.AlternateBackgroundColor &&
           MarginBackgroundColor == settings.MarginBackgroundColor &&
           TextColor == settings.TextColor &&
           SelectedValueColor == settings.SelectedValueColor &&
           HighlightCurrentLine == settings.HighlightCurrentLine &&
           ShowBlockSeparatorLine == settings.ShowBlockSeparatorLine &&
           BlockSeparatorColor == settings.BlockSeparatorColor &&
           CurrentLineBorderColor == settings.CurrentLineBorderColor &&
           ProfileMarkerSettings.Equals(settings.ProfileMarkerSettings) &&
           ColumnSettings.Equals(settings.ColumnSettings);
  }

  public override string ToString() {
      return $"FontName:{FontName}\n" +
              $"FontSize:{FontSize}\n" +
              $"BackgroundColor:{BackgroundColor}\n" +
              $"AlternateBackgroundColor:{AlternateBackgroundColor}\n" +
              $"MarginBackgroundColor:{MarginBackgroundColor}\n" +
              $"TextColor:{TextColor}\n" +
              $"SelectedValueColor:{SelectedValueColor}\n" +
              $"ShowBlockSeparatorLine:{ShowBlockSeparatorLine}\n" +
              $"HighlightCurrentLine:{HighlightCurrentLine}\n" +
              $"BlockSeparatorColor:{BlockSeparatorColor}\n" +
              $"CurrentLineBorderColor:{CurrentLineBorderColor}\n" +
              $"ProfileMarkerSettings:{ProfileMarkerSettings}\n" +
              $"ColumnSettings:{ColumnSettings}";
  }
}