// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using ProtoBuf;

namespace IRExplorerUI;

[ProtoContract(SkipConstructor = true)]
public class IRDocumentPopupSettings : SettingsBase {
  public IRDocumentPopupSettings() {
    Reset();
  }

  public double Width { get; set; }
  public double Height { get; set; }
  public bool ShowSourceFile { get; set; }
  // Font, font size, doc style

  public override void Reset() {
  }

  public IRDocumentPopupSettings Clone() {
    byte[] serialized = StateSerializer.Serialize(this);
    return StateSerializer.Deserialize<IRDocumentPopupSettings>(serialized);
  }

  public override bool Equals(object obj) {
    return obj is IRDocumentPopupSettings settings;
  }
}
