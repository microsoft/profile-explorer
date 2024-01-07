// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Windows;
using System.Windows.Controls;

namespace IRExplorerUI.OptionsPanels;

public interface IOptionsPanel {
  event EventHandler PanelClosed;
  event EventHandler PanelReset;
  event EventHandler SettingsChanged;
  event EventHandler<bool> StayOpenChanged;
  SettingsBase Settings { get; set; }
  ISession Session { get; set; }
  void Initialize(FrameworkElement parent, SettingsBase settings, ISession session);
  void PanelClosing();
  void PanelResetting();
  void PanelAfterReset();
}

public class OptionsPanelBase : UserControl, IOptionsPanel {
  private bool initialized_;

  public virtual double DefaultHeight => 320;
  public virtual double MinimumHeight => 200;
  public virtual double DefaultWidth => 350;
  public virtual double MinimumWidth => 350;

  public event EventHandler PanelClosed;
  public event EventHandler PanelReset;
  public event EventHandler SettingsChanged;
  public event EventHandler<bool> StayOpenChanged;
  public ISession Session { get; set; }
  public FrameworkElement Parent { get; set; }

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

  public virtual void OnSettingsChanged(object newSettings) {
  }

  protected void ReloadSettings() {
    var temp = Settings;
    Settings = null;
    Settings = temp;
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

  public virtual void Initialize(FrameworkElement parent, SettingsBase settings, ISession session) {
    Parent = parent;
    Settings = settings;
    Session = session;
    initialized_ = true;
  }

  public virtual void PanelClosing() { }
  public virtual void PanelResetting() { }
  public virtual void PanelAfterReset() { }
}