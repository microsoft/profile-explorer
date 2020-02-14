// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Xceed.Wpf.Toolkit;

namespace Client {
    public sealed class ColorEventArgs : EventArgs {
        public Color SelectedColor { get; set; }
    }

    public partial class ColorSelector : UserControl {
        public static DependencyProperty ColorSelectedCommandProperty
               = DependencyProperty.Register(
                   "ColorSelectedCommand",
                   typeof(ICommand),
                   typeof(ColorSelector));

        private static readonly Color[] ButtonColors;

        static ColorSelector() {
            ButtonColors = new Color[] {
                (Color)ColorConverter.ConvertFromString("#f0da69"),
                (Color)ColorConverter.ConvertFromString("#f7938f"),
                (Color)ColorConverter.ConvertFromString("#F3F986"),
                (Color)ColorConverter.ConvertFromString("#A7F986"),
                (Color)ColorConverter.ConvertFromString("#86B0F9"),
                (Color)ColorConverter.ConvertFromString("#85decc"),
                (Color)ColorConverter.ConvertFromString("#B696FC"),
                (Color)ColorConverter.ConvertFromString("#86D4F9"),
                (Color)ColorConverter.ConvertFromString("#f5ac6c"),
            };
        }

        public ICommand ColorSelectedCommand {
            get {
                return (ICommand)GetValue(ColorSelectedCommandProperty);
            }

            set {
                SetValue(ColorSelectedCommandProperty, value);
            }
        }

        public IInputElement CommandTarget {
            get { return (IInputElement)GetValue(CommandTargetProperty); }
            set { SetValue(CommandTargetProperty, value); }
        }

        public static readonly DependencyProperty CommandTargetProperty =
            DependencyProperty.Register("CommandTarget", typeof(IInputElement),
                                        typeof(ColorSelector), new UIPropertyMetadata(null));

        public ColorSelector() {
            InitializeComponent();
            this.Focusable = true;
            this.PreviewKeyDown += ColorSelector_PreviewKeyDown;
            this.Loaded += ColorSelector_Loaded;
        }

        private void ColorSelector_Loaded(object sender, RoutedEventArgs e) {
            Focus();
        }

        private void ColorSelector_PreviewKeyDown(object sender, KeyEventArgs e) {
            int index = -1;

            switch (e.Key) {
                case Key.D0: { index = 0; break; }
                case Key.D1: { index = 1; break; }
                case Key.D2: { index = 2; break; }
                case Key.D3: { index = 3; break; }
                case Key.D4: { index = 4; break; }
                case Key.D5: { index = 5; break; }
                case Key.D6: { index = 6; break; }
                case Key.D7: { index = 7; break; }
                case Key.D8: { index = 8; break; }
                case Key.D9: { index = 9; break; }
            }

            if (index != -1) {
                CommitColorAtIndex(index);
                e.Handled = true;
            }
        }

        private void CommitColorAtIndex(int index) {
            CommitColor(ButtonColors[index]);
        }

        private void CommitColor(Color color) {
            RaiseSelectedColorEvent(color);
            CloseParentMenu();
        }

        private void RaiseSelectedColorEvent(Color color) {
            if (ColorSelectedCommand == null) {
                return;
            }

            var parentHost = FindParentHost();

            if (parentHost != null) {
                parentHost.Focus();
            }

            if (ColorSelectedCommand.CanExecute(null)) {
                ColorSelectedCommand.Execute(new ColorEventArgs() {
                    SelectedColor = color
                });
            }
        }

        private UIElement FindParentHost() {
            var logicalRoot = LogicalTreeHelper.GetParent(this);

            while (logicalRoot != null) {
                if (logicalRoot is UserControl ||
                    logicalRoot is Window) {
                    break;
                }

                logicalRoot = LogicalTreeHelper.GetParent(logicalRoot);
            }

            return logicalRoot as UIElement;
        }

        private void CloseParentMenu() {
            // Close the context menu hosting the control.
            var logicalRoot = LogicalTreeHelper.GetParent(this);

            while (logicalRoot != null) {
                if (logicalRoot is ContextMenu menu) {
                    menu.IsOpen = false;
                    break;
                }

                logicalRoot = LogicalTreeHelper.GetParent(logicalRoot);
            }
        }

        private void Button_MouseUp(object sender, MouseButtonEventArgs e) {
            var button = sender as Button;
            var brush = button.Background as SolidColorBrush;
            RaiseSelectedColorEvent(brush.Color);
            CloseParentMenu();
            e.Handled = true;
        }

        private void AnyButton_Click(object sender, RoutedEventArgs e) {
            int index = new Random().Next(0, ButtonColors.Length - 1);
            CommitColorAtIndex(index);
        }

        private void MoreButton_Click(object sender, RoutedEventArgs e) {
            MoreColorPicker.Visibility = Visibility.Visible;
            MoreColorPicker.SelectedColorChanged += MoreColorPicker_SelectedColorChanged;
            MoreColorPicker.IsOpen = true;
        }

        private void MoreColorPicker_SelectedColorChanged(object sender, RoutedPropertyChangedEventArgs<Color?> e) {
            if (e.NewValue.HasValue) {
                MoreColorPicker.Visibility = Visibility.Collapsed;
                CommitColor(e.NewValue.Value);
            }
        }
    }
}
