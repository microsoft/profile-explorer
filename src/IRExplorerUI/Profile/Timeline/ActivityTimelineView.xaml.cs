// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using IRExplorerUI.Utilities;

namespace IRExplorerUI.Profile;

public enum ThreadActivityAction {
  IncludeThread,
  IncludeSameNameThread,
  ExcludeThread,
  ExcludeSameNameThread,
  FilterToThread,
  FilterToSameNameThread,
  SelectThread
}

public partial class ActivityTimelineView : UserControl, INotifyPropertyChanged {
  private Brush disabledMarginBackColor_;
  private Brush marginBackColor_;
  private bool pendingClick_;

  public ActivityTimelineView() {
    InitializeComponent();
    disabledMarginBackColor_ = Brushes.GhostWhite;
    marginBackColor_ = Brushes.Linen;
    DataContext = this;
  }

  public event EventHandler<ThreadActivityAction> ThreadActivityAction;
  public event PropertyChangedEventHandler PropertyChanged;
  public RelayCommand<object> IncludeThreadCommand =>
    new RelayCommand<object>(obj => ThreadActivityAction?.Invoke(this, Profile.ThreadActivityAction.IncludeThread));
  public RelayCommand<object> IncludeSameNameThreadCommand =>
    new RelayCommand<object>(
      obj => ThreadActivityAction?.Invoke(this, Profile.ThreadActivityAction.IncludeSameNameThread));
  public RelayCommand<object> ExcludeThreadCommand =>
    new RelayCommand<object>(obj => ThreadActivityAction?.Invoke(this, Profile.ThreadActivityAction.ExcludeThread));
  public RelayCommand<object> ExcludeSameNameThreadCommand =>
    new RelayCommand<object>(
      obj => ThreadActivityAction?.Invoke(this, Profile.ThreadActivityAction.ExcludeSameNameThread));
  public RelayCommand<object> FilterToThreadCommand =>
    new RelayCommand<object>(obj => ThreadActivityAction?.Invoke(this, Profile.ThreadActivityAction.FilterToThread));
  public RelayCommand<object> FilterToSameNameThreadCommand =>
    new RelayCommand<object>(
      obj => ThreadActivityAction?.Invoke(this, Profile.ThreadActivityAction.FilterToSameNameThread));

  public Brush DisabledMarginBackColor {
    get => disabledMarginBackColor_;
    set => SetField(ref disabledMarginBackColor_, value);
  }

  public Brush MarginBackColor {
    get => IsThreadIncluded ? marginBackColor_ : disabledMarginBackColor_;
    set => SetField(ref marginBackColor_, value);
  }

  public bool IsThreadIncluded {
    get => ActivityHost.IsThreadIncluded;
    set {
      if (ActivityHost.IsThreadIncluded != value) {
        ActivityHost.IsThreadIncluded = value;
        OnPropertyChanged();
        OnPropertyChanged(nameof(MarginBackColor));
      }
    }
  }

  public int ThreadId => ActivityHost.ThreadId;
  public string ThreadName => ActivityHost.ThreadName;

  protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null) {
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
  }

  protected bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null) {
    if (EqualityComparer<T>.Default.Equals(field, value)) return false;
    field = value;
    OnPropertyChanged(propertyName);
    return true;
  }

  private void Margin_MouseDown(object sender, MouseButtonEventArgs e) {
    if (e.LeftButton == MouseButtonState.Pressed &&
        e.ClickCount >= 2) {
      pendingClick_ = false; // Cancel click action.
      ThreadActivityAction?.Invoke(this, Profile.ThreadActivityAction.FilterToThread);
    }
    else if (e.LeftButton == MouseButtonState.Pressed) {
      // There is no way in WPF to ignore a click event if double-click happens,
      // add some delay to the action to see that a double-click happens.
      pendingClick_ = true;

      Task.Run(async () => {
        // Wait system double-click time interval.
        await Task.Delay(TimeSpan.FromMilliseconds(NativeMethods.GetDoubleClickTime()));

        if (pendingClick_) { // No double-click happened.
          pendingClick_ = false;
          Dispatcher.Invoke(() =>
                              ThreadActivityAction?.Invoke(this, Profile.ThreadActivityAction.SelectThread));
        }
      });
    }
  }

  private void ThreadContextMenuButton_Click(object sender, RoutedEventArgs e) {
    Utils.ShowContextMenu(sender as FrameworkElement, this);
  }
}
