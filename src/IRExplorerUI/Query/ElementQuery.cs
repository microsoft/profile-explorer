using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using IRExplorerCore.IR;

namespace IRExplorerUI.Query {
    public interface IElementQuery {
        public ISessionManager Session { get; }
        public bool Initialize(ISessionManager session);
        public bool Execute(QueryData data);
    }

    public class QueryDefinition : INotifyPropertyChanged {
        private Type queryType_;
        private IElementQuery queryInstance_;

        public QueryDefinition(Type queryType) {
            Data = new QueryData();
            Data.ValueChanged += InputValueChanged;
            Data.PropertyChanged += DataPropertyChanged;
            queryType_ = queryType;
        }

        public QueryDefinition(Type queryType, string name, string description) : this(queryType) {
            Name = name;
            Description = description;
        }

        public string Name { get; set; }
        public string Description { get; set; }
        public QueryData Data { get; set; }

        public bool CreateQueryInstance(ISessionManager session) {
            if (queryInstance_ == null) {
                queryInstance_ = (IElementQuery)Activator.CreateInstance(queryType_);
                return queryInstance_.Initialize(session);
            }

            return true;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void InputValueChanged(object sender, EventArgs e) {
            if (AreAllInputValuesSet()) {
                queryInstance_.Execute(Data);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Data)));
            }
        }

        private bool AreAllInputValuesSet() {
            return Data.InputValues.FindIndex(item => item.Value == null) == -1;
        }

        private void DataPropertyChanged(object sender, PropertyChangedEventArgs e) {
            PropertyChanged?.Invoke(this, e);
        }
    }
}
