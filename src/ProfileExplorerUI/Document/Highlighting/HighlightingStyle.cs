// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Windows.Media;
using ProtoBuf;

namespace ProfileExplorer.UI;

[ProtoContract]
public sealed class HighlightingStyle : IEquatable<HighlightingStyle> {
  public HighlightingStyle() { }
  public HighlightingStyle(Color color) : this(color, 1.0) { }
  public HighlightingStyle(Color color, Pen border = null) : this(color, 1.0, border) { }

  public HighlightingStyle(string color, Pen border = null) : this(
    Utils.ColorFromString(color), 1.0, border) {
  }

  public HighlightingStyle(Color color, double opacity = 1.0, Pen border = null) {
    BackColor = Math.Abs(opacity - 1.0) < double.Epsilon ?
      ColorBrushes.GetBrush(color) :
      ColorBrushes.GetTransparentBrush(color, opacity);
    Border = border;
  }

  public HighlightingStyle(Brush backColor, Pen border = null) {
    BackColor = backColor;
    Border = border;
  }

  [ProtoMember(1)] public Brush BackColor { get; set; }
  [ProtoMember(2)] public Pen Border { get; set; }

  public static bool operator ==(HighlightingStyle left, HighlightingStyle right) {
    return Equals(left, right);
  }

  public static bool operator !=(HighlightingStyle left, HighlightingStyle right) {
    return !Equals(left, right);
  }

  public override bool Equals(object obj) {
    return ReferenceEquals(this, obj) || obj is HighlightingStyle other && Equals(other);
  }

  public override int GetHashCode() {
    return HashCode.Combine(BackColor, Border);
  }

  public bool Equals(HighlightingStyle other) {
    if (ReferenceEquals(null, other)) {
      return false;
    }

    if (ReferenceEquals(this, other)) {
      return true;
    }

    return Equals(BackColor, other.BackColor) && Equals(Border, other.Border);
  }
}

public sealed class PairHighlightingStyle {
  public PairHighlightingStyle() {
    ParentStyle = new HighlightingStyle();
    ChildStyle = new HighlightingStyle();
  }

  public HighlightingStyle ParentStyle { get; set; }
  public HighlightingStyle ChildStyle { get; set; }
}

public class HighlightingStyleCollection {
  public HighlightingStyleCollection(List<HighlightingStyle> styles = null) {
    if (styles == null) {
      Styles = new List<HighlightingStyle>();
    }
    else {
      Styles = styles;
    }
  }

  public List<HighlightingStyle> Styles { get; set; }

  public HighlightingStyle ForIndex(int index) {
    return Styles[index % Styles.Count];
  }
}

public class HighlightingStyleCyclingCollection : HighlightingStyleCollection {
  private int counter_;
  public HighlightingStyleCyclingCollection(List<HighlightingStyle> styles = null) : base(styles) { }

  public HighlightingStyleCyclingCollection(HighlightingStyleCollection styleSet) : base(
    styleSet.Styles) {
  }

  public HighlightingStyle GetNext() {
    var style = ForIndex(counter_);
    counter_ = (counter_ + 1) % Styles.Count;
    return style;
  }
}