// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
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
    return obj is IRDocumentPopupSettings settings &&
           Math.Abs(Width - settings.Width) < double.Epsilon &&
           Math.Abs(Height - settings.Height) < double.Epsilon &&
           ShowSourceFile == settings.ShowSourceFile;
  }

  public override string ToString() {
    return $"Width: {Width}, Height: {Height}, ShowSourceFile: {ShowSourceFile}";
  }
}
