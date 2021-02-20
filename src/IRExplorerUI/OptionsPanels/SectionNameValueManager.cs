// unset

using System;
using System.Collections.Generic;
using IRExplorerUI.Controls;

namespace IRExplorerUI.OptionsPanels {
    public class SectionNameValueManager : PropertyValueManager {
        private ICompilerInfoProvider compilerInfo_;

        public SectionNameValueManager(ICompilerInfoProvider compilerInfo) {
            compilerInfo_ = compilerInfo;
        }

        public event EventHandler ValueChanged;

        public override List<object> LoadValues() {
            var provider = compilerInfo_.SectionStyleProvider;

            if (provider.LoadSettings()) {
                return provider.SectionNameMarkers.ToObjectList();
            }

            return null;
        }

        public override void UpdateValues(List<object> values) {
            if (HasChanges) {
                var provider = compilerInfo_.SectionStyleProvider;
                provider.SectionNameMarkers = values.ConvertAll(item => (MarkedSectionName)item);
            }
        }

        public override bool SaveValues(List<object> values) {
            if (HasChanges) {
                UpdateValues(values);
                return compilerInfo_.SectionStyleProvider.SaveSettings();
            }

            return true;
        }

        public override object CreateNewValue() {
            return new MarkedSectionName();
        }

        public override List<object> ResetValues() {
            return null;
        }

        public override void OnValueChanged(object value) {
            ValueChanged?.Invoke(this, null);
        }
    }
}