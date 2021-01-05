// unset

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows.Data;
using Xceed.Wpf.Toolkit;
using Xceed.Wpf.Toolkit.PropertyGrid;
using Xceed.Wpf.Toolkit.PropertyGrid.Editors;

namespace IRExplorerUI {
    public class EnumCheckComboBoxEditor : TypeEditor<CheckComboBox> {
        // Converter from enum to string and back.
        public class EnumConverter : IValueConverter {
            private Type enumType_;

            public EnumConverter(Type enumType) {
                enumType_ = enumType;
            }

            //? TODO: Could query the Description on the enum field to get the name
            //? TODO: If no flag is selected (default value 0), pick the field marked with DefaultValue
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
                // Form a comma-separated string with the set enum flags.
                int flags = (int)value;
                return String.Join(",", Enum.GetValues(enumType_).OfType<object>().
                    Where(v => (flags & (int)v) != 0).Select(v => v.ToString()));
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
                // Split the enum flags strings into the flag names and reconstruct the enum value.
                string valueString = (string)value;
                var flags = valueString.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).
                    Aggregate(0, (acc, val) => acc | (int)Enum.Parse(enumType_, val));

                return Enum.Parse(enumType_, flags.ToString());
            }
        }
        
        protected override void SetValueDependencyProperty() {
            ValueProperty = Xceed.Wpf.Toolkit.Primitives.Selector.SelectedItemsOverrideProperty;
        }

        protected override CheckComboBox CreateEditor() {
            return new PropertyGridEditorCheckComboBox();
        }

        protected override void ResolveValueBinding(PropertyItem propertyItem) {
            SetItemsSource(propertyItem);
            base.ResolveValueBinding(propertyItem);
        }

        private void SetItemsSource(PropertyItem propertyItem) {
            Editor.ItemsSource = CreateItemsSource(propertyItem);

            // Create a binding that gets triggered when the value changes.
            var _binding = new Binding("Value");
            _binding.Source = propertyItem;
            _binding.UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged;
            _binding.Mode = BindingMode.TwoWay;
            _binding.Converter = new EnumConverter(propertyItem.PropertyType);
            BindingOperations.SetBinding(Editor, CheckComboBox.SelectedValueProperty, _binding);

        }

        protected IEnumerable CreateItemsSource(PropertyItem propertyItem) {
            return GetValues(propertyItem.PropertyType);
        }

        private static object[] GetValues(Type enumType) {
            List<object> values = new List<object>();

            if (enumType != null) {
                var fields = enumType.GetFields().Where(x => x.IsLiteral);
                DefaultValueAttribute defaultValue = null;

                foreach (FieldInfo field in fields) {
                    // Get array of BrowsableAttribute attributes
                    object[] attrs = field.GetCustomAttributes(typeof(BrowsableAttribute), false);

                    if (attrs.Length == 1) {
                        // If attribute exists and its value is false continue to the next field...
                        BrowsableAttribute brAttr = (BrowsableAttribute)attrs[0];
                        if (!brAttr.Browsable) {
                            continue;
                        }
                    }

                    values.Add(field.GetValue(enumType));
                }
            }

            return values.ToArray();
        }
    }
}