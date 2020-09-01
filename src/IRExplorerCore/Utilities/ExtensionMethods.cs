using System;
using System.Collections.Generic;
using System.Text;

namespace IRExplorerCore.Utilities {
    public static class ExtensionMethods {
        public static string Indent(this String value, int spaces) {
            var whitespace = new string(' ', spaces);
            var valueNoCr = value.Replace("\r\n", "\n", StringComparison.Ordinal);
            return valueNoCr.Replace("\n", $"{Environment.NewLine}{whitespace}", StringComparison.Ordinal);
        }
    }
}
