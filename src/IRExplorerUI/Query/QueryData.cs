using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Windows.Media;
using IRExplorerCore.IR;

namespace IRExplorerUI.Query {
    public class QueryData : INotifyPropertyChanged {
        private int nextId_;

        public QueryData() {
            InputValues = new List<QueryValue>();
            OutputValues = new List<QueryValue>();
            Buttons = new List<QueryButton>();
        }

        public List<QueryValue> InputValues { get; set; }
        public List<QueryValue> OutputValues { get; set; }
        public List<QueryButton> Buttons { get; set; }
        public bool HasInputValuesSwitchButton { get; set; }
        public IElementQuery Instance { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler ValueChanged;

        public QueryValue AddInput(QueryValue value) {
            value.Kind |= QueryValueKind.Input;
            value.PropertyChanged += InputValueChanged;
            InputValues.Add(value);
            OnPropertyChange(nameof(InputValues));
            return value;
        }

        private void InputValueChanged(object sender, PropertyChangedEventArgs e) {
            // Trigger a value changed event only if the actual value is modified,
            // not for flags like IsWarning which can be set from within the query execution,
            // which could end up in stack overflow by repeatedly executing the query.
            if (e.PropertyName == "Value") {
                ValueChanged?.Invoke(this, e);
            }
        }

        private int GetNextId() {
            return nextId_++;
        }

        public QueryButton AddButton(string name, EventHandler<object> action = null, object data = null) {
            var button = new QueryButton(name, action, data);
            Buttons.Add(button);
            OnPropertyChange(nameof(Buttons));
            return button;
        }

        public void ClearButtons() {
            Buttons.Clear();
            OnPropertyChange(nameof(Buttons));
        }

        public void RemoveButton(string name) {
            Buttons.RemoveAll((button) => button.Text == name);
            OnPropertyChange(nameof(Buttons));
        }

        public QueryButton ReplaceButton(string name, EventHandler<object> action = null, object data = null) {
            RemoveButton(name);
            return AddButton(name, action, data);
        }
        public void AddInputs(object inputObject) {
            // Use reflection to add the corresponding input value
            // for each of the properties.
            var inputType = inputObject.GetType();

            foreach (var property in inputType.GetProperties()) {
                if (!property.CanRead || !property.CanWrite) {
                    continue;
                }

                // Extract name and description from attributes.
                string name = "";
                string description = "";
                var attributes = property.GetCustomAttributes(true);

                foreach (var attr in attributes) {
                    if (attr is DisplayNameAttribute nameAttr) {
                        name = nameAttr.DisplayName;
                    }
                    if (attr is DescriptionAttribute descrAttr) {
                        description = descrAttr.Description;
                    }
                }

                // Determine the value type.
                var valueType = property.PropertyType;
                var valueKind = QueryValueKind.Other;

                switch (valueType) {
                    case var _ when valueType == typeof(bool): {
                        valueKind = QueryValueKind.Bool;
                        break;
                    }
                    case var _ when valueType == typeof(int): {
                        valueKind = QueryValueKind.Number;
                        break;
                    }
                    case var _ when valueType == typeof(string): {
                        valueKind = QueryValueKind.String;
                        break;
                    }
                    case var _ when valueType == typeof(Color): {
                        valueKind = QueryValueKind.Color;
                        break;
                    }
                }

                if (valueKind == QueryValueKind.Other) {
                    continue;
                }

                // If no name was provided, use the property name.
                if (string.IsNullOrEmpty(name)) {
                    name = property.Name;
                }

                // Use current property value.
                var value = property.GetValue(inputObject);
                var queryValue = AddInput(name, valueKind, value, description);
                queryValue.Tag = property;
            }
        }

        public object ExtractInputs(Type outputType) {
            var options = Activator.CreateInstance(outputType);

            foreach (var inputValue in InputValues) {
                // Consider input values mapped to properties of T.
                if (inputValue.Tag is PropertyInfo propertyTag &&
                    outputType.GetProperty(propertyTag.Name) != null) {
                    // For numbers, try to convert to int.
                    if (inputValue.Kind.HasFlag(QueryValueKind.Number)) {
                        if (!ExtractInt(inputValue, out int result)) {
                            return null;
                        }

                        propertyTag.SetValue(options, result);
                        continue;
                    }

                    propertyTag.SetValue(options, inputValue.Value);
                }
            }

            return options;
        }

        private static bool ExtractInt(QueryValue queryValue, out int number) {
            try {
                number = Convert.ToInt32(queryValue.Value);
                return true;
            }
            catch (Exception ex) {
                number = 0;
                return false;
            }
        }

        public QueryValue AddInput(string name, QueryValueKind kind, string description = null) {
            return AddInput(new QueryValue(GetNextId(), name, QueryValue.GetDefaultValue(kind), kind, description));
        }

        public QueryValue AddInput(string name, QueryValueKind kind, object initialValue, string description = null) {
            return AddInput(new QueryValue(GetNextId(), name, initialValue, kind, description));
        }

        public T GetInput<T>(string name) {
            foreach (var value in InputValues) {
                if (value.Name == name) {
                    return ExtractInputValue<T>(value);
                }
            }

            throw new InvalidOperationException($"Input value not found: {name}");
        }

        public T GetInput<T>(int id) {
            foreach (var value in InputValues) {
                if (value.Id == id) {
                    return ExtractInputValue<T>(value);
                }
            }

            throw new InvalidOperationException($"Input value not found: {id}");
        }

        private static T ExtractInputValue<T>(QueryValue value) {
            if (value.Value == null) {
                throw new InvalidOperationException($"Input value not set: {value.Name}");
            }

            if (value.Kind.HasFlag(QueryValueKind.Number)) {
                if (!ExtractInt(value, out int result)) {
                    throw new InvalidOperationException($"Invalid input number: {value.Name}");
                }

                return (T)((object)result);
            }

            return (T)value.Value;
        }

        public void AddOutput(string name, QueryValueKind kind, string description = null) {
            SetOutput(name, QueryValue.GetDefaultValue(kind), kind, description);
        }

        public void SetOutput(string name, object value, QueryValueKind kind, string description = null) {
            var existingValue = OutputValues.Find(item => item.Name == name);

            if (existingValue != null) {
                existingValue.Kind = kind;
                existingValue.Value = value;
            }
            else {
                var outputValue = new QueryValue(GetNextId(), name, value, kind, description);
                outputValue.PropertyChanged += OutputValueChanged;
                OutputValues.Add(outputValue);
                OnPropertyChange(nameof(OutputValues));
            }
        }

        private void OutputValueChanged(object sender, PropertyChangedEventArgs e) {
            OnPropertyChange(nameof(OutputValues));
        }

        public void SetOutput(string name, object value, string description = null) {
            SetOutput(name, value, QueryValueKind.Other, description);
        }

        public void SetOutput(string name, bool value, string description = null) {
            SetOutput(name, value, QueryValueKind.Bool, description);
        }

        public void SetOutput(string name, int value, string description = null) {
            SetOutput(name, value, QueryValueKind.Number, description);
        }

        public void SetOutput(string name, long value, string description = null) {
            SetOutput(name, value, QueryValueKind.Number, description);
        }

        public void SetOutput(string name, float value, string description = null) {
            SetOutput(name, value, QueryValueKind.Number, description);
        }

        public void SetOutput(string name, double value, string description = null) {
            SetOutput(name, value, QueryValueKind.Number, description);
        }

        public void SetOutput(string name, IRElement value, string description = null) {
            SetOutput(name, value, QueryValueKind.Element, description);
        }

        public void SetOutput(string name, string value, string description = null) {
            SetOutput(name, value, QueryValueKind.String, description);
        }

        public void SetOutput<T>(string name, IList<T> value, string description = null) where T : class {
            SetOutput(name, value, QueryValueKind.List, description);
        }

        public void ResetWarningFlags() {
            foreach (var value in OutputValues) {
                value.IsWarning = false;
            }

            foreach (var value in InputValues) {
                value.IsWarning = false;
            }
        }

        public void ResetOutputValues() {
            foreach (var value in OutputValues) {
                value.ResetValue();
            }
        }

        public void ResetResults() {
            ResetOutputValues();
            ResetWarningFlags();
        }

        public void SetOutputWarning(string name, string message = null) {
            var value = SetOutputMessage(name);
            value.SetWarning(message);
            OnPropertyChange(nameof(OutputValues));
        }

        public void SetOutputInfo(string name, string message = null) {
            var value = SetOutputMessage(name);
            OnPropertyChange(nameof(OutputValues));
        }

        private QueryValue SetOutputMessage(string name) {
            var existingValue = OutputValues.Find(item => item.Name == name);

            if (existingValue != null) {
                return existingValue;
            }
            else {
                var outputValue = new QueryValue(GetNextId(), name, null, QueryValueKind.Other);
                outputValue.PropertyChanged += OutputValueChanged;
                OutputValues.Add(outputValue);
                return outputValue;
            }
        }

        public void SetInputWarning(string name, string message = null) {
            var existingValue = InputValues.Find(item => item.Name == name);

            if (existingValue != null) {
                existingValue.SetWarning(message);
                OnPropertyChange(nameof(InputValues));
            }
        }

        public void OnPropertyChange(string propertyname) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyname));
        }
    }
}
