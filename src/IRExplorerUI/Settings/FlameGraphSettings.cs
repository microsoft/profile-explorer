// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Windows.Media;
using ProtoBuf;

namespace IRExplorerUI {
    [ProtoContract(SkipConstructor = true)]
    public class FlameGraphSettings : SettingsBase {
        public FlameGraphSettings() {
            Reset();
        }

        [ProtoMember(1)]
        public bool PrependModuleToFunction { get; set; }
        [ProtoMember(2)]
        public bool ShowDetailsPanel { get; set; }
        [ProtoMember(3)]
        public bool SyncSourceFile { get; set; }
        [ProtoMember(4)]
        public bool SyncSelection{ get; set; }
        [ProtoMember(5)]
        public bool UseCompactMode { get; set; } // font size, node height

        public override void Reset() {
            PrependModuleToFunction = true;
            SyncSelection = true;
            SyncSourceFile = false;
            ShowDetailsPanel = true;
        }

        public FlameGraphSettings Clone() {
            var serialized = StateSerializer.Serialize(this);
            return StateSerializer.Deserialize<FlameGraphSettings>(serialized);
        }

        public override bool Equals(object obj) {
            return obj is FlameGraphSettings settings &&
                   PrependModuleToFunction == settings.PrependModuleToFunction &&
                   ShowDetailsPanel == settings.ShowDetailsPanel &&
                   SyncSelection == settings.SyncSelection &&
                   SyncSourceFile == settings.SyncSourceFile && 
                   UseCompactMode == settings.UseCompactMode;
        }
    }
}
