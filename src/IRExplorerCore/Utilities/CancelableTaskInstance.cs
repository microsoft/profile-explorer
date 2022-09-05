using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace IRExplorerCore {
    public class CancelableTaskInstance : IDisposable {
        private object lockObject_ = new object();
        private CancelableTask taskInstance_;
        private bool completeOnCancel_;

        public CancelableTaskInstance(bool completeOnCancel = true) {
            completeOnCancel_ = completeOnCancel;
        }

        public delegate void CancelableTaskDelegate(CancelableTask task);

        public CancelableTask CreateTask(CancelableTaskDelegate registerAction = null) {
            lock (lockObject_) {
                if (taskInstance_ != null) {
                    // Cancel running task without blocking.
                    CancelTask();
                }

                taskInstance_ = new CancelableTask(completeOnCancel_);
                registerAction?.Invoke(taskInstance_);
                return taskInstance_;
            }
        }

        public async Task<CancelableTask> CancelPreviousAndCreateTaskAsync(CancelableTaskDelegate registerAction = null) {
            CancelableTask task = null;

            lock (lockObject_) {
                task = taskInstance_;
                taskInstance_ = null;
            }

            if (task != null) {
                await CancelTaskAsync(task);
            }

            lock (lockObject_) {
                taskInstance_ = new CancelableTask(completeOnCancel_);
                registerAction?.Invoke(taskInstance_);
                return taskInstance_;
            }
        }

        public void CancelTask(CancelableTaskDelegate unregisterAction = null) {
            lock (lockObject_) {
                if (taskInstance_ == null) {
                    return;
                }

                var canceledTask = taskInstance_;
                taskInstance_ = null;

                // Cancel the task and wait for it to complete without blocking.
                canceledTask.Cancel();
                unregisterAction?.Invoke(canceledTask);

                Task.Run(() => {
                    canceledTask.WaitToComplete();
                    canceledTask.Dispose();
                });
            }
        }

        private async Task CancelTaskAsync(CancelableTask canceledTask, CancelableTaskDelegate unregisterAction = null) {
            // Cancel the task and wait for it to complete.
            canceledTask.Cancel();
            unregisterAction?.Invoke(canceledTask);

            await Task.Run(async () => {
                await canceledTask.WaitToCompleteAsync();
                canceledTask.Dispose();
            });
        }

        public void CompleteTask(CancelableTask task, CancelableTaskDelegate unregisterAction = null) {
            lock (lockObject_) {
                if (task != taskInstance_) {
                    return; // A canceled task, ignore it.
                }

                if (taskInstance_ != null) {
                    unregisterAction?.Invoke(taskInstance_);
                    taskInstance_.Completed();
                    taskInstance_.Dispose();
                    taskInstance_ = null;
                }
            }
        }

        public void WaitForTask() {
            lock (lockObject_) {
                taskInstance_?.WaitToComplete();
            }
        }

        public async Task WaitForTaskAsync() {
            CancelableTask task = null;

            lock (lockObject_) {
                task = taskInstance_;
            }

            if (task != null) {
                await task.WaitToCompleteAsync();
            }
        }

        public void Dispose() {
            taskInstance_?.Dispose();
        }
    }
}
