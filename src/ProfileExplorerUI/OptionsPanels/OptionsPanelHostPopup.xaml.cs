// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ProfileExplorer.UI.Controls;
using ProfileExplorer.Core.Session;
using ProfileExplorer.Core.Settings;

namespace ProfileExplorer.UI.OptionsPanels;

/// <summary>
/// Wrapper that adapts an MVVM-compatible panel to the IOptionsPanel interface.
/// This allows MVVM panels to work with the existing OptionsPanelHostPopup infrastructure.
/// </summary>
internal class MvvmOptionsPanelWrapper : UserControl, IOptionsPanel {
  private readonly IMvvmOptionsPanel mvvmPanel_;
  private SettingsBase settings_;
  private IUISession session_;

  public MvvmOptionsPanelWrapper(IMvvmOptionsPanel mvvmPanel, SettingsBase settings, IUISession session) {
    mvvmPanel_ = mvvmPanel;
    settings_ = settings;
    session_ = session;
    
    // Set the MVVM panel as the content
    Content = mvvmPanel as UserControl;
    
    // Initialize the MVVM panel
    mvvmPanel_.Initialize(this, settings, session);
  }

  public SettingsBase Settings {
    get => settings_;
    set {
      if (settings_ != value) {
        settings_ = value;
        // Re-initialize the MVVM panel when settings change
        mvvmPanel_.Initialize(this, value, session_);
      }
    }
  }

  public IUISession Session {
    get => session_;
    set => session_ = value;
  }

  public event EventHandler PanelClosed;
  public event EventHandler PanelReset;
  public event EventHandler SettingsChanged;
  public event EventHandler<bool> StayOpenChanged;

  public void Initialize(FrameworkElement parent, SettingsBase settings, IUISession session) {
    Settings = settings;
    Session = session;
    mvvmPanel_.Initialize(parent, settings, session);
  }

  public void PanelClosing() {
    mvvmPanel_.PanelClosing();
  }

  public void PanelResetting() {
    mvvmPanel_.ResetSettings();
  }

  public void PanelAfterReset() {
    // MVVM panels handle this through ResetSettings()
  }

  /// <summary>
  /// Triggers the SettingsChanged event
  /// </summary>
  public void TriggerSettingsChanged() {
    SettingsChanged?.Invoke(this, EventArgs.Empty);
  }

  /// <summary>
  /// Triggers the PanelReset event
  /// </summary>
  public void TriggerPanelReset() {
    PanelReset?.Invoke(this, EventArgs.Empty);
  }
}

public partial class OptionsPanelHostPopup : DraggablePopup, IOptionsPanel {
  private bool closed_;
  private IOptionsPanel optionsPanel_;

  public OptionsPanelHostPopup(UserControl panel, Point position,
                               double width, double height,
                               UIElement referenceElement,
                               SettingsBase settings, IUISession session,
                               bool showResetButton = true) {
    InitializeComponent();

    // Offset to account for drop shadow margin.
    position.Offset(6, 0);
    Initialize(position, width, height, referenceElement);
    ZoomTransform.ScaleX = WindowScaling;
    ZoomTransform.ScaleY = WindowScaling;

    StaysOpen = true; // Keep popup open when clicking outside.
    PanelResizeGrip.ResizedControl = this;
    ShowResetButton = showResetButton;

    optionsPanel_ = (IOptionsPanel)panel;
    optionsPanel_.Initialize(this, settings, session);
    optionsPanel_.PanelClosed += SettingsPanel_PanelClosed;
    optionsPanel_.PanelReset += SettingsPanel_PanelReset;
    optionsPanel_.SettingsChanged += SettingsPanel_SettingsChanged;
    optionsPanel_.StayOpenChanged += OptionsPanel_StayOpenChanged;
    PanelHost.Content = panel;
  }

  public double WindowScaling => App.Settings.GeneralSettings.WindowScaling;
  public bool ShowResetButton { get; set; }
  public event EventHandler PanelClosed;
  public event EventHandler PanelReset;
  public event EventHandler SettingsChanged;
  public event EventHandler<bool> StayOpenChanged;

  public SettingsBase Settings {
    get => optionsPanel_.Settings;
    set => optionsPanel_.Settings = value;
  }

  public IUISession Session {
    get => optionsPanel_.Session;
    set => optionsPanel_.Session = value;
  }

  public void Initialize(FrameworkElement parent, SettingsBase settings, IUISession session) {
    Settings = settings;
    Session = session;
  }

  public void PanelClosing() { }
  public void PanelResetting() { }
  public void PanelAfterReset() { }

