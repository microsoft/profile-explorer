// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace ProfileExplorerExtension.Windows;

/// <summary>
///   Interaction logic for ExpressionToolWindowControl.
/// </summary>
public partial class ExpressionToolWindowControl : UserControl {
  /// <summary>
  ///   Initializes a new instance of the <see cref="ExpressionToolWindowControl" /> class.
  /// </summary>
  public ExpressionToolWindowControl() {
    InitializeComponent();
  }

  /// <summary>
  ///   Handles click on the button by displaying a message box.
  /// </summary>
  /// <param name="sender">The event sender.</param>
  /// <param name="e">The event args.</param>
  [SuppressMessage("Microsoft.Globalization", "CA1300:SpecifyMessageBoxOptions", Justification = "Sample code")]
  [SuppressMessage("StyleCop.CSharp.NamingRules", "SA1300:ElementMustBeginWithUpperCaseLetter",
                   Justification = "Default event handler naming pattern")]
  private void button1_Click(object sender, RoutedEventArgs e) {
    MessageBox.Show(
      string.Format(CultureInfo.CurrentUICulture, "Invoked '{0}'", ToString()),
      "ExpressionToolWindow");
  }
}