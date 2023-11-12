// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using ProtoBuf;

namespace IRExplorerUI.Settings;

[ProtoContract(SkipConstructor = true)]
class FunctionTaskSettings : SettingsBase {
  [ProtoMember(1)]
  public double OutputPanelWidth { get; set; }
  [ProtoMember(2)]
  public double OutputPanelHeight { get; set; }
}
