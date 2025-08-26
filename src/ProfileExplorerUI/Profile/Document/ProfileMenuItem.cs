// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Windows;
using System.Windows.Media;
using ProfileExplorerCore.IR;

namespace ProfileExplorer.UI.Profile.Document;

public class ProfileMenuItem : BindableObject {
  private Thickness borderThickness_;
  private Brush borderBrush_;
  private string text_;
  private string prefixText_;
  private double minTextWidth_;
  private string toolTip_;
  private Brush textColor_;
  private Brush backColor_;
  private bool showPercentageBar_;
  private Brush percentageBarBackColor__;
  private double percentageBarBorderThickness_;
  private Brush percentageBarBorderBrush_;
  private FontWeight textWeight_;
  private double textSize_;
  private FontFamily textFont_;

  public ProfileMenuItem(string text, long value = 0, double valueValuePercentage = 0.0) {
    Text = text;
    Value = value;
    ValuePercentage = valueValuePercentage;
    TextWeight = FontWeights.Normal;
    TextColor = Brushes.Black;
  }

  public IRElement Element { get; set; }
  public long Value { get; set; }
  public double ValuePercentage { get; set; }

  public Thickness BorderThickness {
    get => borderThickness_;
    set => SetAndNotify(ref borderThickness_, value);
  }

  public Brush BorderBrush {
    get => borderBrush_;
    set => SetAndNotify(ref borderBrush_, value);
  }

  public string Text {
    get => text_;
    set => SetAndNotify(ref text_, value);
  }

  public string PrefixText {
    get => prefixText_;
    set => SetAndNotify(ref prefixText_, value);
  }

  public double MinTextWidth {
    get => minTextWidth_;
    set => SetAndNotify(ref minTextWidth_, value);
  }

  public string ToolTip {
    get => toolTip_;
    set => SetAndNotify(ref toolTip_, value);
  }

  public Brush TextColor {
    get => textColor_;
    set => SetAndNotify(ref textColor_, value);
  }

  public Brush BackColor {
    get => backColor_;
    set => SetAndNotify(ref backColor_, value);
  }

  public bool ShowPercentageBar {
    get => showPercentageBar_;
    set => SetAndNotify(ref showPercentageBar_, value);
  }

  public Brush PercentageBarBackColor {
    get => percentageBarBackColor__;
    set => SetAndNotify(ref percentageBarBackColor__, value);
  }

  public double PercentageBarBorderThickness {
    get => percentageBarBorderThickness_;
    set => SetAndNotify(ref percentageBarBorderThickness_, value);
  }

  public Brush PercentageBarBorderBrush {
    get => percentageBarBorderBrush_;
    set => SetAndNotify(ref percentageBarBorderBrush_, value);
  }

  public FontWeight TextWeight {
    get => textWeight_;
    set => SetAndNotify(ref textWeight_, value);
  }

  public double TextSize {
    get => textSize_;
    set => SetAndNotify(ref textSize_, value);
  }

  public FontFamily TextFont {
    get => textFont_;
    set => SetAndNotify(ref textFont_, value);
  }
}