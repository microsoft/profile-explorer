// unset

using System;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;

namespace IRExplorerUI {
    class EnumNameConverter : EnumConverter {
        private Type enumType_;

        public EnumNameConverter(Type type) : base(type) {
            enumType_ = type;
        }
        
        public override bool CanConvertTo(ITypeDescriptorContext context, Type destType) {
            return destType == typeof(string);
        }

        public override object ConvertTo(ITypeDescriptorContext context, CultureInfo culture,
                                         object value, Type destType) {
            FieldInfo fi = enumType_.GetField(Enum.GetName(enumType_, value));
            DescriptionAttribute dna = (DescriptionAttribute)Attribute.GetCustomAttribute(fi,
                typeof(DescriptionAttribute));
            return dna != null ? dna.Description : value.ToString();
        }

        public override bool CanConvertFrom(ITypeDescriptorContext context, Type srcType) {
            return srcType == typeof(string);
        }

        public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value) {
            foreach (FieldInfo fi in enumType_.GetFields()) {
                DescriptionAttribute dna = (DescriptionAttribute)Attribute.GetCustomAttribute(fi,
                                            typeof(DescriptionAttribute));
                if ((dna != null) && ((string)value == dna.Description)) {
                    return Enum.Parse(enumType_, fi.Name);
                }
            }

            return Enum.Parse(enumType_, (string)value);
        }
    }
}