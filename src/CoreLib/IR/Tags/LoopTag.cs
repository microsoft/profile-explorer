// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using CoreLib.Analysis;

namespace CoreLib.IR {
    public sealed class LoopBlockTag : ITag {
        public LoopBlockTag(Loop parentLoop, int nestingLevel = 0) {
            Loop = parentLoop;
            NestingLevel = nestingLevel;
        }

        public Loop Loop { get; set; }
        public int NestingLevel { get; set; }
        public string Name => "Loop block";
        public IRElement Parent { get; set; }
    }
}
