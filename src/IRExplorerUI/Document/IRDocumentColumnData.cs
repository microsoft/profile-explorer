using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using IRExplorerCore.IR;
using IRExplorerCore.Utilities;

namespace IRExplorerUI {
    public class IRDocumentColumnData {
        public List<OptionalColumn> Columns { get; set; }
        public Dictionary<IRElement, ElementRowValue> Values { get; set; }
        

        public IRDocumentColumnData(int capacity = 0) {
            Columns = new List<OptionalColumn>();
            Values = new Dictionary<IRElement, ElementRowValue>(capacity);
        }

        public bool HasData => Values.Count > 0;
        public OptionalColumn MainColumn => Columns.Find(column => column.IsMainColumn);

        public OptionalColumn AddColumn(OptionalColumn column) {
            // Make a clone so that changing values such as the width
            // doesn't modify the column template.
            column = (OptionalColumn)column.Clone();
            Columns.Add(column);
            return column;
        }

        public ElementRowValue AddValue(ElementColumnValue value, IRElement element, OptionalColumn column) {
            if (!Values.TryGetValue(element, out var valueGroup)) {
                valueGroup = new ElementRowValue(element);
                Values[element] = valueGroup;
            }

            valueGroup.ColumnValues[column] = value;
            value.Element = element;
            return valueGroup;
        }

        public ElementRowValue GetValues(IRElement element) {
            if (Values.TryGetValue(element, out var valueGroup)) {
                return valueGroup;
            }

            return null;
        }

        public ElementColumnValue GetColumnValue(IRElement element, OptionalColumn column) {
            var values = GetValues(element);
            return values?[column];
        }

        public void Reset() {

        }
    }


    public class ElementColumnValue : BindableObject {
        public ElementColumnValue(string text, long value = 0, double valueValuePercentage = 0.0, 
                                int valueOrder = int.MaxValue, string tooltip = null) {
            Text = text;
            Value = value;
            ValuePercentage = valueValuePercentage;
            ValueOrder = valueOrder;
            TextWeight = FontWeights.Normal;
            TextColor = Brushes.Black;
            ToolTip = tooltip;
        }

        public static ElementColumnValue Empty => new ElementColumnValue(string.Empty);

        public IRElement Element { get; set; }
        public long Value { get; set; }
        public double ValuePercentage { get; set; }
        public int ValueOrder { get; set; }

        private Thickness borderThickness_;
        public Thickness BorderThickness {
            get => borderThickness_;
            set => SetAndNotify(ref borderThickness_, value);
        }

        private Brush borderBrush_;
        public Brush BorderBrush {
            get => borderBrush_;
            set => SetAndNotify(ref borderBrush_, value);
        }

        private string text_;
        public string Text {
            get => text_;
            set => SetAndNotify(ref text_, value);
        }

        private double minTextWidth_;
        public double MinTextWidth {
            get => minTextWidth_;
            set => SetAndNotify(ref minTextWidth_, value);
        }

        private string toolTip_;
        public string ToolTip {
            get => toolTip_;
            set => SetAndNotify(ref toolTip_, value);
        }

        private Brush textColor_;

        public Brush TextColor {
            get => textColor_;
            set => SetAndNotify(ref textColor_, value);
        }

        private Brush backColor_;

        public Brush BackColor {
            get => backColor_;
            set => SetAndNotify(ref backColor_, value);
        }

        private ImageSource icon_;

        public ImageSource Icon {
            get => icon_;
            set {
                SetAndNotify(ref icon_, value);
                Notify(nameof(ShowIcon));
            }
        }

        public bool ShowIcon => icon_ != null;

        private bool showPercentageBar_;
        public bool ShowPercentageBar {
            get => showPercentageBar_;
            set => SetAndNotify(ref showPercentageBar_, value);
        }

        private Brush percentageBarBackColor__;
        public Brush PercentageBarBackColor {
            get => percentageBarBackColor__;
            set => SetAndNotify(ref percentageBarBackColor__, value);
        }

        private double percentageBarBorderThickness_;
        public double PercentageBarBorderThickness {
            get => percentageBarBorderThickness_;
            set => SetAndNotify(ref percentageBarBorderThickness_, value);
        }

        private Brush percentageBarBorderBrush_;
        public Brush PercentageBarBorderBrush {
            get => percentageBarBorderBrush_;
            set => SetAndNotify(ref percentageBarBorderBrush_, value);
        }

        private FontWeight textWeight_;

        public FontWeight TextWeight {
            get => textWeight_;
            set => SetAndNotify(ref textWeight_, value);
        }

        private double textSize_;
        public double TextSize {
            get => textSize_;
            set => SetAndNotify(ref textSize_, value);
        }

        private FontFamily textFont_;
        public FontFamily TextFont {
            get => textFont_;
            set => SetAndNotify(ref textFont_, value);
        }
    }


    public class ElementRowValue : BindableObject {
        public ElementRowValue(IRElement element) {
            Element = element;
            ColumnValues = new Dictionary<OptionalColumn, ElementColumnValue>();
        }

        public IRElement Element { get; set; }
        public Dictionary<OptionalColumn, ElementColumnValue> ColumnValues { get; set; }
        public ICollection<ElementColumnValue> Values => ColumnValues.Values;
        public ICollection<OptionalColumn> Columns => ColumnValues.Keys;
        public int Count => ColumnValues.Count;

        public ElementColumnValue this[OptionalColumn column] {
            get => ColumnValues.GetValueOrNull(column);
        }

        public ElementColumnValue this[string columnName] {
            get {
                foreach (var pair in ColumnValues) {
                    if (pair.Key.ColumnName == columnName) {
                        return pair.Value;
                    }
                }
                
                return null;
            }
        }

        private Brush backColor_;
        public Brush BackColor {
            get => backColor_;
            set => SetAndNotify(ref backColor_, value);
        }

        private Thickness borderThickness_;
        public Thickness BorderThickness {
            get => borderThickness_;
            set => SetAndNotify(ref borderThickness_, value);
        }

        private Brush borderBrush_;
        public Brush BorderBrush {
            get => borderBrush_;
            set => SetAndNotify(ref borderBrush_, value);
        }
    }
}
