// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Threading;

namespace ProfileExplorer.UI;

public class ScopedResetEvent : IDisposable {
  private ManualResetEvent scopedEvent_;

  public ScopedResetEvent(ManualResetEvent scopedEvent) {
    scopedEvent_ = scopedEvent;
    scopedEvent_.Reset();
  }

  public void Dispose() {
    scopedEvent_.Set();
  }
}