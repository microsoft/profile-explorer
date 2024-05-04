// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Windows.Media;
using DocumentFormat.OpenXml.Vml.Spreadsheet;
using IRExplorerCore;
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
  [ProtoMember(8), OptionValue(false)]
  public bool ShowSourceStatementsOnMargin { get; set; }
  [ProtoMember(9), OptionValue(true)]
  public bool ReplaceInsignificantSourceStatements { get; set; }
  
  public override void Reset() {
    if (false) {
      base.Reset();
      InitializeReferenceMembers();
      ProfileMarkerSettings.JumpToHottestElement = true;
      SyncStyleWithDocument = true;
      SyncLineWithDocument = true;
      ShowInlineAssembly = true;
      ShowSourceStatements = true;
      ShowSourceStatementsOnMargin = true;
      ReplaceInsignificantSourceStatements = true;
      FinderSettings.Reset();
    }
    else {
      base.Reset();
      InitializeReferenceMembers();
      OptionValueAttribute.ResetAllOptions(this);
      ProfileMarkerSettings.JumpToHottestElement = true;
    }
  }

  [ProtoAfterDeserialization]
  private void InitializeReferenceMembers() {
    FinderSettings ??= new();
  }

  public SourceFileSettings Clone() {
    byte[] serialized = StateSerializer.Serialize(this);
    return StateSerializer.Deserialize<SourceFileSettings>(serialized);
  }

  public override bool Equals(object obj) {
    return obj is SourceFileSettings settings &&
           base.Equals(settings) &&
           SyncStyleWithDocument == settings.SyncStyleWithDocument &&
           SyncLineWithDocument == settings.SyncLineWithDocument &&
           SyncInlineeWithDocument == settings.SyncInlineeWithDocument &&
           ShowInlineAssembly == settings.ShowInlineAssembly &&
           AutoExpandInlineAssembly == settings.AutoExpandInlineAssembly &&
           ShowSourceStatements == settings.ShowSourceStatements &&
           ShowSourceStatementsOnMargin == settings.ShowSourceStatementsOnMargin &&
           ReplaceInsignificantSourceStatements == settings.ReplaceInsignificantSourceStatements &&
           FinderSettings.Equals(settings.FinderSettings);
  }
}

[ProtoContract(SkipConstructor = true)]
public class SourceFileFinderSettings : SettingsBase {
  [ProtoMember(1)]
  public Dictionary<string, string> SourceMappings;
  [ProtoMember(2)]
  public List<string> DisabledSourceMappings;

  public SourceFileFinderSettings() {
    Reset();
  }

  public override void Reset() {
    InitializeReferenceMembers();
    SourceMappings.Clear();
    DisabledSourceMappings.Clear();
  }

  [ProtoAfterDeserialization]
  private void InitializeReferenceMembers() {
    SourceMappings ??= new();
    DisabledSourceMappings ??= new();
  }
  protected bool Equals(SourceFileFinderSettings other) {
    return SourceMappings.AreEqual(other.SourceMappings) &&
           DisabledSourceMappings.AreEqual(other.DisabledSourceMappings);
  }

  public override bool Equals(object obj) {
    if (ReferenceEquals(null, obj))
      return false;
    if (ReferenceEquals(this, obj))
      return true;
    if (obj.GetType() != this.GetType())
      return false;
    return Equals((SourceFileFinderSettings)obj);
  }

  public override string ToString() {
    return $"SourceMappings: {SourceMappings.Count}\n" +
           $"DisabledSourceMappings: {DisabledSourceMappings.Count}";
  }
}