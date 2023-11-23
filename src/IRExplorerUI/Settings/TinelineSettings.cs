// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using ProtoBuf;

namespace IRExplorerUI;

[ProtoContract(SkipConstructor = true)]
public class TimelineSettings : SettingsBase {
  public TimelineSettings() {
    Reset();
  }

  //? TODO: Options for
  //? - grouping
  //? - custom colors for thread names
  //? - show backtrace preview on hover
  //?    - max depth

  public override void Reset() {
  }

  public TimelineSettings Clone() {
    byte[] serialized = StateSerializer.Serialize(this);
    return StateSerializer.Deserialize<TimelineSettings>(serialized);
  }

  public override bool Equals(object obj) {
    return obj is TimelineSettings settings;
  }
}
