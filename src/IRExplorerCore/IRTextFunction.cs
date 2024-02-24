// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace IRExplorerCore;

public class IRTextFunction : IEquatable<IRTextFunction> {
  private int cachedHashCode_;

  public IRTextFunction(string name) {
    Name = string.Intern(name);
    Sections = new List<IRTextSection>();
  }

  public List<IRTextSection> Sections { get; }
  public int Number { get; set; }
  public string Name { get; }
  public IRTextSummary ParentSummary { get; set; }

  public int MaxBlockCount {
    get {
      IRTextSection maxSection = null;

      foreach (var section in Sections) {
        if (maxSection == null) {
          maxSection = section;
        }
        else if (section.BlockCount > maxSection.BlockCount) {
          maxSection = section;
        }
      }

      return maxSection?.BlockCount ?? 0;
    }
  }

  public int SectionCount => Sections?.Count ?? 0;
  public bool HasSections => SectionCount != 0;

  public void AddSection(IRTextSection section) {
    section.Number = Sections.Count + 1;
    Sections.Add(section);
  }

  public IRTextSection FindSection(string name) {
    return Sections.Find(item => item.Name == name);
  }

  public List<IRTextSection> FindAllSections(string nameSubstring) {
    return Sections.FindAll(section => section.Name.Contains(nameSubstring));
  }

  public bool Equals(IRTextFunction other) {
    if (ReferenceEquals(null, other)) {
      return false;
    }

    if (ReferenceEquals(this, other)) {
      return true;
    }

    return Name.Equals(other.Name, StringComparison.Ordinal);
  }

  public bool Equals(object obj) {
    if (ReferenceEquals(null, obj)) {
      return false;
    }

    if (ReferenceEquals(this, obj)) {
      return true;
    }

    if (obj.GetType() != GetType()) {
      return false;
    }

    return Equals((IRTextFunction)obj, true);
  }

  public override int GetHashCode() {
    // Compute the hash so that functs. with same name in diff. modules
    // don't get the same hash code.
    if (cachedHashCode_ == 0) {
      cachedHashCode_ = ParentSummary != null ? HashCode.Combine(Name, ParentSummary) : HashCode.Combine(Name);
    }

    return cachedHashCode_;
  }

  public override string ToString() {
    return Name;
  }

  private bool HasSameModule(IRTextFunction other) {
    if (ReferenceEquals(ParentSummary, other.ParentSummary)) {
      return true;
    }

    if (ParentSummary != null && other.ParentSummary != null) {
      return ParentSummary.ModuleName.Equals(other.ParentSummary.ModuleName, StringComparison.Ordinal);
    }

    return ParentSummary == null && other.ParentSummary == null;
  }
}