﻿// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using ProtoBuf;

namespace IRExplorerUI;

[ProtoContract(SkipConstructor = true)]
public class SourceFileSettings : SettingsBase {
  public SourceFileSettings() {
    Reset();
  }

  [ProtoMember(1)]
  public bool Foo { get; set; }
  //? TODO: Options for
  //? - font, font size
  //? - syntax highlighting
  //? - other options from DocumentSettings

  public override void Reset() {
    Foo = true;
  }

  public SourceFileSettings Clone() {
    byte[] serialized = StateSerializer.Serialize(this);
    return StateSerializer.Deserialize<SourceFileSettings>(serialized);
  }

  public override bool Equals(object obj) {
    return obj is SourceFileSettings settings &&
           Foo == settings.Foo;
  }
}