﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using IRExplorerCore.IR;

namespace IRExplorerUI.Query {
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

        public QueryValue(int id, string name, QueryValueKind kind, string description = null) {
            Id = id;
            Name = name;
            Kind = kind;
            Description = description;
        }

        public QueryValue(int id, string name, object value, QueryValueKind kind, string description = null) {
            Id = id;
            Name = name;
            Kind = kind;
            Value = value;
            Description = description;
        }

        public int Id { get; set; }

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
            return kind switch
            {
                QueryValueKind.Bool => false,
                QueryValueKind.Element => null,
                QueryValueKind.Number => 0,
                QueryValueKind.String => "",
                _ => null
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

    public class QueryButton : BindableObject {
        private string text_;
        private string description_;
        private bool hasBoldText_;
        private bool hasDemiBoldText_;
        private bool isEnabled_;

        public QueryButton(string text, EventHandler<object> action = null, object data = null) {
            Text = text;
            Action = action;
            Data = data;
            IsEnabled = true;
        }

        public string Text {
            get => text_;
            set => SetAndNotify(ref text_, value);
        }

        public string Description {
            get => description_;
            set => SetAndNotify(ref description_, value);
        }

        public bool HasBoldText {
            get => hasBoldText_;
            set => SetAndNotify(ref hasBoldText_, value);
        }

        public bool HasDemiBoldText {
            get => hasDemiBoldText_;
            set => SetAndNotify(ref hasDemiBoldText_, value);
        }

        public bool IsEnabled {
            get => isEnabled_ && Action != null;
            set => SetAndNotify(ref isEnabled_, value);
        }

        public EventHandler<object> Action { get; set; }
        public object Data { get; set; }
    }

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

        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler ValueChanged;

        public int AddInput(QueryValue value) {
            value.Kind |= QueryValueKind.Input;
            value.PropertyChanged += InputValueChanged;
            InputValues.Add(value);
            OnPropertyChange(nameof(InputValues));
            return value.Id;
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

        public int AddInput(string name, QueryValueKind kind, string description = null) {
            return AddInput(new QueryValue(GetNextId(), name, QueryValue.GetDefaultValue(kind), kind, description));
        }

        public T GetInput<T>(string name) {
            foreach (var value in InputValues) {
                if (value.Name == name) {
                    if (value.Value == null) {
                        throw new InvalidOperationException($"Input value not set: {name}");
                    }

                    return (T)value.Value;
                }
            }

            throw new InvalidOperationException($"Input value not found: {name}");
        }

        public T GetInput<T>(int id) {
            foreach (var value in InputValues) {
                if (value.Id == id) {
                    if (value.Value == null) {
                        throw new InvalidOperationException($"Input value not set: {id}, {value.Name}");
                    }

                    return (T)value.Value;
                }
            }

            throw new InvalidOperationException($"Input value not found: {id}");
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
            var existingValue = OutputValues.Find(item => item.Name == name);

            if (existingValue != null) {
                existingValue.SetWarning(message);
                OnPropertyChange(nameof(OutputValues));
            }
            else {
                var outputValue = new QueryValue(GetNextId(), name, null, QueryValueKind.Other);
                outputValue.PropertyChanged += OutputValueChanged;
                OutputValues.Add(outputValue);
                OnPropertyChange(nameof(OutputValues));
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
