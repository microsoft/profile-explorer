// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ProfileExplorer.UI;

public sealed class SelectedColorEventArgs : EventArgs {
  public Color SelectedColor { get; set; }
}

public partial class ColorSelector : UserControl {
  public static readonly DependencyProperty CommandTargetProperty =
    DependencyProperty.Register("CommandTarget", typeof(IInputElement), typeof(ColorSelector),
                                new UIPropertyMetadata(null));
  public static DependencyProperty ColorSelectedCommandProperty =
    DependencyProperty.Register("ColorSelectedCommand", typeof(ICommand), typeof(ColorSelector));
  private static readonly Color[] ButtonColors;
  public Brush[] ButtonBrushes { get; set; }

  static ColorSelector() {
    ButtonColors = new[] {
      Utils.ColorFromString("#F2C3C1"),
      Utils.ColorFromString("#F3F4A6"),
      Utils.ColorFromString("#D2F4C3"),
      Utils.ColorFromString("#C1D4F2"),
      Utils.ColorFromString("#B1DDD4"),
      Utils.ColorFromString("#F2C1DA"),
      Utils.ColorFromString("#DED1FF"),
      Utils.ColorFromString("#C3E6F4"),
      Utils.ColorFromString("#F2D8C1"),
      Utils.ColorFromString("#EDE4BD"),
      Utils.ColorFromString("#ED8EBF"),
      Utils.ColorFromString("#FFF47F"),
      Utils.ColorFromString("#B9FF99"),
      Utils.ColorFromString("#99E2FF"),
      Utils.ColorFromString("#C6B2FF")
    };
  }

  public ColorSelector() {
    InitializeComponent();
    ZoomTransform.ScaleX = WindowScaling;
    ZoomTransform.ScaleY = WindowScaling;
    Focusable = true;
    PreviewKeyDown += ColorSelector_PreviewKeyDown;
    Loaded += ColorSelector_Loaded;
    ButtonBrushes = new Brush[ButtonColors.Length];

    for (int i = 0; i < ButtonColors.Length; i++) {
      ButtonBrushes[i] = ColorBrushes.GetBrush(ButtonColors[i]);
    }
  }

  public event EventHandler<SelectedColorEventArgs> ColorSelected;
  public double WindowScaling => App.Settings.GeneralSettings.WindowScaling;

  public ICommand ColorSelectedCommand {
    get => (ICommand)GetValue(ColorSelectedCommandProperty);
    set => SetValue(ColorSelectedCommandProperty, value);
  }

  public IInputElement CommandTarget {
    get => (IInputElement)GetValue(CommandTargetProperty);
    set => SetValue(CommandTargetProperty, value);
  }

  private void ColorSelector_Loaded(object sender, RoutedEventArgs e) {
    Focus();
  }

  private void ColorSelector_PreviewKeyDown(object sender, KeyEventArgs e) {
    int index = e.Key switch {
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
      Key.A  => 10,
      Key.B  => 11,
      Key.C  => 12,
      Key.D  => 13,
      Key.E  => 14,
      _      => -1
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
    Utils.CloseParentMenu(this);
  }

  private void RaiseSelectedColorEvent(Color color) {
    if (ColorSelectedCommand == null && ColorSelected == null) {
      return;
    }

    var parentHost = Utils.FindParentHost(this);

    if (parentHost != null) {
      parentHost.Focus();
    }

    var args = new SelectedColorEventArgs {
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

  private void Button_MouseUp(object sender, MouseButtonEventArgs e) {
    var button = sender as Button;
    var brush = button.Background as SolidColorBrush;
    Utils.CloseParentMenu(this);
    RaiseSelectedColorEvent(brush.Color);
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