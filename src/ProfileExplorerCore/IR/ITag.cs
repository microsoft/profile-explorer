// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
namespace ProfileExplorer.Core.IR;

public interface ITag {
  string Name { get; }
  TaggedObject Owner { get; set; }
}