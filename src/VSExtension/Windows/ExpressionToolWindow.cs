// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace ProfileExplorerExtension.Windows;

[Guid("719cf141-c681-4762-88b2-386313fd36d4")]
public class ExpressionToolWindow : ToolWindowPane {
  /// <summary>
  ///   Initializes a new instance of the <see cref="ExpressionToolWindow" /> class.
  /// </summary>
  public ExpressionToolWindow() : base(null) {
    Caption = "Profile Explorer Expression";
    Content = new ExpressionToolWindowControl();
  }
}