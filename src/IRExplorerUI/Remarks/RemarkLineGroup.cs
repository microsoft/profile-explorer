// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using IRExplorerCore;

namespace IRExplorerUI {
    public class RemarkLineGroup {
        public RemarkLineGroup(int line) {
            LineNumber = line;
            Remarks = new List<Remark>();
        }

        public RemarkLineGroup(int line, Remark remark) : this(line) {
            Add(remark, null);
        }

        public int LineNumber { get; set; }
        public List<Remark> Remarks { get; set; }
        public Remark LeaderRemark { get; set; }

        public void Add(Remark remark, IRTextSection currentSection) {
            // Don't add multiple remarks referencing the same output text location,
            // can happen when both an instruction and its operands are marked.
            if (Remarks.Find((item) => item.RemarkLocation == remark.RemarkLocation) != null) {
                return;
            }

            Remarks.Add(remark);

            if (LeaderRemark == null || remark.Priority < LeaderRemark.Priority) {
                LeaderRemark = remark;
            }
            else if (remark.Priority == LeaderRemark.Priority &&
                     remark.Section == currentSection) {
                LeaderRemark = remark;
            }
        }
    }
}
