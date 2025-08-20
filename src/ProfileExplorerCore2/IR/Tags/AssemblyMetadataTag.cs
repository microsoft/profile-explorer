// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Collections.Generic;
using System.Text;

namespace ProfileExplorerCore2.IR.Tags;

public sealed class AssemblyMetadataTag : ITag {
  public AssemblyMetadataTag() {
    AddressToElementMap = new Dictionary<long, IRElement>();
    OffsetToElementMap = new Dictionary<long, IRElement>();
    ElementToOffsetMap = new Dictionary<IRElement, long>();
    ElementSizeMap = new Dictionary<IRElement, int>();
  }

  public Dictionary<long, IRElement> AddressToElementMap { get; set; }
  public Dictionary<long, IRElement> OffsetToElementMap { get; set; }
  public Dictionary<IRElement, long> ElementToOffsetMap { get; set; }
  public Dictionary<IRElement, int> ElementSizeMap { get; set; }
  public long FunctionSize { get; set; }
  public string Name => "Address metadata";
  public TaggedObject Owner { get; set; }

  public void EnsureCapacity(int length) {
    AddressToElementMap.EnsureCapacity(length);
    OffsetToElementMap.EnsureCapacity(length);
    ElementToOffsetMap.EnsureCapacity(length);
    ElementSizeMap.EnsureCapacity(length);
  }

  public override string ToString() {
    var builder = new StringBuilder();

    foreach (var pair in OffsetToElementMap) {
      builder.Append($"{pair.Key} = {pair.Value}");
    }

    return builder.ToString();
  }
}