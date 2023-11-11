// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Threading.Tasks;

namespace IRExplorerUI {
    public class DelayedAction {
        private bool canceled_;

        public static TimeSpan DefaultDelay = TimeSpan.FromMilliseconds(500);
        
        public async Task Start(TimeSpan delay, Action action) {
            canceled_ = false;
            await Task.Delay(delay);

            if (!canceled_) {
                action();
            }
        }

        public void Cancel() {
            canceled_ = true;
        }

        public static DelayedAction StartNew(Action action) {
            return StartNew(DefaultDelay, action);
        }

        public static DelayedAction StartNew(TimeSpan delay, Action action) {
            var instance = new DelayedAction();
            instance.Start(delay, action);
            return instance;
        }
    }
}
