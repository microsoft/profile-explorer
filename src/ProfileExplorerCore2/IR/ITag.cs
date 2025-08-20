// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
namespace ProfileExplorerCore2.IR;

public interface ITag {
  string Name { get; }
  TaggedObject Owner { get; set; }
}