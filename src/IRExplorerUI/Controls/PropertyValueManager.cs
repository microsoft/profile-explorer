// unset

using System.Collections.Generic;

namespace IRExplorerUI.Controls {
    public abstract class PropertyValueManager {
        public string EditorTitle { get; set; }
        public bool HasChanges { get; set; }
        public abstract List<object> LoadValues();
        public abstract bool SaveValues(List<object> values);
        public abstract object CreateNewValue();
        public abstract List<object> ResetValues();

        public virtual bool OnValueRemoved(object value) {
            return true;
        }

        public virtual void OnValueChanged(object value) {

        }

        public virtual string GetValueName(object value) {
            return value.ToString();
        }
    }
}