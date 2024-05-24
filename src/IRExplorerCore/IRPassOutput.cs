// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;

namespace IRExplorerCore;

public class IRPassOutput {
  public static readonly IRPassOutput Empty = new IRPassOutput(0, 0, 0, 0);

  public IRPassOutput(long dataStartOffset, long dataEndOffset, int startLine,
                      int endLine) {
    DataStartOffset = dataStartOffset;
    DataEndOffset = dataEndOffset;
    StartLine = startLine;
    EndLine = endLine;
  }

  public long DataStartOffset { get; set; }
  public long DataEndOffset { get; set; } // One past end.
  public long Size => DataEndOffset - DataStartOffset;
  public byte[] Signature { get; set; } // SHA256 signature of the text.
  public int StartLine { get; set; }
  public int EndLine { get; set; }
  public int LineCount => EndLine - StartLine + 1;
  public bool HasPreprocessedLines { get; set; }

  public override bool Equals(object obj) {
    return obj is IRPassOutput output &&
           DataStartOffset == output.DataStartOffset &&
           DataEndOffset == output.DataEndOffset &&
           StartLine == output.StartLine &&
           EndLine == output.EndLine;
  }

  public override int GetHashCode() {
    return HashCode.Combine(DataStartOffset, DataEndOffset);
  }
}
