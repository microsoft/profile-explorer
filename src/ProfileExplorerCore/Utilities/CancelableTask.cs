// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ProfileExplorer.Core;

public static class WaitHandleExtensions {
  public static Task AsTask(this WaitHandle handle) {
    return AsTask(handle, Timeout.InfiniteTimeSpan);
  }

  public static Task AsTask(this WaitHandle handle, TimeSpan timeout) {
    var taskSource = new TaskCompletionSource<object>();
    var registration = ThreadPool.RegisterWaitForSingleObject(handle, (state, timedOut) => {
      var localTaskSource = (TaskCompletionSource<object>)state;
      if (timedOut)
        localTaskSource.TrySetCanceled();
      else
        localTaskSource.TrySetResult(null);
    }, taskSource, timeout, true);

    taskSource.Task.ContinueWith((_, state) => ((RegisteredWaitHandle)state).Unregister(null), registration,
                                 TaskScheduler.Default);
    return taskSource.Task;
  }
}

public class CancelableTask : IDisposable {
  private CancellationToken cancelToken_;
  private ManualResetEvent taskCompletedEvent_;
  private CancellationTokenSource tokenSource_;
  private bool disposed_;
  private bool completeOnCancel_;

  public CancelableTask(bool completeOnCancel = true) {
    taskCompletedEvent_ = new ManualResetEvent(false);
    tokenSource_ = new CancellationTokenSource();
    cancelToken_ = tokenSource_.Token;
    completeOnCancel_ = completeOnCancel;

    //Debug.WriteLine($"+ Create task {ObjectTracker.Track(this)}");
    //Debug.WriteLine($"{Environment.StackTrace}\n-------------------------------------------\n");
  }

  public CancellationToken Token => cancelToken_;
  public bool IsCanceled => cancelToken_.IsCancellationRequested;
  public bool IsCompleted => !IsCanceled && taskCompletedEvent_.WaitOne(0);

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

  public async Task<bool> WaitToCompleteAsync() {
    //Debug.WriteLine($"+ Wait to complete task {ObjectTracker.Track(this)}");
    //Debug.WriteLine($"{Environment.StackTrace}\n-------------------------------------------\n");
    return await WaitToCompleteAsync(TimeSpan.FromMilliseconds(int.MaxValue - 1));
  }

  public bool WaitToComplete(TimeSpan timeout) {
    if (disposed_) {
      return false;
    }

    return taskCompletedEvent_.WaitOne(timeout);
  }

  private async Task<bool> WaitToCompleteAsync(TimeSpan timeout) {
    //Debug.WriteLine($"+ Wait to complete task {ObjectTracker.Track(this)}");
    //Debug.WriteLine($"{Environment.StackTrace}\n-------------------------------------------\n");
    if (disposed_) {
      return false;
    }

    try {
      await taskCompletedEvent_.AsTask(timeout);
      return true;
    }
    catch (TaskCanceledException ex) {
      // Triggered when timing out.
      return false;
    }
  }

  public void Complete() {
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

    if (completeOnCancel_) {
      taskCompletedEvent_.Set();
    }
  }

  private void Dispose(bool disposing) {
    if (!disposed_) {
      taskCompletedEvent_.Set(); // May not be marked as completed yet.
      tokenSource_.Dispose();
      taskCompletedEvent_.Dispose();
      disposed_ = true;
    }
  }
}