// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
namespace ProfileExplorer.Core.IR;

public interface ITag {
  string Name { get; }
  TaggedObject Owner { get; set; }
}