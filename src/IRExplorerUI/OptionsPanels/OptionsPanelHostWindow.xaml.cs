// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Windows;
using System.Windows.Controls;
using IRExplorerUI.Controls;

namespace IRExplorerUI.OptionsPanels;

public partial class OptionsPanelHostWindow : DraggablePopup, IOptionsPanel {
  private bool closed_;
  private IOptionsPanel optionsPanel_;

  public OptionsPanelHostWindow(UserControl panel, Point position,
                                double width, double height,
                                UIElement referenceElement,
                                SettingsBase settings, ISession session,
                                bool showResetButton = true) {
    InitializeComponent();

    // Offset to account for drop shadow margin.
    position.Offset(6, 0);
    Initialize(position, width, height, referenceElement);

    PanelResizeGrip.ResizedControl = this;
    ShowResetButton = showResetButton;
    DataContext = this;

    optionsPanel_ = (IOptionsPanel)panel;
    optionsPanel_.Initialize(this, settings, session);
    optionsPanel_.PanelClosed += SettingsPanel_PanelClosed;
    optionsPanel_.PanelReset += SettingsPanel_PanelReset;
    optionsPanel_.SettingsChanged += SettingsPanel_SettingsChanged;
    optionsPanel_.StayOpenChanged += OptionsPanel_StayOpenChanged;
    PanelHost.Content = panel;
  }

  public static OptionsPanelHostWindow Create<T, S>(SettingsBase settings, FrameworkElement relativeControl, ISession session,
                                                    Func<S, bool, S> newSettingsHandler,
                                                    Action panelClosedHandler,
                                                    Point positionAdjustment = new Point())
    where T : OptionsPanelBase, new()
    where S: SettingsBase, new() {
    var panel = new T();
    double width = Math.Max(panel.MinimumWidth,
                            Math.Min(relativeControl.ActualWidth, panel.DefaultWidth));
    double height = Math.Max(panel.MinimumHeight,
                             Math.Min(relativeControl.ActualHeight, panel.DefaultHeight));
    var position = new Point(relativeControl.ActualWidth - width, 0);
    position .Offset(positionAdjustment.X, positionAdjustment.Y);
    var panelHost = new OptionsPanelHostWindow(panel, position, width, height, relativeControl,
                                               settings, session);
    panelHost.SettingsChanged += (sender, args) => {
      var result = newSettingsHandler((S)panelHost.Settings, false);

      if (result != null) {
        panel.Settings = result;
      }
    };
    panelHost.PanelReset += (sender, args) => {
      var newSettings =  new S();
      panelHost.Settings = newSettings;
      var result = newSettingsHandler(newSettings, true);

      if (result != null) {
        panel.Settings = result;
      }
    };
    panelHost.PanelClosed += (sender, args) => {
      panelHost.IsOpen = false;
      panelHost.PanelClosed = null;
      panelHost.PanelReset = null;
      panelHost.SettingsChanged = null;
      newSettingsHandler((S)panelHost.Settings, true);
      panelClosedHandler();
    };

    panelHost.IsOpen = true;
    return panelHost;
  }

  public event EventHandler PanelClosed;
  public event EventHandler PanelReset;
  public event EventHandler SettingsChanged;
  public event EventHandler<bool> StayOpenChanged;
  public bool ShowResetButton { get; set; }

  public SettingsBase Settings {
    get => optionsPanel_.Settings;
    set => optionsPanel_.Settings = value;
  }

  public ISession Session {
    get => optionsPanel_.Session;
    set => optionsPanel_.Session = value;
  }

  public void Initialize(FrameworkElement parent, SettingsBase settings, ISession session) {
    Settings = settings;
    Session = session;
  }

  public void PanelClosing() { }
  public void PanelResetting() { }
  public void PanelAfterReset() { }

  protected override void OnClosed(EventArgs e) {
    base.OnClosed(e);

    optionsPanel_.PanelClosed -= SettingsPanel_PanelClosed;
    optionsPanel_.PanelReset -= SettingsPanel_PanelReset;
    optionsPanel_.SettingsChanged -= SettingsPanel_SettingsChanged;

    if (!closed_) {
      closed_ = true;
      PanelClosed?.Invoke(this, e);
    }
  }

  private void SettingsPanel_SettingsChanged(object sender, EventArgs e) {
    SettingsChanged?.Invoke(this, e);
  }

  private void SettingsPanel_PanelReset(object sender, EventArgs e) {
    PanelReset?.Invoke(this, e);
  }

  private void SettingsPanel_PanelClosed(object sender, EventArgs e) {
    closed_ = true;
    PanelClosed?.Invoke(this, e);
  }

  private void OptionsPanel_StayOpenChanged(object sender, bool staysOpen) {
    StaysOpen = staysOpen;
  }

  private void ResetButton_Click(object sender, RoutedEventArgs e) {
    using var centerForm = new DialogCenteringHelper(this);

    if (MessageBox.Show("Do you want to reset all settings?", "IR Explorer",
                        MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) {
      return;
    }

    optionsPanel_.PanelResetting();
    PanelReset?.Invoke(this, e);
    optionsPanel_.PanelAfterReset();
  }

  private void CloseButton_Click(object sender, RoutedEventArgs e) {
    closed_ = true;
    optionsPanel_.PanelClosing();
    PanelClosed?.Invoke(this, e);
  }
}