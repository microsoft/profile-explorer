using System;
using System.Windows.Media;

namespace IRExplorerUI;

[AttributeUsage(AttributeTargets.All)]
public class OptionValueAttribute : Attribute {
  public object Value { get; set; }

  public OptionValueAttribute(object value) {
    Value = value;
  }

  public OptionValueAttribute(Type type, string convertedValue) {
    if (type == typeof(Color)) {
      Value = Utils.ColorFromString(convertedValue);
    }
    else {
      throw new InvalidOperationException("Type not handled");
    }
  }
}
