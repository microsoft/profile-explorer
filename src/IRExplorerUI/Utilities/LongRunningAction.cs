using System;
using System.Threading.Tasks;
using System.Windows;

namespace IRExplorerUI;

public static class LongRunningAction {
  public static async Task<T> Start<T>(Func<Task<T>> task, TimeSpan timeout,
                                       Action timeoutExceeded,
                                       Action afterTimeoutExceeded) where T:class {
    // Start a timer that triggers the timeoutExceeded action
    // if the timeout is exceeded while the task is running.
    var timeoutAction = DelayedAction.StartNew(timeout, () => {
      timeoutExceeded?.Invoke();
    });

    var result = await task();

    // If the timeout was exceeded and the timeoutExceeded action ran,
    // also execute the afterTimeoutExceeded action.
    if (timeoutAction.Cancel()) {
      afterTimeoutExceeded?.Invoke();
    }

    return result;
  }

  public static async Task Start(Func<Task> task, TimeSpan timeout,
                                 Action timeoutExceeded,
                                 Action afterTimeoutExceeded) {
    // Start a timer that triggers the timeoutExceeded action
    // if the timeout is exceeded while the task is running.
    var timeoutAction = DelayedAction.StartNew(timeout, () => {
      timeoutExceeded?.Invoke();
    });

    await task();

    // If the timeout was exceeded and the timeoutExceeded action ran,
    // also execute the afterTimeoutExceeded action.
    if (timeoutAction.Cancel()) {
      afterTimeoutExceeded?.Invoke();
    }
  }

  public static async Task<T> Start<T>(Func<Task<T>> task, TimeSpan timeout,
                                       string timeoutStatusText,
                                       FrameworkElement timeoutDisabledControl,
                                       ISession session) where T : class {
    return await Start<T>(task, timeout,
      () => {
        timeoutDisabledControl.IsEnabled = false;
        session.SetApplicationProgress(true, double.NaN, timeoutStatusText);
      },
      () => {
        timeoutDisabledControl.IsEnabled = true;
        session.SetApplicationProgress(false, double.NaN);
      });
  }

  public static async Task Start(Func<Task> task, TimeSpan timeout,
                                       string timeoutStatusText,
                                       FrameworkElement timeoutDisabledControl,
                                       ISession session) {
    await Start(task, timeout,
      () => {
        timeoutDisabledControl.IsEnabled = false;
        session.SetApplicationProgress(true, double.NaN, timeoutStatusText);
      },
      () => {
        timeoutDisabledControl.IsEnabled = true;
        session.SetApplicationProgress(false, double.NaN);
      });
  }
}