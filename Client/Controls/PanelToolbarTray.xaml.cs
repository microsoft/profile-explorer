// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace Client {
    public class PinEventArgs : EventArgs {
        public bool IsPinned { get; set; }
    }

    public class DuplicateEventArgs : EventArgs {
        public DuplicatePanelKind Kind { get; set; }
    }

    public class BindMenuItem {
        public string Header { get; set; }
        public string ToolTip { get; set; }
        public object Tag { get; set; }
        public bool IsChecked { get; set; }
    }

    public class BindMenuItemsArgs : EventArgs {
        public List<BindMenuItem> MenuItems;

        public BindMenuItemsArgs() {
            MenuItems = new List<BindMenuItem>();
        }
    }

    public partial class PanelToolbarTray : ToolBarTray {
        public event EventHandler<PinEventArgs> PinnedChanged;
        public event EventHandler<DuplicateEventArgs> DuplicateClicked;
        public event EventHandler SettingsClicked;
        public event EventHandler<BindMenuItemsArgs> BindMenuOpen;
        public event EventHandler<BindMenuItem> BindMenuItemSelected;

        public bool HasPinButton {
            get { return (bool)GetValue(HasPinButtonProperty); }
            set { SetValue(HasPinButtonProperty, value); }
        }

        public static readonly DependencyProperty HasPinButtonProperty =
            DependencyProperty.Register(
                "HasPinButton",
                typeof(bool),
                typeof(PanelToolbarTray),
                new PropertyMetadata(true, OnHasPinButtonPropertyChanged));

        private static void OnHasPinButtonPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            PanelToolbarTray source = d as PanelToolbarTray;
            bool visible = (bool)e.NewValue;
            source.PinButton.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        public bool HasDuplicateButton {
            get { return (bool)GetValue(HasDuplicateButtonProperty); }
            set { SetValue(HasDuplicateButtonProperty, value); }
        }

        public static readonly DependencyProperty HasDuplicateButtonProperty =
            DependencyProperty.Register(
                "HasDuplicateButton",
                typeof(bool),
                typeof(PanelToolbarTray),
                new PropertyMetadata(true, OnHasDuplicateButtonPropertyChanged));

        private static void OnHasDuplicateButtonPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            PanelToolbarTray source = d as PanelToolbarTray;
            bool visible = (bool)e.NewValue;
            source.DuplicateButton.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        private static void OnHasPinButtonChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e) {
            if (sender is PanelToolbarTray control) {
                bool visible = (bool)e.NewValue;
            }
        }

        private static void OnHasDuplicateButtonChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e) {
            if (sender is PanelToolbarTray control) {
                bool visible = (bool)e.NewValue;
                control.DuplicateButton.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        public PanelToolbarTray() {
            InitializeComponent();
        }

        public bool IsPinned {
            get {
                return PinButton.IsChecked.HasValue &&
                       PinButton.IsChecked.Value;
            }
            set {
                PinButton.IsChecked = value;
            }
        }

        private void ToolBar_Loaded(object sender, RoutedEventArgs e) {
            Utils.RemoveToolbarOverflowButton(sender as ToolBar);
        }

        private void PinButton_Checked(object sender, RoutedEventArgs e) {
            PinnedChanged?.Invoke(this, new PinEventArgs() { IsPinned = true });
        }

        private void PinButton_Unchecked(object sender, RoutedEventArgs e) {
            PinnedChanged?.Invoke(this, new PinEventArgs() { IsPinned = false });
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e) {
            SettingsClicked?.Invoke(this, new EventArgs());
        }

        private void DuplicateMenu_Click(object sender, RoutedEventArgs e) {
            DuplicateClicked?.Invoke(this, new DuplicateEventArgs() { Kind = DuplicatePanelKind.SameSet });
        }

        private void DuplicateLeftMenu_Click(object sender, RoutedEventArgs e) {
            DuplicateClicked?.Invoke(this, new DuplicateEventArgs() { Kind = DuplicatePanelKind.NewSetDockedLeft });
        }

        private void DuplicateRightMenu_Click(object sender, RoutedEventArgs e) {
            DuplicateClicked?.Invoke(this, new DuplicateEventArgs() { Kind = DuplicatePanelKind.NewSetDockedRight });
        }

        private void DuplicateFloatingMenu_Click(object sender, RoutedEventArgs e) {
            DuplicateClicked?.Invoke(this, new DuplicateEventArgs() { Kind = DuplicatePanelKind.Floating });
        }

        private void MenuItem_SubmenuOpened(object sender, RoutedEventArgs e) {
            if (BindMenuOpen != null) {
                var args = new BindMenuItemsArgs();
                BindMenuOpen(this, args);

                foreach (var item in BindMenu.Items) {
                    ((MenuItem)item).Click -= BindMenuItem_Click;
                }

                BindMenu.Items.Clear();

                foreach (var item in args.MenuItems) {
                    var menuItem = new MenuItem() {
                        Header = item.Header,
                        ToolTip = item.ToolTip,
                        Tag = item,
                        IsCheckable = true,
                        IsChecked = item.IsChecked
                    };

                    menuItem.Click += BindMenuItem_Click;
                    BindMenu.Items.Add(menuItem);
                }
            }
        }

        private void BindMenuItem_Click(object sender, RoutedEventArgs e) {
            if (BindMenuItemSelected != null) {
                var menuItem = sender as MenuItem;
                BindMenuItemSelected(this, menuItem.Tag as BindMenuItem);
            }
        }
    }
}
