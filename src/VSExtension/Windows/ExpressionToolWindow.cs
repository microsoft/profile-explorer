// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace IRExplorerExtension.Windows;

[Guid("719cf141-c681-4762-88b2-386313fd36d4")]
public class ExpressionToolWindow : ToolWindowPane {
  /// <summary>
  ///   Initializes a new instance of the <see cref="ExpressionToolWindow" /> class.
  /// </summary>
  public ExpressionToolWindow() : base(null) {
    Caption = "IR Explorer Expression";
    Content = new ExpressionToolWindowControl();
  }
}
