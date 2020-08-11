// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace IRExplorerUI {
    public sealed class ColorEventArgs : EventArgs {
        public Color SelectedColor { get; set; }
    }

    public partial class ColorSelector : UserControl {
        public static DependencyProperty ColorSelectedCommandProperty =
            DependencyProperty.Register("ColorSelectedCommand", typeof(ICommand), typeof(ColorSelector));

        private static readonly Color[] ButtonColors;

        public static readonly DependencyProperty CommandTargetProperty =
            DependencyProperty.Register("CommandTarget", typeof(IInputElement), typeof(ColorSelector),
                                        new UIPropertyMetadata(null));

        static ColorSelector() {
            ButtonColors = new[] {
                (Color) ColorConverter.ConvertFromString("#f0da69"),
                (Color) ColorConverter.ConvertFromString("#f7938f"),
                (Color) ColorConverter.ConvertFromString("#F3F986"),
                (Color) ColorConverter.ConvertFromString("#A7F986"),
                (Color) ColorConverter.ConvertFromString("#86B0F9"),
                (Color) ColorConverter.ConvertFromString("#85decc"),
                (Color) ColorConverter.ConvertFromString("#B696FC"),
                (Color) ColorConverter.ConvertFromString("#86D4F9"),
                (Color) ColorConverter.ConvertFromString("#f5ac6c")
            };
        }

        public ColorSelector() {
            InitializeComponent();
            Focusable = true;
            PreviewKeyDown += ColorSelector_PreviewKeyDown;
            Loaded += ColorSelector_Loaded;
        }

        public ICommand ColorSelectedCommand {
            get => (ICommand)GetValue(ColorSelectedCommandProperty);

            set => SetValue(ColorSelectedCommandProperty, value);
        }

        public IInputElement CommandTarget {
            get => (IInputElement)GetValue(CommandTargetProperty);
            set => SetValue(CommandTargetProperty, value);
        }

        public event EventHandler<ColorEventArgs> ColorSelected;

        private void ColorSelector_Loaded(object sender, RoutedEventArgs e) {
            Focus();
        }

        private void ColorSelector_PreviewKeyDown(object sender, KeyEventArgs e) {
            int index = e.Key switch
            {
                Key.D0 => 0,
                Key.D1 => 1,
                Key.D2 => 2,
                Key.D3 => 3,
                Key.D4 => 4,
                Key.D5 => 5,
                Key.D6 => 6,
                Key.D7 => 7,
                Key.D8 => 8,
                Key.D9 => 9,
                _ => -1
            };

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
            if (ColorSelectedCommand == null && ColorSelected == null) {
                return;
            }

            var parentHost = FindParentHost();

            if (parentHost != null) {
                parentHost.Focus();
            }

            var args = new ColorEventArgs {
                SelectedColor = color
            };

            if (ColorSelectedCommand != null) {
                if (ColorSelectedCommand.CanExecute(args)) {
                    ColorSelectedCommand.Execute(args);
                }
            }
            else {
                ColorSelected?.Invoke(this, args);
            }
        }

        private UIElement FindParentHost() {
            var logicalRoot = LogicalTreeHelper.GetParent(this);

            while (logicalRoot != null) {
                if (logicalRoot is UserControl || logicalRoot is Window) {
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
                else if (logicalRoot is Popup popup) {
                    popup.IsOpen = false;
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

        private void MoreColorPicker_SelectedColorChanged(object sender,
                                                          RoutedPropertyChangedEventArgs<Color?> e) {
            if (e.NewValue.HasValue) {
                MoreColorPicker.Visibility = Visibility.Collapsed;
                CommitColor(e.NewValue.Value);
            }
        }
    }
}