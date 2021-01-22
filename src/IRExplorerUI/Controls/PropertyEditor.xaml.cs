using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace IRExplorerUI.Controls {
    //! Option to get background color for listview item

    public partial class PropertyEditor : UserControl {
        private List<object> values_;
        private ObservableCollectionRefresh<object> valuesView_;

        public PropertyEditor() {
            InitializeComponent();
            Editor.PropertyValueChanged += Editor_PropertyValueChanged;
        }

        public class ValueArgs : EventArgs {
            public ValueArgs() { }

            public ValueArgs(object value) {
                Value = value;
            }

            public object Value { get; set; }
        }

        public event EventHandler<ValueArgs> CreateValue;
        public event EventHandler<ValueArgs> ValueRemoved;
        public event EventHandler PropertyChanged;

        public void Initialize<T>(List<T> values) where T : class {
            Values = values.Cast<object>().ToList();
        }

        public bool HasChanges { get; set; }

        public List<object> Values {
            get => values_;
            set {
                if (value != values_) {
                    values_ = value;
                    valuesView_ = new ObservableCollectionRefresh<object>(values_);
                    ValueList.ItemsSource = valuesView_;

                    if (values_.Count > 0) {
                        ValueList.SelectedItem = values_[0];
                    }
                }
            }
        }

        private void ValueList_OnSelectionChanged(object sender, SelectionChangedEventArgs e) {
            Editor.SelectedObject = ValueList.SelectedItem;
        }

        private void AddNewValue() {
            var args = new ValueArgs();
            CreateValue?.Invoke(this, args);

            if (args.Value != null) {
                InsertValue(values_.Count, args.Value);
            }
        }

        private void InsertValue(int index, object value) {
            values_.Insert(index, value);
            valuesView_.Insert(index, value);
            ValueList.SelectedItem = value;
            HasChanges = true;
        }

        private int RemoveValue(object value, bool triggerEvent = true) {
            int index = values_.IndexOf(value);

            if (index != -1) {
                values_.RemoveAt(index);
                valuesView_.RemoveAt(index);
                HasChanges = true;

                if (triggerEvent) {
                    ValueRemoved?.Invoke(this, new ValueArgs(value));
                }
            }

            return index;
        }

        private void MoveValueUp(object value) {
            int prevIndex = RemoveValue(value, false);

            if (prevIndex != -1) {
                int newIndex = Math.Max(0, prevIndex - 1);
                InsertValue(newIndex, value);
            }
        }

        private void MoveValueDown(object value) {
            int prevIndex = RemoveValue(value, false);

            if (prevIndex != -1) {
                int newIndex = Math.Min(values_.Count, prevIndex + 1);
                InsertValue(newIndex, value);
            }
        }

        private void MoveUpButton_Click(object sender, RoutedEventArgs e) {
            if (ValueList.SelectedItem != null) {
                MoveValueUp(ValueList.SelectedItem);
            }
        }

        private void MoveDownButton_Click(object sender, RoutedEventArgs e) {
            if (ValueList.SelectedItem != null) {
                MoveValueDown(ValueList.SelectedItem);
            }
        }

        private void ToolBar_Loaded(object sender, RoutedEventArgs e) {
            Utils.PatchToolbarStyle(sender as ToolBar);
        }

        private void RemoveButton_Click(object sender, RoutedEventArgs e) {
            if (ValueList.SelectedItem != null) {
                RemoveValue(ValueList.SelectedItem);
            }
        }

        private void AddButton_Click(object sender, RoutedEventArgs e) {
            AddNewValue();
        }

        private void Editor_PropertyValueChanged(object sender, Xceed.Wpf.Toolkit.PropertyGrid.PropertyValueChangedEventArgs e) {
            valuesView_.Refresh();
            HasChanges = true;
            PropertyChanged?.Invoke(this, null);
        }

    }
}
