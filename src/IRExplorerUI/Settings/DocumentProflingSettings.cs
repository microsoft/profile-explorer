// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using ProtoBuf;

namespace IRExplorerUI;

[ProtoContract(SkipConstructor = true)]
public class DocumentProfilingSettings : SettingsBase {
  public DocumentProfilingSettings() {
    Reset();
  }

  //? TODO: Options for
  //? - grouping by thread na
  //? - custom colors for thread names
  //? - show backtrace preview on hover
  //?    - max depth
  //?    - hover time
  //?    - same settings in SectionPanel
  public override void Reset() {
  }

  public DocumentProfilingSettings Clone() {
    byte[] serialized = StateSerializer.Serialize(this);
    return StateSerializer.Deserialize<DocumentProfilingSettings>(serialized);
  }

  public override bool Equals(object obj) {
    return obj is DocumentProfilingSettings settings;
  }
}