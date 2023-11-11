// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Threading;

namespace IRExplorerUI {
    public class ScopedResetEvent : IDisposable {
        private ManualResetEvent scopedEvent_;

        public ScopedResetEvent(ManualResetEvent scopedEvent) {
            scopedEvent_ = scopedEvent;
            scopedEvent_.Reset();
        }

        public void Dispose() {
            scopedEvent_.Set();
        }
    }
}
