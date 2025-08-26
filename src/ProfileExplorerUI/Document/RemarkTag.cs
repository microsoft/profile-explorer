// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Collections.Generic;
using System.Text;
using ProfileExplorerCore.IR;
using ProfileExplorerCore.Utilities;

namespace ProfileExplorer.UI;

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