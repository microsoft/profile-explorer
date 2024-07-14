// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Threading.Tasks;

namespace IRExplorerCore;

// Manages a unique running instance of a CancelableTask.
public class CancelableTaskInstance : IDisposable {
  private CancelableTask taskInstance_;
  private CancelableTaskDelegate registerAction_;
  private CancelableTaskDelegate unregisterAction_;
  private object lockObject_ = new();
  private bool completeOnCancel_;

  public CancelableTaskInstance(bool completeOnCancel = true,
                                CancelableTaskDelegate registerAction = null,
                                CancelableTaskDelegate unregisterAction = null) {
    completeOnCancel_ = completeOnCancel;
    registerAction_ = registerAction;
    unregisterAction_ = unregisterAction;
  }

  public delegate void CancelableTaskDelegate(CancelableTask task);

  public CancelableTask CurrentInstance {
    get
    {
      lock (lockObject_) {
        if (taskInstance_ == null) {
          throw new InvalidOperationException("No task instance running!");
        }

        return taskInstance_;
      }
    }
  }

  public CancelableTask CreateTask() {
    lock (lockObject_) {
      if (taskInstance_ != null) {
        // Cancel running task without blocking.
        CancelTask();
      }

      taskInstance_ = new CancelableTask(completeOnCancel_);
      registerAction_?.Invoke(taskInstance_);
      return taskInstance_;
    }
  }

  public async Task<CancelableTask> CancelCurrentAndCreateTaskAsync() {
    CancelableTask task = null;

    lock (lockObject_) {
      task = taskInstance_;
    }

    if (task != null) {
      await CancelTaskAndWaitAsync(task);
    }

    lock (lockObject_) {
      // Check that nothing else changed the current task
      // before replacing it.
      if (taskInstance_ == task) {
        taskInstance_ = new CancelableTask(completeOnCancel_);
        registerAction_?.Invoke(taskInstance_);
      }
      
      return taskInstance_;
    }
  }
  
  public async Task<CancelableTask> WaitAndCreateTaskAsync() {
    CancelableTask task = null;

    lock (lockObject_) {
      task = taskInstance_;
    }

    if (task != null) {
      await task.WaitToCompleteAsync();
    }

    lock (lockObject_) {
      // Check that nothing else changed the current task
      // before replacing it.
      if (taskInstance_ == task) {
        taskInstance_ = new CancelableTask(completeOnCancel_);
        registerAction_?.Invoke(taskInstance_);
      }

      return taskInstance_;
    }
  }

  public void CancelTask() {
    lock (lockObject_) {
      if (taskInstance_ == null) {
        return;
      }

      var canceledTask = taskInstance_;
      taskInstance_ = null;

      // Cancel the task and wait for it to complete without blocking.
      canceledTask.Cancel();
      unregisterAction_?.Invoke(canceledTask);
    }
  }

  public void CancelTaskAndWait() {
    lock (lockObject_) {
      if (taskInstance_ == null) {
        return;
      }

      var canceledTask = taskInstance_;
      taskInstance_ = null;

      // Cancel the task and wait for it to complete without blocking.
      canceledTask.Cancel();
      canceledTask.WaitToComplete();
      unregisterAction_?.Invoke(canceledTask);
    }
  }

  public async Task CancelTaskAndWaitAsync() {
    CancelableTask task = null;

    lock (lockObject_) {
      task = taskInstance_;
      taskInstance_ = null;
    }

    if (task != null) {
      await CancelTaskAndWaitAsync(task);
    }
  }

  public void CompleteTask(CancelableTask task) {
    lock (lockObject_) {
      if (task != taskInstance_) {
        return; // A canceled task, ignore it.
      }

      if (taskInstance_ != null) {
        unregisterAction_?.Invoke(taskInstance_);
        taskInstance_.Complete();
        taskInstance_.Dispose();
        taskInstance_ = null;
      }
    }
  }

  public void CompleteTask() {
    CancelableTask task = null;

    lock (lockObject_) {
      task = taskInstance_;
    }

    if (task != null) {
      CompleteTask(task);
    }
  }

  public void WaitForTask() {
    CancelableTask task = null;

    lock (lockObject_) {
      task = taskInstance_;
    }

    if (task != null) {
      task.WaitToComplete();
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

  private async Task CancelTaskAndWaitAsync(CancelableTask canceledTask) {
    // Cancel the task and wait for it to complete.
    canceledTask.Cancel();
    unregisterAction_?.Invoke(canceledTask);
    await canceledTask.WaitToCompleteAsync();
  }
}