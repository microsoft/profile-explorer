// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using ProfileExplorer.Core.IR;
using ProfileExplorer.Core.Session;
using ProtoBuf;

namespace ProfileExplorer.UI;

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
    return new FontWeightSurrogate {
      Weight = fontWeigth.ToOpenTypeWeight()
    };
  }

  public static implicit operator FontWeight(FontWeightSurrogate value) {
    return FontWeight.FromOpenTypeWeight(value.Weight);
  }
}

public static class UIStateSerializer {
  static UIStateSerializer() {
    // Initialize the core StateSerializer
    StateSerializer.Initialize();
    
    // Register UI-specific surrogates
    StateSerializer.RegisterSurrogate<Color, ColorSurrogate>();
    StateSerializer.RegisterSurrogate<Brush, BrushSurrogate>();
    StateSerializer.RegisterSurrogate<Pen, PenSurrogate>();
    StateSerializer.RegisterSurrogate<Rect, RectSurrogate>();
    StateSerializer.RegisterSurrogate<FontWeight, FontWeightSurrogate>();
  }

  // UI-specific functionality for working with highlighted elements
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
      var group = new HighlightedElementGroup(groupState.Style);

      foreach (var item in groupState.Elements) {
        if (item.Value != null) {
          group.Add(item);
        }
      }

      groups.Add(new HighlightedSegmentGroup(group));
    }

    return groups;
  }

  // Delegate core serialization functionality to the Core StateSerializer
  public static byte[] Serialize<T>(T state, FunctionIR function = null) where T : class {
    return StateSerializer.Serialize(state, function);
  }

  public static bool Serialize<T>(string filePath, T state, FunctionIR function = null) where T : class {
    return StateSerializer.Serialize(filePath, state, function);
  }

  public static T Deserialize<T>(byte[] data, FunctionIR function) where T : class {
    return StateSerializer.Deserialize<T>(data, function);
  }

  public static T Deserialize<T>(byte[] data) where T : class {
    return StateSerializer.Deserialize<T>(data);
  }

  public static T Deserialize<T>(string filePath) where T : class {
    return StateSerializer.Deserialize<T>(filePath);
  }

  public static T Deserialize<T>(object data, FunctionIR function) where T : class {
    return StateSerializer.Deserialize<T>(data, function);
  }

  public static T Deserialize<T>(object data) where T : class {
    return StateSerializer.Deserialize<T>(data);
  }

  // Expose core functionality for advanced scenarios
  public static void RegisterSurrogate<T1, T2>() {
    StateSerializer.RegisterSurrogate<T1, T2>();
  }

  public static void RegisterSurrogate(Type realType, Type surrogateType) {
    StateSerializer.RegisterSurrogate(realType, surrogateType);
  }

  public static void RegisterDerivedClass<T1, T2>(int id = 0) {
    StateSerializer.RegisterDerivedClass<T1, T2>(id);
  }

  public static void RegisterDerivedClass(Type derivedType, Type baseType, int id = 0) {
    StateSerializer.RegisterDerivedClass(derivedType, baseType, id);
  }
}