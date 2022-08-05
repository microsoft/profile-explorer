using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace IRExplorerUI.Utilities {
    class BooleanConverter<T> : IValueConverter {
        public BooleanConverter(T trueValue, T falseValue) {
            True = trueValue;
            False = falseValue;
        }

        public T True { get; set; }
        public T False { get; set; }

        public virtual object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            return value is bool && ((bool)value) ? True : False;
        }

        public virtual object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            return value is T && EqualityComparer<T>.Default.Equals((T)value, True);
        }
    }

    class InvertedBooleanConverter : BooleanConverter<bool> {
        public InvertedBooleanConverter() :
            base(false, true) { }
    }

    class BoolToVisibilityConverter : BooleanConverter<Visibility> {
        public BoolToVisibilityConverter() :
            base(Visibility.Visible, Visibility.Collapsed) { }
    }

    class EnumBooleanConverter : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            return value.Equals(parameter);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            return ((bool)value) ? parameter : Binding.DoNothing;
        }
    }

    class BoolToParameterConverter : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            if ((bool)value) {
                return new SolidColorBrush(Utils.ColorFromString((string)parameter));
            }

            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            return null;
        }
    }

    class DoubleScalingConverter : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            double doubleValue = (double)value;

            if (doubleValue == 0.0 || Math.Abs(doubleValue) < double.Epsilon) {
                return 0.0;
            }

            double maxValue = double.Parse((string)parameter);
            return Math.Min(doubleValue * maxValue, maxValue);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            return null;
        }
    }

    class TreeViewLineConverter : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) {
            TreeViewItem item = (TreeViewItem)value;
            ItemsControl ic = ItemsControl.ItemsControlFromItemContainer(item);
            return ic.ItemContainerGenerator.IndexFromContainer(item) == ic.Items.Count - 1;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) {
            throw new Exception("The method or operation is not implemented.");
        }
    }

    class ColorBrushOpacityConverter : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) {
            var brush = value as SolidColorBrush;
            double opacity = double.Parse((string)parameter);

            if (brush == null) {
                return Brushes.Transparent;
            }

            return ColorBrushes.GetTransparentBrush(brush.Color, opacity);
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) {
            throw new Exception("The method or operation is not implemented.");
        }
    }

    class MillisecondTimeConverter : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            if(value is double doubleValue) {
                return $" {Math.Round(doubleValue, 2)} ms";
            }

            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            return null;
        }
    }

    public class ListToStringConverter : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            var sb = new StringBuilder();

            if (value is List<string> list) {
                foreach (var line in list) {
                    sb.AppendLine(line);
                }
            }

            return sb.ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            var list = new List<string>();

            if (value is string text) {
                var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                list.AddRange(lines);
            }

            return list;

        }
    }
}
