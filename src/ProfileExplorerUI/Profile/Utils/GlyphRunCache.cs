// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace ProfileExplorer.UI.Profile;

public class GlyphRunCache {
  private static readonly Point ZeroPoint = new(0, 0);
  private readonly GlyphTypeface glyphTypeface_;
  private readonly Dictionary<string, Dictionary<double, GlyphInfo>> textGlyphsCache_;
  private readonly Typeface typeFace_;
  private readonly double textSize_;
  private readonly float pixelsPerDip_;
  private readonly bool useGlyphIndexFastPath_;
  private readonly double[] advanceGlyphWidths_;

  public GlyphRunCache(Typeface typeFace, double textSize, double pixelsPerDip) {
    typeFace_ = typeFace;
    textSize_ = textSize;
    pixelsPerDip_ = (float)pixelsPerDip;
    textGlyphsCache_ = new Dictionary<string, Dictionary<double, GlyphInfo>>();

    if (!typeFace.TryGetGlyphTypeface(out glyphTypeface_)) {
      throw new InvalidOperationException("Failed to get GlyphTypeface");
    }

    // Check if a direct mapping from ASCII code to glyph index is possible.
    char testLetter = 'a';
    ushort glyphIndex = glyphTypeface_.CharacterToGlyphMap[testLetter];
    useGlyphIndexFastPath_ = glyphIndex == testLetter - 29;

    if (useGlyphIndexFastPath_) {
      advanceGlyphWidths_ = new double[glyphTypeface_.GlyphCount];

      for (int i = 0; i < glyphTypeface_.GlyphCount; i++) {
        advanceGlyphWidths_[i] = glyphTypeface_.AdvanceWidths[(ushort)i] * textSize_;
      }
    }
  }

  public GlyphInfo GetGlyphs(string text) {
    return GetGlyphs(text, double.MaxValue);
  }

  public GlyphInfo GetGlyphs(string text, double maxWidth) {
    if (textGlyphsCache_.TryGetValue(text, out var glyphsList)) {
      if (glyphsList.TryGetValue(maxWidth, out var info)) {
        return info;
      }
    }

    return MakeGlyphRun(text);
  }

  public void CacheGlyphs(GlyphInfo info, string text, double maxWidth) {
    if (info.IsCached) {
      return;
    }

    if (!textGlyphsCache_.TryGetValue(text, out var glyphsList)) {
      glyphsList = new Dictionary<double, GlyphInfo>(new MaxWidthComparer());
      textGlyphsCache_[text] = glyphsList;
    }

    glyphsList[maxWidth] = info;
  }

  private GlyphInfo MakeGlyphRun(string text) {
    if (string.IsNullOrEmpty(text)) {
      text = " "; // GlyphRun constructor doesn't like 0-length arrays.
    }

    double size = textSize_;
    double totalWidth = 0;
    ushort[] glyphIndexes = new ushort[text.Length];
    double[] advanceWidths = new double[text.Length];

    for (int i = 0; i < text.Length; i++) {
      ushort glyphIndex;

      if (useGlyphIndexFastPath_) {
        glyphIndex = (ushort)(text[i] - 29);
      }
      else {
        glyphIndex = glyphTypeface_.CharacterToGlyphMap[text[i]];
      }

      glyphIndexes[i] = glyphIndex;
      double glyphWidth = 0;

      if (useGlyphIndexFastPath_) {
        glyphWidth = advanceGlyphWidths_[glyphIndex];
      }
      else {
        glyphWidth = glyphTypeface_.AdvanceWidths[glyphIndex] * size;
      }

      advanceWidths[i] = glyphWidth;
      totalWidth += glyphWidth;
    }

    var glyphRun = new GlyphRun(glyphTypeface_, 0, false, size, pixelsPerDip_,
                                glyphIndexes, ZeroPoint, advanceWidths,
                                null, null, null, null, null, null);
    double height = glyphTypeface_.Height * size;
    return new GlyphInfo(glyphRun, totalWidth, height, false);
  }

  public struct GlyphInfo {
    public GlyphRun Glyphs;
    public double TextWidth;
    public double TextHeight;
    public bool IsCached;
    public bool IsTrimmed;

    public GlyphInfo(GlyphRun glyphs, double textWidth, double textHeight, bool isTrimmed) {
      Glyphs = glyphs;
      TextWidth = textWidth;
      TextHeight = textHeight;
      IsTrimmed = isTrimmed;
      IsCached = false;
    }
  }

  private class MaxWidthComparer : IEqualityComparer<double> {
    public bool Equals(double x, double y) {
      return Math.Abs(x - y) < double.Epsilon;
    }

    public int GetHashCode(double value) {
      return (int)value;
    }
  }
}