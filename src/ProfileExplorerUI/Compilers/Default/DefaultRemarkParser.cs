// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using ProfileExplorer.Core;
using ProfileExplorer.Core.IR;

namespace ProfileExplorer.UI.Compilers.Default;

public class DefaultRemarkParser {
  private IRSectionParser parser_;

  public DefaultRemarkParser(ICompilerInfoProvider compilerInfo) {
    parser_ = compilerInfo.IR.CreateSectionParser(null);
  }

  public static string ExtractValueNumber(IRElement element, string prefix) {
    var tag = element.GetTag<RemarkTag>();

    if (tag == null) {
      return null;
    }

    foreach (var remark in tag.Remarks) {
      if (remark.RemarkText.StartsWith(prefix)) {
        string[] tokens = remark.RemarkText.Split(' ', ':');
        string number = tokens[1];
        return number;
      }
    }

    return null;
  }

  public void Initialize(ReadOnlyMemory<char> line) {
  }

  public TupleIR ParseTuple() {
    return null;
  }

  public OperandIR ParseOperand() {
    return null;
  }
}