  public static OptionsPanelHostPopup Create<T, S>(SettingsBase settings, FrameworkElement relativeControl,
                                                   IUISession session,
                                                   Func<S, bool, Task<S>> newSettingsHandler,
                                                   Action panelClosedHandler,
                                                   Point positionAdjustment = new(),
                                                   bool dockLeft = false)
    where T : OptionsPanelBase, new()
    where S : SettingsBase, new() {
    var panel = new T();
    double width = Math.Max(panel.MinimumWidth,
                            Math.Min(relativeControl.ActualWidth, panel.DefaultWidth));
    double height = Math.Max(panel.MinimumHeight,
                             Math.Min(relativeControl.ActualHeight, panel.DefaultHeight));
    var position = dockLeft ? new Point(0, 0) :
      new Point(relativeControl.ActualWidth - width, 0);
    position.Offset(positionAdjustment.X, positionAdjustment.Y);
    var panelHost = new OptionsPanelHostPopup(panel, position, width, height, relativeControl,
                                              settings, session);
    panelHost.SettingsChanged += async (sender, args) => {
      var result = await newSettingsHandler((S)panelHost.Settings, false);

      if (result != null) {
        panel.Settings = result;
      }
    };
    panelHost.PanelReset += async (sender, args) => {
      var newSettings = new S();
      panelHost.Settings = newSettings;
      var result = await newSettingsHandler(newSettings, true);

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

  /// <summary>
  /// Creates an OptionsPanelHostPopup for MVVM-compatible panels that use ViewModels.
  /// This allows for gradual migration to MVVM architecture.
  /// </summary>
  /// <typeparam name="T">The panel type that implements IMvvmOptionsPanel</typeparam>
  /// <typeparam name="S">The settings type</typeparam>
  /// <param name="settings">Initial settings to load</param>
  /// <param name="relativeControl">Control to position the popup relative to</param>
  /// <param name="session">UI session</param>
  /// <param name="newSettingsHandler">Handler for settings changes and commits</param>
  /// <param name="panelClosedHandler">Handler for when panel is closed</param>
  /// <param name="positionAdjustment">Position adjustment offset</param>
  /// <param name="dockLeft">Whether to dock to the left side</param>
  /// <returns>The created popup host</returns>
  public static OptionsPanelHostPopup CreateMvvm<T, S>(SettingsBase settings, FrameworkElement relativeControl,
                                                       IUISession session,
                                                       Func<S, bool, Task<S>> newSettingsHandler,
                                                       Action panelClosedHandler,
                                                       Point positionAdjustment = new(),
                                                       bool dockLeft = false)
    where T : UserControl, IMvvmOptionsPanel, new()
    where S : SettingsBase, new() {
    var panel = new T();
    double width = Math.Max(panel.MinimumWidth,
                            Math.Min(relativeControl.ActualWidth, panel.DefaultWidth));
    double height = Math.Max(panel.MinimumHeight,
                             Math.Min(relativeControl.ActualHeight, panel.DefaultHeight));
    var position = dockLeft ? new Point(0, 0) :
      new Point(relativeControl.ActualWidth - width, 0);
    position.Offset(positionAdjustment.X, positionAdjustment.Y);

    // Create a wrapper that adapts the MVVM panel to the IOptionsPanel interface
    var wrapper = new MvvmOptionsPanelWrapper(panel, settings, session);
    var panelHost = new OptionsPanelHostPopup(wrapper, position, width, height, relativeControl,
                                              settings, session);

    panelHost.SettingsChanged += async (sender, args) => {
      var currentSettings = panel.GetCurrentSettings();
      var result = await newSettingsHandler((S)currentSettings, false);

      if (result != null) {
        // Re-initialize the panel with the new settings
        panel.Initialize(relativeControl, result, session);
      }
    };

    panelHost.PanelReset += async (sender, args) => {
      var newSettings = new S();
      panel.ResetSettings();
      var result = await newSettingsHandler(newSettings, true);

      if (result != null) {
        panel.Initialize(relativeControl, result, session);
      }
    };

    panelHost.PanelClosed += (sender, args) => {
      panelHost.IsOpen = false;
      panelHost.PanelClosed = null;
      panelHost.PanelReset = null;
      panelHost.SettingsChanged = null;
      
      panel.SaveSettings();
      var finalSettings = panel.GetCurrentSettings();
      newSettingsHandler((S)finalSettings, true);
      panelClosedHandler();
    };

    panelHost.IsOpen = true;
    return panelHost;
  }

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

    if (MessageBox.Show("Do you want to reset all settings?", "Profile Explorer",
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