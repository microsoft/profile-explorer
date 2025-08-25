// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using ProfileExplorerCore2.Session;
using ProfileExplorerCore2.Settings;

namespace ProfileExplorer.UI.OptionsPanels;

public interface IOptionsPanel {
  SettingsBase Settings { get; set; }
  IUISession Session { get; set; }
  event EventHandler PanelClosed;
  event EventHandler PanelReset;
  event EventHandler SettingsChanged;
  event EventHandler<bool> StayOpenChanged;
  void Initialize(FrameworkElement parent, SettingsBase settings, IUISession session);
  void PanelClosing();
  void PanelResetting();
  void PanelAfterReset();
}

public class OptionsPanelBase : UserControl, IOptionsPanel, INotifyPropertyChanged {
  private bool initialized_;
  public virtual double DefaultHeight => 320;
  public virtual double MinimumHeight => 200;
  public virtual double DefaultWidth => 380;
  public virtual double MinimumWidth => 380;
  public FrameworkElement Parent { get; set; }
  public event PropertyChangedEventHandler PropertyChanged;
  public event EventHandler PanelClosed;
  public event EventHandler PanelReset;
  public event EventHandler SettingsChanged;
  public event EventHandler<bool> StayOpenChanged;
  public IUISession Session { get; set; }

  public SettingsBase Settings {
    get => (SettingsBase)DataContext;
    set {
      if (DataContext != value) {
        DataContext = null;
        DataContext = value;

        if (value != null && initialized_) {
          OnSettingsChanged(value);
        }
      }
    }
  }

  public virtual void Initialize(FrameworkElement parent, SettingsBase settings, IUISession session) {
    Parent = parent;
    Settings = settings;
    Session = session;
    PreviewMouseUp += (sender, args) => {
      if (Utils.IsOptionsUpdateEvent(args)) {
        NotifySettingsChanged();
      }
    };

    PreviewKeyUp += (sender, args) => {
      if (Utils.IsOptionsUpdateEvent(args)) {
        NotifySettingsChanged();
      }
    };

    initialized_ = true;
  }

  public virtual void PanelClosing() { }
  public virtual void PanelResetting() { }
  public virtual void PanelAfterReset() { }

  public virtual void OnSettingsChanged(object newSettings) {
  }

  public virtual void ReloadSettings() {
    var temp = Settings;
    Settings = null;
    Settings = temp;
  }

  protected virtual void NotifySettingsChanged() {
    DelayedAction.StartNew(TimeSpan.FromMilliseconds(100), () => {
      RaiseSettingsChanged(null);
    });
  }

  public void RaisePanelClosed(EventArgs e) {
    PanelClosed?.Invoke(this, e);
  }

  public void RaisePanelReset(EventArgs e) {
    PanelReset?.Invoke(this, e);
  }

  public void RaiseSettingsChanged(EventArgs e) {
    SettingsChanged?.Invoke(this, e);
  }

  public void RaiseStayOpenChanged(bool staysOpen) {
    StayOpenChanged?.Invoke(this, staysOpen);
  }

  protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null) {
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
  }

  protected bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null) {
    if (EqualityComparer<T>.Default.Equals(field, value))
      return false;
    field = value;
    OnPropertyChanged(propertyName);
    return true;
  }
}