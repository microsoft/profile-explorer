// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.Threading;

namespace IRExplorerCore {
    public class CancelableTaskInfo : IDisposable {
        private bool canceled = false;
        private CancellationToken cancelToken_;

        private bool competed = false;
        private bool disposed_;
        public ManualResetEvent TaskCompletedEvent;
        private CancellationTokenSource tokenSource_;

        public CancelableTaskInfo() {
            TaskCompletedEvent = new ManualResetEvent(false);
            tokenSource_ = new CancellationTokenSource();
            cancelToken_ = tokenSource_.Token;

            //Debug.WriteLine($"+ Create task {ObjectTracker.Track(this)}");
            //Debug.WriteLine($"{Environment.StackTrace}\n-------------------------------------------\n");
        }

        public bool IsCanceled => cancelToken_.IsCancellationRequested;

        public void Dispose() {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~CancelableTaskInfo() {
            Dispose(false);
        }

        public void WaitToComplete() {
            //Debug.WriteLine($"+ Wait to complete task {ObjectTracker.Track(this)}");
            //Debug.WriteLine($"{Environment.StackTrace}\n-------------------------------------------\n");
            Debug.Assert(!disposed_);
            TaskCompletedEvent.WaitOne();
        }

        public void WaitToComplete(TimeSpan timeout) {
            Debug.Assert(!disposed_);
            TaskCompletedEvent.WaitOne(timeout);
        }

        public void Completed() {
            //Debug.WriteLine($"+ Complete task {ObjectTracker.Track(this)}");
            //Debug.WriteLine($"{Environment.StackTrace}\n-------------------------------------------\n");
            Debug.Assert(!disposed_);
            TaskCompletedEvent.Set();
        }

        public void Cancel() {
            //Debug.WriteLine($"+ Cancel task {ObjectTracker.Track(this)}");
            //Debug.WriteLine($"{Environment.StackTrace}\n-------------------------------------------\n");
            Debug.Assert(!disposed_);
            Debug.Assert(!IsCanceled);
            tokenSource_.Cancel();
        }

        protected virtual void Dispose(bool disposing) {
            if (!disposed_) {
                tokenSource_.Dispose();
                TaskCompletedEvent.Dispose();
                disposed_ = true;
            }
        }
    }
}
