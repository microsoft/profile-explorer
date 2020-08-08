using System;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;
using IRExplorerCore.IR;

namespace IRExplorerUI.Query {
    public class BoolObjectConverter : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            try {
                return (bool)value;
            }
            catch (Exception) {
                return null;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            return value;
        }
    }

    public class ElementObjectConverter : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            try {
                return !(value is IRElement element) ? null : Utils.MakeElementDescription(element);
            }
            catch (Exception) {
                return null;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            return value;
        }
    }

    public partial class OutputQueryViewElement : UserControl {
        public OutputQueryViewElement() {
            InitializeComponent();
        }
    }
}
