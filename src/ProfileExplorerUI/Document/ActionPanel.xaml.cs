﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace ProfileExplorer.UI.Document;

public class ActionPanelButton {
  public ActionPanelButton(string name, object tag = null) {
    Name = name;
    Tag = tag;
  }

  public string Name { get; set; }
  public object Tag { get; set; }
}

public partial class ActionPanel : UserControl, INotifyPropertyChanged {
  private ObservableCollectionRefresh<ActionPanelButton> buttons_;
  private bool showRemarksButton_;

  public ActionPanel() {
    InitializeComponent();
    DataContext = this;

    buttons_ = new ObservableCollectionRefresh<ActionPanelButton>();
    ActionButtonsPanel.ItemsSource = buttons_;
  }

  public bool ShowRemarksButton {
    get => showRemarksButton_;
    set {
      if (showRemarksButton_ != value) {
        showRemarksButton_ = value;
        OnPropertyChange(nameof(ShowRemarksButton));
      }
    }
  }

  public bool HasActionButtons => buttons_.Count > 0;
  public event PropertyChangedEventHandler PropertyChanged;
  public event EventHandler RemarksButtonClicked;
  public event EventHandler<ActionPanelButton> ActionButtonClicked;

  public ActionPanelButton AddActionButton(string name, object tag = null) {
    var button = new ActionPanelButton(name, tag);
    buttons_.Add(button);
    OnPropertyChange(nameof(HasActionButtons));
    return button;
  }

  public void ClearActionButtons() {
    buttons_.Clear();
    OnPropertyChange(nameof(HasActionButtons));
  }

  public void OnPropertyChange(string propertyname) {
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyname));
  }

  private void ActionButton_Click(object sender, RoutedEventArgs e) {
    var button = (ActionPanelButton)((Button)sender).DataContext;
    ActionButtonClicked?.Invoke(this, button);
  }

  private void RemarkButton_Click(object sender, RoutedEventArgs e) {
    RemarksButtonClicked?.Invoke(this, e);
  }
}