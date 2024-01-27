using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace IRExplorerUI.Controls;
/// <summary>
/// Interaction logic for ColorPaletteViewer.xaml
/// </summary>
public partial class ColorPaletteViewer : UserControl {
  public static readonly DependencyProperty PaletteProperty =
    DependencyProperty.Register("Palette", typeof(ColorPalette), typeof(ColorPaletteViewer),
                                new PropertyMetadata(null, OnPaletteChanged));

  private static void OnPaletteChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
    var source = d as ColorPaletteViewer;
    source.Palette = e.NewValue as ColorPalette;
  }

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

  protected override void OnRender(DrawingContext dc) {
    var palette = Palette;

    if (palette == null || palette.Colors.Count == 0) {
      Trace.WriteLine("Nothing to render");
      return;
    }

    Trace.WriteLine($"Render palette {Palette.Name}");

    double width  = ActualWidth;
    double height = ActualHeight;
    double cellWidth = width / palette.Colors.Count;
    var pen = ColorPens.GetTransparentPen(Colors.Black, 50, 0.5);

    for (int i = 0; i < palette.Colors.Count; i++) {
      var color = palette.Colors[i].AsBrush();
      dc.DrawRectangle(color, pen, new Rect(i * cellWidth, 0, cellWidth, height));
    }
  }
}
