// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using ProfileExplorerCore.Settings;
using ProtoBuf;

namespace ProfileExplorer.UI.Settings;

[ProtoContract(SkipConstructor = true)]
class FunctionTaskSettings : SettingsBase {
  [ProtoMember(1)]
  public double OutputPanelWidth { get; set; }
  [ProtoMember(2)]
  public double OutputPanelHeight { get; set; }
}