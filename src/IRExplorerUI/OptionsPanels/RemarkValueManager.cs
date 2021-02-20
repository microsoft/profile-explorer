// unset

using System;
using System.Collections.Generic;
using IRExplorerUI.Controls;

namespace IRExplorerUI.OptionsPanels {
    public class RemarkValueManager : PropertyValueManager {
        public enum ValueType {
            Category,
            Boundary,
            Highlight
        }

        private ICompilerInfoProvider compilerInfo_;
        private ValueType valueType_;

        public RemarkValueManager(ValueType valueType, ICompilerInfoProvider compilerInfo) {
            valueType_ = valueType;
            compilerInfo_ = compilerInfo;
        }

        public event EventHandler ValueChanged;

        public override List<object> LoadValues() {
            var provider = compilerInfo_.RemarkProvider;

            if (provider.LoadSettings()) {
                return valueType_ switch {
                    ValueType.Category => provider.RemarkCategories.ToObjectList(),
                    ValueType.Boundary => provider.RemarkSectionBoundaries.ToObjectList(),
                    ValueType.Highlight => provider.RemarkTextHighlighting.ToObjectList(),
                    _ => throw new ArgumentOutOfRangeException()
                };
            }

            return null;
        }

        public override void UpdateValues(List<object> values) {
            if (HasChanges) {
                var provider = compilerInfo_.RemarkProvider;

                switch (valueType_) {
                    case ValueType.Category: {
                        provider.RemarkCategories = values.ConvertAll(item => (RemarkCategory)item);
                        break;
                    }
                    case ValueType.Boundary: {
                        provider.RemarkSectionBoundaries = values.ConvertAll(item => (RemarkSectionBoundary)item);
                        break;
                    }
                    case ValueType.Highlight: {
                        provider.RemarkTextHighlighting = values.ConvertAll(item => (RemarkTextHighlighting)item);
                        break;
                    }
                }
            }
        }

        public override bool SaveValues(List<object> values) {
            if (HasChanges) {
                UpdateValues(values);
                return compilerInfo_.RemarkProvider.SaveSettings();
            }

            return true;
        }

        public override object CreateNewValue() {
            return valueType_ switch {
                ValueType.Category => new RemarkCategory(),
                ValueType.Boundary => new RemarkSectionBoundary(),
                ValueType.Highlight => new RemarkTextHighlighting(),
                _ => throw new ArgumentOutOfRangeException()
            };
        }

        public override List<object> ResetValues() {
            return null;
        }

        public override void OnValueChanged(object value) {
            ValueChanged?.Invoke(this, null);
        }
    }
}