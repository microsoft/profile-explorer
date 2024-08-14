// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Text;

namespace ProfileExplorer.Core.IR;

public sealed class SourceLocationTag : ITag {
  public SourceLocationTag() { }

  public SourceLocationTag(int line, int column) {
    Line = line;
    Column = column;
  }

  public List<SourceStackFrame> Inlinees { get; set; }
  public int Line { get; set; }
  public int Column { get; set; }
  public string FilePath { get; set; }
  public bool HasInlinees => Inlinees != null && Inlinees.Count > 0;
  public string Name => "Source location";
  public TaggedObject Owner { get; set; }

  public List<SourceStackFrame> InlineesReversed {
    get {
      var clone = new List<SourceStackFrame>(Inlinees);
      clone.Reverse();
      return clone;
    }
  }

  public void AddInlinee(string function, string filePath, int line, int column) {
    Inlinees ??= new List<SourceStackFrame>();
    Inlinees.Add(new SourceStackFrame(function, filePath, line, column));
  }

  public void AddInlinee(SourceStackFrame inlinee) {
    Inlinees ??= new List<SourceStackFrame>();
    Inlinees.Add(inlinee);
  }

  public void Reset() {
    Inlinees = null;
    Line = 0;
    Column = 0;
  }

  public override bool Equals(object obj) {
    return obj is SourceLocationTag tag && Line == tag.Line && Column == tag.Column;
  }

  public override int GetHashCode() {
    return HashCode.Combine(Line, Column);
  }

  public override string ToString() {
    var builder = new StringBuilder();
    builder.Append($"source location: {Line};{Column}");

    if (Inlinees != null) {
      builder.AppendLine($"\n  inlinees: {Inlinees.Count}");

      foreach (var item in Inlinees) {
        builder.AppendLine($"    {item.Line};{item.Column}: {item.Function}");
      }
    }

    return builder.ToString();
  }
}