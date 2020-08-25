using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace IRExplorerUI.Utilities {
    public class BooleanConverter<T> : IValueConverter {
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

    public sealed class BoolToVisibilityConverter : BooleanConverter<Visibility> {
        public BoolToVisibilityConverter() :
            base(Visibility.Visible, Visibility.Collapsed) { }
    }

    public class EnumBooleanConverter : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            return value.Equals(parameter);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            return ((bool)value) ? parameter : Binding.DoNothing;
        }
    }

    public class BoolToParameterConverter : IValueConverter {
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
}
