// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;

namespace IRExplorerCore;

public class IRTextSection : IEquatable<IRTextSection> {
  private CompressedObject<Dictionary<int, string>> lineMetadata_;
  public IRTextSection() { }

  public IRTextSection(IRTextFunction parent, string name,
                       IRPassOutput sectionOutput, int blocks = 0) {
    ParentFunction = parent;
    Name = string.Intern(name);
    Output = sectionOutput;
    BlockCount = blocks;
  }

  public int Id { get; set; }
  public int Number { get; set; }
  public string Name { get; set; }
  public int BlockCount { get; set; }
  public IRTextFunction ParentFunction { get; set; }
  public string ModuleName => ParentFunction?.ParentSummary?.ModuleName;
  public int LineCount => Output.LineCount;
  public IRPassOutput Output { get; set; }
  public IRPassOutput OutputAfter { get; set; }
  public IRPassOutput OutputBefore { get; set; }

  public Dictionary<int, string> LineMetadata {
    get => lineMetadata_?.GetValue();
    set => lineMetadata_ = new CompressedObject<Dictionary<int, string>>(value);
  }

  public void AddLineMetadata(int lineNumber, string metadata) {
    LineMetadata ??= new Dictionary<int, string>();
    LineMetadata[lineNumber] = metadata;
  }

  public string GetLineMetadata(int lineNumber) {
    if (LineMetadata != null &&
        LineMetadata.TryGetValue(lineNumber, out string value)) {
      return value;
    }

    return null;
  }

  public void CompressLineMetadata() {
    if (lineMetadata_ != null) {
      lineMetadata_.Compress();
    }
  }

  public bool IsSectionTextDifferent(IRTextSection other) {
    // If there is a signature, assume that same signature means same text.
    if (Output?.Signature != null &&
        other?.Output.Signature != null) {
      return !Output.Signature.AsSpan().SequenceEqual(other.Output.Signature.AsSpan());
    }

    return true;
  }

  public override bool Equals(object obj) {
    if (ReferenceEquals(null, obj)) return false;
    if (ReferenceEquals(this, obj)) return true;
    if (obj.GetType() != GetType()) return false;
    return Equals((IRTextSection)obj);
  }

  public override int GetHashCode() {
    return HashCode.Combine(Number, ParentFunction?.GetHashCode() ?? 0);
  }

  public override string ToString() {
    return $"({Number}) {Name}";
  }

  public bool Equals(IRTextSection other) {
    if (ReferenceEquals(null, other)) {
      return false;
    }

    if (ReferenceEquals(this, other)) {
      return true;
    }

    return Number == other.Number &&
           Name.Equals(other.Name, StringComparison.Ordinal) &&
           (ParentFunction == null && other.ParentFunction == null ||
            ParentFunction != null && ParentFunction.Equals(other.ParentFunction));
  }
}
