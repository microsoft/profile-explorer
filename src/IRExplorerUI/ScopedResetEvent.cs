// Copyright(c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading;

namespace IRExplorerUI {
    public partial class MainWindow {
        class ScopedResetEvent : IDisposable {
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
}
