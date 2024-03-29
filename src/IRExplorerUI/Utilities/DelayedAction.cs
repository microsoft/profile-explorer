// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Threading.Tasks;
using System.Windows;

namespace IRExplorerUI;

public class DelayedAction {
  public static TimeSpan DefaultDelay = TimeSpan.FromMilliseconds(500);
  private bool canceled_;
  private bool timedOut_;

  public static DelayedAction StartNew(Action action) {
    return StartNew(DefaultDelay, action);
  }

  public static DelayedAction StartNew(TimeSpan delay, Action action) {
    var instance = new DelayedAction();
    instance.Start(delay, action);
    return instance;
  }

  public async Task Start(TimeSpan delay, Action action) {
    canceled_ = false;
    timedOut_ = false;

    await Task.Delay(delay);

    if (!canceled_) {
      timedOut_ = true;
      action();
    }
  }

  public bool Cancel() {
    canceled_ = true;
    return timedOut_;
  }
}