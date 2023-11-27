// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System.Windows.Media;

namespace IRExplorerUI;

public static class DefaultHighlightingStyles {
  public static HighlightingStyleCollection StyleSet;
  public static HighlightingStyleCollection LightStyleSet;

  static DefaultHighlightingStyles() {
    StyleSet = new HighlightingStyleCollection();

    StyleSet.Styles.Add(
      new HighlightingStyle(Color.FromRgb(254, 202, 165), ColorPens.GetPen(Colors.Gray)));

    StyleSet.Styles.Add(
      new HighlightingStyle(Color.FromRgb(232, 254, 165), ColorPens.GetPen(Colors.Gray)));

    StyleSet.Styles.Add(
      new HighlightingStyle(Color.FromRgb(173, 254, 165), ColorPens.GetPen(Colors.Gray)));

    StyleSet.Styles.Add(
      new HighlightingStyle(Color.FromRgb(165, 180, 254), ColorPens.GetPen(Colors.Gray)));

    StyleSet.Styles.Add(
      new HighlightingStyle(Color.FromRgb(254, 165, 187), ColorPens.GetPen(Colors.Gray)));

    LightStyleSet = new HighlightingStyleCollection();
    LightStyleSet.Styles.Add(new HighlightingStyle(Color.FromRgb(253, 231, 216)));
    LightStyleSet.Styles.Add(new HighlightingStyle(Color.FromRgb(244, 253, 216)));
    LightStyleSet.Styles.Add(new HighlightingStyle(Color.FromRgb(220, 253, 216)));
    LightStyleSet.Styles.Add(new HighlightingStyle(Color.FromRgb(216, 223, 253)));
    LightStyleSet.Styles.Add(new HighlightingStyle(Color.FromRgb(253, 216, 226)));
  }

  public static HighlightingStyleCollection GetStyleSetWithBorder(
    HighlightingStyleCollection baseStyleSet, Pen pen) {
    var newStyle = new HighlightingStyleCollection();

    foreach (var style in baseStyleSet.Styles) {
      newStyle.Styles.Add(new HighlightingStyle(style.BackColor, pen));
    }

    return newStyle;
  }
}
