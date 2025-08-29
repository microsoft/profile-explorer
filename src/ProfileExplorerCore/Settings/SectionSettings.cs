// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using ProfileExplorer.Core.Providers;
using ProtoBuf;

namespace ProfileExplorer.Core.Settings;

/// <summary>
/// Core section settings with properties needed by the Core project.
/// This is a minimal implementation that can be overridden by UI-specific settings.
/// </summary>
[ProtoContract(SkipConstructor = true)]
public class SectionSettings : SettingsBase {
  public SectionSettings() {
    Reset();
  }

  // Properties needed by Core project
  public virtual bool ShowDemangledNames { get; set; } = true;
  public virtual FunctionNameDemanglingOptions DemanglingOptions { get; set; } = FunctionNameDemanglingOptions.Default;

  public override void Reset() {
    ShowDemangledNames = true;
    DemanglingOptions = FunctionNameDemanglingOptions.Default;
  }

  public virtual SectionSettings Clone() {
    return new SectionSettings 
    {
      ShowDemangledNames = this.ShowDemangledNames,
      DemanglingOptions = this.DemanglingOptions
    };
  }

  public override bool Equals(object obj) {
    if (obj is SectionSettings other)
    {
      return ShowDemangledNames == other.ShowDemangledNames &&
             DemanglingOptions == other.DemanglingOptions;
    }
    return false;
  }

  public override string ToString() {
    return $"ShowDemangledNames: {ShowDemangledNames}, DemanglingOptions: {DemanglingOptions}";
  }
}
