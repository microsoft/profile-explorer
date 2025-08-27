// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.ComponentModel;
using System.Windows.Media;
using ProfileExplorer.Core.Settings;
using ProtoBuf;

namespace ProfileExplorer.UI;

[ProtoContract(SkipConstructor = true)]
public class UIDiffSettings : DiffSettings {
  [ProtoMember(13)][OptionValue("#FFF6D9")]
  public Color ModificationColor { get; set; }
  [ProtoMember(14)][OptionValue("#ff6f00")]
  public Color ModificationBorderColor { get; set; }
  [ProtoMember(15)][OptionValue("#E2F0D3")]
  public Color InsertionColor { get; set; }
  [ProtoMember(16)][OptionValue("#7FA72E")]
  public Color InsertionBorderColor { get; set; }
  [ProtoMember(17)][OptionValue("#FFE8EA")]
  public Color DeletionColor { get; set; }
  [ProtoMember(18)][OptionValue("#B33232")]
  public Color DeletionBorderColor { get; set; }
  [ProtoMember(19)][OptionValue("#E1E1E1")]
  public Color MinorModificationColor { get; set; }
  [ProtoMember(20)][OptionValue("#8F8F8F")]
  public Color MinorModificationBorderColor { get; set; }
  [ProtoMember(21)][OptionValue("#A9A9A9")]
  public Color PlaceholderBorderColor { get; set; }
  [ProtoMember(22)][OptionValue("")]
  public string ExternalDiffAppPath { get; set; }

  public UIDiffSettings Clone() {
    byte[] serialized = UIStateSerializer.Serialize(this);
    return UIStateSerializer.Deserialize<UIDiffSettings>(serialized);
  }
}