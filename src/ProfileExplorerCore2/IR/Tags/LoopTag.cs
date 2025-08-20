// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using ProfileExplorerCore2.Analysis;

namespace ProfileExplorerCore2.IR.Tags;

public sealed class LoopBlockTag : ITag {
  public LoopBlockTag(Loop parentLoop, int nestingLevel = 0) {
    Loop = parentLoop;
    NestingLevel = nestingLevel;
  }

  public Loop Loop { get; set; }
  public int NestingLevel { get; set; }
  public string Name => "Loop block";
  public TaggedObject Owner { get; set; }
}