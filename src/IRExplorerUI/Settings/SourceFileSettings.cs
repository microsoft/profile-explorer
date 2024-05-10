// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System.Collections.Generic;
using System.Windows.Media;
using ProtoBuf;

namespace IRExplorerUI;

[ProtoContract(SkipConstructor = true)]
public class SourceFileSettings : TextViewSettingsBase {
  public SourceFileSettings() {
    Reset();
  }

  [ProtoMember(1)]
  public SourceFileFinderSettings FinderSettings { get; set; }
  [ProtoMember(2), OptionValue(true)]
  public bool SyncStyleWithDocument { get; set; }
  [ProtoMember(3), OptionValue(true)]
  public bool SyncLineWithDocument { get; set; }
  [ProtoMember(4), OptionValue(false)]
  public bool SyncInlineeWithDocument { get; set; }
  [ProtoMember(5), OptionValue(true)]
  public bool ShowInlineAssembly { get; set; }
  [ProtoMember(6), OptionValue(false)]
  public bool AutoExpandInlineAssembly { get; set; }
  [ProtoMember(7), OptionValue(true)]
  public bool ShowSourceStatements { get; set; }
  [ProtoMember(8), OptionValue(true)]
  public bool ShowSourceStatementsOnMargin { get; set; }
  [ProtoMember(9), OptionValue(true)]
  public bool ReplaceInsignificantSourceStatements { get; set; }
  [ProtoMember(10), OptionValue(typeof(Color), "#0202B0")]
  public Color AssemblyTextColor { get; set; }
  [ProtoMember(11), OptionValue(typeof(Color), "#14828282")]
  public Color AssemblyBackColor { get; set; }

  public override void Reset() {
      base.Reset();
      InitializeReferenceMembers();
      ResetAllOptions(this);
      ProfileMarkerSettings.JumpToHottestElement = true;
  }

  [ProtoAfterDeserialization]
  private void InitializeReferenceMembers() {
    InitializeReferenceOptions(this);
  }

  public SourceFileSettings Clone() {
    byte[] serialized = StateSerializer.Serialize(this);
    return StateSerializer.Deserialize<SourceFileSettings>(serialized);
  }

  public override bool Equals(object obj) {
    return obj is SourceFileSettings settings &&
           base.Equals(settings) &&
           AreOptionsEqual(this, settings);
  }

  public override string ToString() {
    return base.ToString() + PrintOptions(this);
  }
}

[ProtoContract(SkipConstructor = true)]
public class SourceFileFinderSettings : SettingsBase {
  [ProtoMember(1), OptionValue()]
  public Dictionary<string, string> SourceMappings { get; set; }
  [ProtoMember(2), OptionValue()]
  public List<string> DisabledSourceMappings { get; set; }

  public SourceFileFinderSettings() {
    Reset();
  }

  public override void Reset() {
    InitializeReferenceMembers();
    ResetAllOptions(this);
  }

  [ProtoAfterDeserialization]
  private void InitializeReferenceMembers() {
    InitializeReferenceOptions(this);
  }

  public override bool Equals(object obj) {
    return AreOptionsEqual(this, obj);
  }

  public override string ToString() {
    return PrintOptions(this);
  }
}