// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System.Collections.Generic;
using IRExplorerCore.IR;

namespace IRExplorerUI;

public sealed class HighlightedElementGroup {
  public HighlightedElementGroup(HighlightingStyle style) {
    Elements = new List<IRElement>();
    Style = style;
  }

  public HighlightedElementGroup(IRElement element, HighlightingStyle style) : this(style) {
    Add(element);
  }

  public List<IRElement> Elements { get; set; }
  public HighlightingStyle Style { get; set; }

  public bool IsEmpty() {
    return Elements.Count == 0;
  }

  public void Add(IRElement element) {
    Elements.Add(element);
  }

  public void AddRange(IEnumerable<IRElement> elements) {
    Elements.AddRange(elements);
  }

  public void AddFront(IRElement element) {
    Elements.Insert(0, element);
  }

  public bool Contains(IRElement element) {
    return Elements.Contains(element);
  }

  public bool Remove(IRElement element) {
    return Elements.Remove(element);
  }
}