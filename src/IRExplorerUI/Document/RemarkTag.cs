// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System.Collections.Generic;
using System.Text;
using IRExplorerCore.IR;
using IRExplorerCore.Utilities;

namespace IRExplorerUI;

public class RemarkTag : ITag {
  public RemarkTag() {
    Remarks = new List<Remark>();
  }

  public List<Remark> Remarks { get; }
  public string Name => "Remark tag";
  public TaggedObject Owner { get; set; }

  public override string ToString() {
    var builder = new StringBuilder();
    builder.AppendLine($"remarks count: {Remarks.Count}");

    foreach (var remark in Remarks) {
      builder.Append($"  o {remark}".Indent(4));
    }

    return builder.ToString();
  }
}
