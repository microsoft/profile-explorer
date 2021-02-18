// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using IRExplorerCore.IR;
using ProtoBuf;
using ProtoBuf.Meta;

namespace IRExplorerUI {
    static class StateSerializer {
        public static readonly int subtypeIdStep_ = 100;
        public static int nextSubtypeId_;

        static StateSerializer() {
            RegisterSurrogate<Color, ColorSurrogate>();
            RegisterSurrogate<Brush, BrushSurrogate>();
            RegisterSurrogate<Pen, PenSurrogate>();
            RegisterSurrogate<Rect, RectSurrogate>();
            RegisterSurrogate<FontWeight, FontWeightSurrogate>();
        }

        public static void RegisterSurrogate<T1, T2>() {
            RegisterSurrogate(typeof(T1), typeof(T2));
        }

        public static void RegisterSurrogate(Type realType, Type surrogateType) {
            var model = RuntimeTypeModel.Default;
            model.Add(surrogateType);
            model.Add(realType, false).SetSurrogate(surrogateType);
        }

        public static void RegisterDerivedClass<T1, T2>(int id = 0) {
            RegisterDerivedClass(typeof(T1), typeof(T2), id);
        }

        public static void RegisterDerivedClass(Type derivedType, Type baseType, int id = 0) {
            var model = RuntimeTypeModel.Default;

            if (id == 0) {
                nextSubtypeId_ += subtypeIdStep_;
                id = nextSubtypeId_;
            }

            model.Add(baseType, false).AddSubType(id, derivedType);
        }

        public static byte[] Serialize<T>(T state, FunctionIR function = null) where T : class {
            var stream = new MemoryStream();
            Serializer.Serialize(stream, state);
            return stream.ToArray();
        }

        public static T Deserialize<T>(byte[] data, FunctionIR function) where T : class {
            var value = Deserialize<T>(data);

            if (value != null) {
                PatchIRElementObjects(value, function);
                return value;
            }

            return null;
        }

        public static T Deserialize<T>(byte[] data) where T : class {
            if (data == null) {
                return null;
            }

            var stream = new MemoryStream(data);
            return Serializer.Deserialize<T>(stream);
        }

        public static T Deserialize<T>(object data, FunctionIR function) where T : class {
            return Deserialize<T>((byte[])data, function);
        }

        public static T Deserialize<T>(object data) where T : class {
            return Deserialize<T>((byte[])data);
        }

        public static List<ElementGroupState> SaveElementGroupState(List<HighlightedSegmentGroup> groups) {
            var groupStates = new List<ElementGroupState>();

            foreach (var segmentGroup in groups) {
                if (!segmentGroup.SavesStateToFile) {
                    continue;
                }

                var groupState = new ElementGroupState();
                groupStates.Add(groupState);
                groupState.Style = segmentGroup.Group.Style;

                foreach (var item in segmentGroup.Group.Elements) {
                    groupState.Elements.Add(new IRElementReference(item));
                }
            }

            return groupStates;
        }

        public static List<HighlightedSegmentGroup>
            LoadElementGroupState(List<ElementGroupState> groupStates) {
            var groups = new List<HighlightedSegmentGroup>();

            if (groupStates == null) {
                return groups;
            }

            foreach (var groupState in groupStates) {
                var group = new HighlightedGroup(groupState.Style);

                foreach (var item in groupState.Elements) {
                    if (item.Value != null) {
                        group.Add(item);
                    }
                }

                groups.Add(new HighlightedSegmentGroup(group));
            }

            return groups;
        }

        public static void PatchIRElementObjects(object value, FunctionIR function) {
            if (value == null) {
                return;
            }

            if (value is IRElementReference elementRef) {
                elementRef.Value = function.GetElementWithId(elementRef.Id);
                return;
            }
            else if (!value.GetType().GetTypeInfo().IsClass) {
                return; // Don't walk primitive types.
            }

            if (value is IList list) {
                foreach (var item in list) {
                    PatchIRElementObjects(item, function);
                }
            }
            else if (value is IDictionary dict) {
                foreach (var item in dict.Keys) {
                    PatchIRElementObjects(item, function);
                }

                foreach (var item in dict.Values) {
                    PatchIRElementObjects(item, function);
                }
            }
            else {
                var fields = value.GetType().GetFields(BindingFlags.Public |
                                                       BindingFlags.NonPublic |
                                                       BindingFlags.Instance);

                foreach (var field in fields) {
                    PatchIRElementObjects(field.GetValue(value), function);
                }
            }
        }
    }

    [ProtoContract]
    public class ColorSurrogate {
        [ProtoMember(4)]
        public byte A;
        [ProtoMember(3)]
        public byte B;
        [ProtoMember(2)]
        public byte G;
        [ProtoMember(1)]
        public byte R;

        public static implicit operator ColorSurrogate(Color color) {
            return new ColorSurrogate {
                R = color.R,
                G = color.G,
                B = color.B,
                A = color.A
            };
        }

        public static implicit operator Color(ColorSurrogate value) {
            return Color.FromArgb(value.A, value.R, value.G, value.B);
        }
    }

    [ProtoContract]
    public class BrushSurrogate {
        [ProtoMember(4)]
        public byte A;
        [ProtoMember(3)]
        public byte B;
        [ProtoMember(2)]
        public byte G;
        [ProtoMember(1)]
        public byte R;

        public static implicit operator BrushSurrogate(Brush brush) {
            if (!(brush is SolidColorBrush colorBrush)) {
                return null;
            }

            return new BrushSurrogate {
                R = colorBrush.Color.R,
                G = colorBrush.Color.G,
                B = colorBrush.Color.B,
                A = colorBrush.Color.A
            };
        }

        public static implicit operator Brush(BrushSurrogate value) {
            var color = Color.FromArgb(value.A, value.R, value.G, value.B);
            return Utils.BrushFromColor(color);
        }
    }

    [ProtoContract]
    public class PenSurrogate {
        [ProtoMember(1)]
        private BrushSurrogate Brush;
        [ProtoMember(2)]
        private double Thickness;

        public static implicit operator PenSurrogate(Pen pen) {
            if (pen == null) {
                return null;
            }

            return new PenSurrogate {
                Brush = pen.Brush,
                Thickness = pen.Thickness
            };
        }

        public static implicit operator Pen(PenSurrogate value) {
            return ColorPens.GetPen(value.Brush, value.Thickness);
        }
    }

    [ProtoContract]
    public class RectSurrogate {
        [ProtoMember(2)]
        private double Left;
        [ProtoMember(1)]
        private double Top;
        [ProtoMember(3)]
        private double Width;
        [ProtoMember(4)]
        private double Height;

        public static implicit operator RectSurrogate(Rect rect) {
            if (rect == null) {
                return null;
            }

            return new RectSurrogate {
                Top = rect.Top,
                Left = rect.Left,
                Width = rect.Width,
                Height = rect.Height
            };
        }

        public static implicit operator Rect(RectSurrogate value) {
            return new Rect(value.Top, value.Left, value.Width, value.Height);
        }
    }

    [ProtoContract]
    public class FontWeightSurrogate {
        [ProtoMember(1)]
        private int Weight;

        public static implicit operator FontWeightSurrogate(FontWeight fontWeigth) {
            if (fontWeigth == null) {
                return null;
            }

            return new FontWeightSurrogate {
                Weight = fontWeigth.ToOpenTypeWeight()
            };
        }

        public static implicit operator FontWeight(FontWeightSurrogate value) {
            return FontWeight.FromOpenTypeWeight(value.Weight);
        }
    }
}
