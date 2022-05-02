using System;
using System.Windows.Media;
using ProtoBuf;

namespace IRExplorerUI {
    [ProtoContract(SkipConstructor = true)]
    public class CallTreeSettings : SettingsBase {
        public CallTreeSettings() {
            Reset();
        }

        [ProtoMember(1)]
        public bool CombineInstances { get; set; }
        [ProtoMember(2)]
        public bool PrependModuleToFunction { get; set; }
        [ProtoMember(3)]
        public bool ShowTimeAfterPercentage { get; set; }

        public override void Reset() {
            CombineInstances = true;
            PrependModuleToFunction = true;
            ShowTimeAfterPercentage = true;
        }

        public override SettingsBase Clone() {
            var serialized = StateSerializer.Serialize(this);
            return StateSerializer.Deserialize<CallTreeSettings>(serialized);
        }

        public override bool Equals(object obj) {
            return obj is CallTreeSettings settings &&
                   CombineInstances == settings.CombineInstances &&
                   PrependModuleToFunction == settings.PrependModuleToFunction &&
                   ShowTimeAfterPercentage == settings.ShowTimeAfterPercentage;
        }
    }
}
