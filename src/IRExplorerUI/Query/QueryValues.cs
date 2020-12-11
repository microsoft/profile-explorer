using System;
using System.Windows;
using System.Windows.Media;

namespace IRExplorerUI.Query {
    [Flags]
    public enum QueryValueKind {
        Other = 0,
        Bool = 1 << 0,
        Number = 1 << 1,
        String = 1 << 2,
        Element = 1 << 3,
        List = 1 << 7,
        Color = 1 << 8,
        Input = 1 << 16
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

        public void ForceValueUpdate(object newValue) {
            // If value is the same the notify event is not triggered, but sometimes
            // that is needed to have the query redo the temporary marking, for ex.
            if (!SetAndNotify(ref value_, newValue, "Value")) {
                NotifyPropertyChanged(nameof(Value));
            }
        }

        public object Tag { get; set; }

        public bool IsBool => Kind.HasFlag(QueryValueKind.Bool);
        public bool IsNumber => Kind.HasFlag(QueryValueKind.Number);
        public bool IsString => Kind.HasFlag(QueryValueKind.Number);
        public bool IsElement => Kind.HasFlag(QueryValueKind.Element);
        public bool IsList => Kind.HasFlag(QueryValueKind.List);
        public bool IsColor => Kind.HasFlag(QueryValueKind.Color);
        public bool IsInput => Kind.HasFlag(QueryValueKind.Input);
        public bool IsOutput => !Kind.HasFlag(QueryValueKind.Input);

        public static object GetDefaultValue(QueryValueKind kind) {
            return kind switch
            {
                QueryValueKind.Bool => false,
                QueryValueKind.Element => null,
                QueryValueKind.Number => 0,
                QueryValueKind.String => "",
                QueryValueKind.Color => Colors.White,
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
}
