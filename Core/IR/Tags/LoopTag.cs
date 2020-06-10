// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Core.Analysis;

namespace Core.IR {
    public sealed class LoopBlockTag : ITag {
        public string Name { get => "Loop block"; }
        public IRElement Parent { get; set; }
        public Loop Loop { get; set; }
        public int NestingLevel { get; set; }

        public LoopBlockTag(Loop parentLoop, int nestingLevel = 0) {
            Loop = parentLoop;
            NestingLevel = nestingLevel;
        }
    }
}
