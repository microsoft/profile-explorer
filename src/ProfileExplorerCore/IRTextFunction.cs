// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;

namespace ProfileExplorer.Core;

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
  public string ModuleName => ParentSummary?.ModuleName;

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

    // Because name is interned, we can use reference equality.
    return ReferenceEquals(Name, other.Name) &&
           HasSameModule(other);
  }

  public override bool Equals(object obj) {
    if (ReferenceEquals(null, obj)) {
      return false;
    }

    if (ReferenceEquals(this, obj)) {
      return true;
    }

    if (obj.GetType() != GetType()) {
      return false;
    }

    return Equals((IRTextFunction)obj);
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
      return ParentSummary.Equals(other.ParentSummary);
    }

    return ParentSummary == null && other.ParentSummary == null;
  }
}