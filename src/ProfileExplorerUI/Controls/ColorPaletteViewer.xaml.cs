﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ProfileExplorer.UI.Controls;

/// <summary>
/// Interaction logic for ColorPaletteViewer.xaml
/// </summary>
public partial class ColorPaletteViewer : UserControl {
  public static readonly DependencyProperty PaletteProperty =
    DependencyProperty.Register("Palette", typeof(ColorPalette), typeof(ColorPaletteViewer),
                                new PropertyMetadata(null, OnPaletteChanged));

  public ColorPaletteViewer() {
    InitializeComponent();
  }

  public ColorPalette Palette {
    get => (ColorPalette)GetValue(PaletteProperty);
    set {
      SetValue(PaletteProperty, value);
      InvalidateVisual();
    }
  }

  private static void OnPaletteChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
    var source = d as ColorPaletteViewer;
    source.Palette = e.NewValue as ColorPalette;
  }

  protected override void OnRender(DrawingContext dc) {
    var palette = Palette;

    if (palette == null || palette.Colors.Count == 0) {
      return;
    }

    double width = ActualWidth;
    double height = ActualHeight;
    double cellWidth = width / palette.Colors.Count;
    var pen = ColorPens.GetTransparentPen(Colors.Black, 50, 0.5);

    for (int i = 0; i < palette.Colors.Count; i++) {
      var color = palette.Colors[i].AsBrush();
      dc.DrawRectangle(color, pen, new Rect(i * cellWidth, 0, cellWidth, height));
    }
  }
}