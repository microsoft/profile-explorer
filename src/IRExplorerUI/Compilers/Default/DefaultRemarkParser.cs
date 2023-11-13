// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using IRExplorerCore.IR;

namespace IRExplorerUI.Compilers.Default;

public class DefaultRemarkParser {
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
}
