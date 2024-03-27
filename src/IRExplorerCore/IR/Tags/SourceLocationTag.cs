// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Text;

namespace IRExplorerCore.IR;

public sealed class SourceLocationTag : ITag {
  public SourceLocationTag() { }

  public SourceLocationTag(int line, int column) {
    Line = line;
    Column = column;
  }

  public List<StackFrame> Inlinees { get; set; }
  public int Line { get; set; }
  public int Column { get; set; }
  public string FilePath { get; set; }
  public bool HasInlinees => Inlinees != null && Inlinees.Count > 0;
  public string Name => "Source location";
  public TaggedObject Owner { get; set; }

  public List<StackFrame> InlineesReversed {
    get {
      var clone = new List<StackFrame>(Inlinees);
      clone.Reverse();
      return clone;
    }
  }

  public void AddInlinee(string function, string filePath, int line, int column) {
    Inlinees ??= new List<StackFrame>();
    Inlinees.Add(new StackFrame(function, filePath, line, column));
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