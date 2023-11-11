﻿// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Text;
using IRExplorerCore.Analysis;
using IRExplorerCore.IR;

namespace IRExplorerCore.UTC {
    class UTCReferenceFilter : CFGReachabilityReferenceFilter {
        public UTCReferenceFilter(FunctionIR function) : base(function) {
            
        }
    }
}
