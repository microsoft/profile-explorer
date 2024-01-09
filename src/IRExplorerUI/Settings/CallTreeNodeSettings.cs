// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using ProtoBuf;

namespace IRExplorerUI;

[ProtoContract(SkipConstructor = true)]
public class CallTreeNodeSettings : SettingsBase {
  public static readonly int DefaultPreviewPopupDuration = HoverPreview.LongHoverDuration.Milliseconds;

  public CallTreeNodeSettings() {
    Reset();
  }
  
  [ProtoMember(1)]
  public bool ShowPreviewPopup { get; set; }
  [ProtoMember(2)]
  public int PreviewPopupDuration { get; set; }
  [ProtoMember(3)]
  public bool ExpandInstances { get; set; }
  [ProtoMember(4)]
  public bool ExpandHistogram { get; set; }
  [ProtoMember(5)]
  public bool PrependModuleToFunction { get; set; }
  [ProtoMember(6)]
  public bool ShowSourcePreviewPopup { get; set; }
  
  public override void Reset() {
    ShowPreviewPopup = true;
    ExpandInstances = true;
    PrependModuleToFunction = true;
    PreviewPopupDuration = DefaultPreviewPopupDuration;
  }

  public CallTreeNodeSettings Clone() {
    byte[] serialized = StateSerializer.Serialize(this);
    return StateSerializer.Deserialize<CallTreeNodeSettings>(serialized);
  }

  public override bool Equals(object obj) {
    return obj is CallTreeNodeSettings settings &&
           ShowPreviewPopup == settings.ShowPreviewPopup &&
           PreviewPopupDuration == settings.PreviewPopupDuration &&
           ExpandInstances == settings.ExpandInstances &&
           ExpandHistogram == settings.ExpandHistogram &&
           PrependModuleToFunction == settings.PrependModuleToFunction &&
           ShowSourcePreviewPopup == settings.ShowSourcePreviewPopup;
  }
}
