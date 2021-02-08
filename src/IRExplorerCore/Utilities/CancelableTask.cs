// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace IRExplorerCore {
    public static class WaitHandleExtensions {
        public static Task AsTask(this WaitHandle handle) {
            return AsTask(handle, Timeout.InfiniteTimeSpan);
        }

        public static Task AsTask(this WaitHandle handle, TimeSpan timeout) {
            var tcs = new TaskCompletionSource<object>();
            var registration = ThreadPool.RegisterWaitForSingleObject(handle, (state, timedOut) => {
                var localTcs = (TaskCompletionSource<object>)state;
                if (timedOut)
                    localTcs.TrySetCanceled();
                else
                    localTcs.TrySetResult(null);
            }, tcs, timeout, executeOnlyOnce: true);
            tcs.Task.ContinueWith((_, state) => ((RegisteredWaitHandle)state).Unregister(null), registration, TaskScheduler.Default);
            return tcs.Task;
        }
    }

    public class CancelableTask : IDisposable {
        private CancellationToken cancelToken_;
        private ManualResetEvent taskCompletedEvent_;
        private CancellationTokenSource tokenSource_;
        private bool disposed_;

        public CancelableTask() {
            taskCompletedEvent_ = new ManualResetEvent(false);
            tokenSource_ = new CancellationTokenSource();
            cancelToken_ = tokenSource_.Token;

            //Debug.WriteLine($"+ Create task {ObjectTracker.Track(this)}");
            //Debug.WriteLine($"{Environment.StackTrace}\n-------------------------------------------\n");
        }

        public CancellationToken Token => cancelToken_;
        public bool IsCanceled => cancelToken_.IsCancellationRequested;

        public void Dispose() {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~CancelableTask() {
            Dispose(false);
        }

        public void WaitToComplete() {
            //Debug.WriteLine($"+ Wait to complete task {ObjectTracker.Track(this)}");
            //Debug.WriteLine($"{Environment.StackTrace}\n-------------------------------------------\n");
            if (disposed_) {
                return;
            }

            taskCompletedEvent_.WaitOne();
        }

        public async Task WaitToCompleteAsync() {
            //Debug.WriteLine($"+ Wait to complete task {ObjectTracker.Track(this)}");
            //Debug.WriteLine($"{Environment.StackTrace}\n-------------------------------------------\n");
            if (disposed_) {
                return;
            }

            await taskCompletedEvent_.AsTask();
        }

        public void WaitToComplete(TimeSpan timeout) {
            if (disposed_) {
                return;
            }

            taskCompletedEvent_.WaitOne(timeout);
        }

        public async Task WaitToCompleteAsync(TimeSpan timeout) {
            //Debug.WriteLine($"+ Wait to complete task {ObjectTracker.Track(this)}");
            //Debug.WriteLine($"{Environment.StackTrace}\n-------------------------------------------\n");
            if (disposed_) {
                return;
            }

            await taskCompletedEvent_.AsTask(timeout);
        }

        public void Completed() {
            //Debug.WriteLine($"+ Complete task {ObjectTracker.Track(this)}");
            //Debug.WriteLine($"{Environment.StackTrace}\n-------------------------------------------\n");
            if (disposed_) {
                return;
            }

            taskCompletedEvent_.Set();
        }

        public void Cancel() {
            //Debug.WriteLine($"+ Cancel task {ObjectTracker.Track(this)}");
            //Debug.WriteLine($"{Environment.StackTrace}\n-------------------------------------------\n");
            if (disposed_) {
                return;
            }

            tokenSource_.Cancel();
            taskCompletedEvent_.Set();
        }

        protected virtual void Dispose(bool disposing) {
            if (!disposed_) {
                tokenSource_.Dispose();
                taskCompletedEvent_.Dispose();
                disposed_ = true;
            }
        }
    }
}
