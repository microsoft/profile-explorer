using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using CoreLib.IR;

namespace Client.Scripting {
    [Flags]
    public enum QueryValueKind {
        Other = 0,
        Bool = 1 << 0,
        Number = 1 << 1,
        String = 1 << 2,
        Element = 1 << 3,
        List = 1 << 7,
        Input = 1 << 8
    }

    // Needs a notification event so that the query is redone on change
    public class QueryValue : BindableObject {
        private string description_;
        private bool isWarning_;
        private QueryValueKind kind_;
        private string name_;
        private object value_;
        private string warningMessage_;

        public QueryValue(string name, QueryValueKind kind, string description = null) {
            Name = name;
            Kind = kind;
            Description = description;
        }

        public QueryValue(string name, object value, QueryValueKind kind, string description = null) {
            Name = name;
            Kind = kind;
            Value = value;
            Description = description;
        }

        public QueryValueKind Kind {
            get => kind_;
            set => SetAndNotify(ref kind_, value);
        }

        public string Name {
            get => name_;
            set => SetAndNotify(ref name_, value);
        }

        public string Description {
            get => description_;
            set => SetAndNotify(ref description_, value);
        }

        public bool IsWarning {
            get => isWarning_;
            set => SetAndNotify(ref isWarning_, value);
        }

        public string WarningMessage {
            get => warningMessage_;
            set => SetAndNotify(ref warningMessage_, value);
        }

        public object Value {
            get => value_;
            set => SetAndNotify(ref value_, value);
        }

        public bool IsBool => Kind.HasFlag(QueryValueKind.Bool);
        public bool IsNumber => Kind.HasFlag(QueryValueKind.Number);
        public bool IsString => Kind.HasFlag(QueryValueKind.Number);
        public bool IsElement => Kind.HasFlag(QueryValueKind.Element);
        public bool IsList => Kind.HasFlag(QueryValueKind.List);
        public bool IsInput => Kind.HasFlag(QueryValueKind.Input);
        public bool IsOutput => !Kind.HasFlag(QueryValueKind.Input);

        public static object GetDefaultValue(QueryValueKind kind) {
            return kind switch {
                QueryValueKind.Bool    => false,
                QueryValueKind.Element => null,
                QueryValueKind.Number  => 0,
                QueryValueKind.String  => "",
                _                      => null
            };
        }

        public void SetWarning(string message = null) {
            WarningMessage = message;
            IsWarning = true;
        }

        public void ResetValue() {
            Value = GetDefaultValue(kind_);
            IsWarning = false;
        }
    }

    public class QueryData : INotifyPropertyChanged {
        public QueryData() {
            InputValues = new List<QueryValue>();
            OutputValues = new List<QueryValue>();
        }

        public List<QueryValue> InputValues { get; set; }
        public List<QueryValue> OutputValues { get; set; }
        public bool HasInputValuesSwitchButton { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler ValueChanged;

        public void AddInput(QueryValue value) {
            value.Kind |= QueryValueKind.Input;
            value.PropertyChanged += InputValueChanged;
            InputValues.Add(value);
            OnPropertyChange("InputValues");
        }

        private void InputValueChanged(object sender, PropertyChangedEventArgs e) {
            ValueChanged?.Invoke(this, e);
        }

        public void AddInput(string name, QueryValueKind kind, string description = null) {
            AddInput(new QueryValue(name, QueryValue.GetDefaultValue(kind), kind | QueryValueKind.Input,
                                    description));
        }

        public T GetInput<T>(string name) {
            foreach (var value in InputValues) {
                if (value.Name == name) {
                    if (value.Value == null) {
                        throw new InvalidOperationException($"Input value not set: {name}");
                    }

                    return (T) value.Value;
                }
            }

            throw new InvalidOperationException($"Input value not found: {name}");
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
                var outputValue = new QueryValue(name, value, kind, description);
                outputValue.PropertyChanged += OutputValueChanged;
                OutputValues.Add(outputValue);
                OnPropertyChange("OutputValues");
            }
        }

        private void OutputValueChanged(object sender, PropertyChangedEventArgs e) {
            OnPropertyChange("OutputValues");
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
            var existingValue = OutputValues.Find(item => item.Name == name);

            if (existingValue != null) {
                existingValue.SetWarning(message);
                OnPropertyChange("OutputValues");
            }
            else {
                var outputValue = new QueryValue(name, null, QueryValueKind.Other);
                outputValue.PropertyChanged += OutputValueChanged;
                OutputValues.Add(outputValue);
                OnPropertyChange("OutputValues");
            }
        }

        public void SetInputWarning(string name, string message = null) {
            var existingValue = InputValues.Find(item => item.Name == name);

            if (existingValue != null) {
                existingValue.SetWarning(message);
                OnPropertyChange("InputValues");
            }
        }

        public void OnPropertyChange(string propertyname) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyname));
        }
    }

    public interface IElementQuery {
        public ElementQueryInfo GetDefinition();
        public bool Execute(QueryData data);
    }

    public class ElementQueryInfo : INotifyPropertyChanged {
        private IElementQuery parent_;

        public ElementQueryInfo(IElementQuery parent) {
            Data = new QueryData();
            Data.ValueChanged += InputValueChanged;
            Data.PropertyChanged += DataPropertyChanged;
            parent_ = parent;
        }

        public ElementQueryInfo(IElementQuery parent, string name, string description) : this(parent) {
            Name = name;
            Description = description;
        }

        public string Name { get; set; }
        public string Description { get; set; }
        public QueryData Data { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        private void InputValueChanged(object sender, EventArgs e) {
            if (AreAllInputValuesSet()) {
                parent_.Execute(Data);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Data"));
            }
        }

        private bool AreAllInputValuesSet() {
            return Data.InputValues.FindIndex(item => item.Value == null) == -1;
        }

        private void DataPropertyChanged(object sender, PropertyChangedEventArgs e) {
            PropertyChanged?.Invoke(this, e);
        }
    }

    // Two parts: a static? method that returns a QueryData,
    // having the required input/output fields defined
    // The actual "execute" method that gets the QueryData,
    // runs the script and populates the output values

    public class ElementQueryScript : Script, IElementQuery {
        public ElementQueryInfo Query { get; set; }

        public ElementQueryInfo GetDefinition() {
            throw new NotImplementedException();
        }

        public bool Execute(QueryData data) {
            MessageBox.Show("Execute ElementQueryScript");
            return true;
        }

        public override bool Execute(ScriptSession session) {
            return base.Execute(session);
        }
    }

    public class BuiltinElementQuery : IElementQuery {
        public ElementQueryInfo GetDefinition() {
            throw new NotImplementedException();
        }

        public bool Execute(QueryData data) {
            //MessageBox.Show("Execute BuiltinElementQuery");
            data.SetOutput("Output A", new Random().NextDouble() > 0.5);
            data.SetOutput("Output B", new Random().NextDouble() > 0.5);
            data.SetOutput("Output C", new Random().Next(10000));
            return true;
        }
    }
}
