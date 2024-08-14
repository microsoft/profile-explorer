// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.ComponentModel;

namespace ProfileExplorer.UI.Query;

public interface IElementQuery {
  public ISession Session { get; }
  public bool Initialize(ISession session);
  public bool Execute(QueryData data);
}

public class QueryDefinition : INotifyPropertyChanged {
  private Type queryType_;
  private QueryData data_;
  private IElementQuery queryInstance_;

  public QueryDefinition(Type queryType) {
    Data = new QueryData();
    queryType_ = queryType;
  }

  public QueryDefinition(Type queryType, string name, string description) : this(queryType) {
    Name = name;
    Description = description;
  }

  public event PropertyChangedEventHandler PropertyChanged;
  public string Name { get; set; }
  public string Description { get; set; }

  public QueryData Data {
    get => data_;
    set {
      if (value != data_) {
        if (data_ != null) {
          data_.ValueChanged -= InputValueChanged;
          data_.PropertyChanged -= DataPropertyChanged;
        }

        data_ = value;
        data_.ValueChanged += InputValueChanged;
        data_.PropertyChanged += DataPropertyChanged;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Data)));
      }
    }
  }

  public bool CreateQueryInstance(ISession session) {
    if (queryInstance_ == null) {
      queryInstance_ = (IElementQuery)Activator.CreateInstance(queryType_);
      data_.Instance = queryInstance_;
      return queryInstance_.Initialize(session);
    }

    return true;
  }

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