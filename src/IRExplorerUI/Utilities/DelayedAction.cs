// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;

namespace IRExplorerUI {
    public class DelayedAction {
        private bool canceled_;

        public async void Start(TimeSpan delay, Action action) {
            canceled_ = false;
            await Task.Delay(delay);

            if (!canceled_) {
                action();
            }
        }

        public void Cancel() {
            canceled_ = true;
        }

        public static DelayedAction StartNew(TimeSpan delay, Action action) {
            var instance = new DelayedAction();
            instance.Start(delay, action);
            return instance;
        }
    }
}
