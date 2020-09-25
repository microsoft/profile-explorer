// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace IRExplorerExtension.Windows {
    [Guid("719cf141-c681-4762-88b2-386313fd36d4")]
    public class ExpressionToolWindow : ToolWindowPane {
        /// <summary>
        /// Initializes a new instance of the <see cref="ExpressionToolWindow"/> class.
        /// </summary>
        public ExpressionToolWindow() : base(null) {
            Caption = "IR Explorer Expression";
            Content = new ExpressionToolWindowControl();
        }
    }
}
