// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace ProfileExplorer.UI.Controls;

/// <summary>
/// Interaction logic for ColorPaletteSelector.xaml
/// </summary>
public partial class ColorPaletteSelector : UserControl, INotifyPropertyChanged {
  public static readonly DependencyProperty SelectedPaletteProperty =
    DependencyProperty.Register("SelectedPalette", typeof(ColorPalette), typeof(ColorPaletteSelector),
                                new PropertyMetadata(null, OnSelectedPaletteChanged));
  public static readonly DependencyProperty PalettesSourceProperty =
    DependencyProperty.Register("PalettesSource", typeof(List<ColorPalette>), typeof(ColorPaletteSelector),
                                new PropertyMetadata(null));

  public ColorPaletteSelector() {
    InitializeComponent();
    ZoomTransform.ScaleX = WindowScaling;
    ZoomTransform.ScaleY = WindowScaling;
  }

  public double PreviewWidth {
    get => PaletteList.Width;
    set => PaletteList.Width = value;
  }

  public ColorPalette SelectedPalette {
    get => (ColorPalette)GetValue(SelectedPaletteProperty);
    set {
      if (value != SelectedPalette) {
        SetValue(SelectedPaletteProperty, value);
        PaletteViewer.Palette = value;
        OnPropertyChanged();
      }
    }
  }

  public List<ColorPalette> PalettesSource {
    get => (List<ColorPalette>)GetValue(PalettesSourceProperty);
    set {
      if (value != PalettesSource) {
        SetValue(PalettesSourceProperty, value);
        PaletteList.ItemsSource = value;
        OnPropertyChanged();
      }
    }
  }

  public double WindowScaling => App.Settings.GeneralSettings.WindowScaling;
  public event PropertyChangedEventHandler PropertyChanged;

  private static void OnSelectedPaletteChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
    var sender = d as ColorPaletteSelector;
    var value = e.NewValue as ColorPalette;
    sender.SelectedPalette = value;
    sender.PaletteViewer.Palette = value;
  }

  private void PaletteList_OnSelectionChanged(object sender, SelectionChangedEventArgs e) {
    SelectedPalette = PaletteList.SelectedItem as ColorPalette;
    PaletteSplitButton.IsOpen = false;
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

  private void PaletteSplitButton_OnClick(object sender, RoutedEventArgs e) {
    PaletteSplitButton.IsOpen = true;
  }
}