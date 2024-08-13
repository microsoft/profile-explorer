// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.ConstrainedExecution;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ProfileExplorer.UI.Profile;

// Copied from ClrMD.
class WindowsThreadSuspender : CriticalFinalizerObject, IDisposable {
  private readonly object _sync = new();
  private readonly int _pid;
  private volatile int[] _suspendedThreads;

  public WindowsThreadSuspender(int pid) {
    _pid = pid;
    _suspendedThreads = SuspendThreads();
  }

  ~WindowsThreadSuspender() {
    Dispose(false);
  }

  public void Dispose() {
    Dispose(true);
    GC.SuppressFinalize(this);
  }

  private int[] SuspendThreads() {
    bool permissionFailure = false;
    var suspendedThreads = new HashSet<int>();

    // A thread may create more threads while we are in the process of walking the list.  We will keep looping through
    // the thread list over and over until we find that we haven't found any new threads to suspend.
    try {
      int originalCount;

      do {
        originalCount = suspendedThreads.Count;

        Process process;

        try {
          process = Process.GetProcessById(_pid);
        }
        catch (ArgumentException e) {
          throw new InvalidOperationException($"Unable to inspect process {_pid:x}.", e);
        }

        foreach (ProcessThread thread in process.Threads) {
          if (thread != null) {
            if (suspendedThreads.Contains(thread.Id))
              continue;

            using var threadHandle = Interop.OpenThread(Interop.ThreadAccess.SUSPEND_RESUME, false, (uint)thread.Id);

            if (threadHandle.IsInvalid || Interop.SuspendThread(threadHandle.DangerousGetHandle()) == -1) {
              permissionFailure = true;
              continue;
            }

            suspendedThreads.Add(thread.Id);
          }
        }
      } while (originalCount != suspendedThreads.Count);

      // If we fail to suspend any thread then we didn't have permission.  We'll throw an exception in that case.  If
      // we fail to suspend a few of the threads we'll treat that as non-fatal.
      if (permissionFailure && suspendedThreads.Count == 0)
        throw new InvalidOperationException($"Unable to suspend threads of process {_pid:x}.");

      int[] result = suspendedThreads.ToArray();
      suspendedThreads = null;
      return result;
    }
    finally {
      if (suspendedThreads != null)
        ResumeThreads(suspendedThreads);
    }
  }

  private void ResumeThreads(IEnumerable<int> suspendedThreads) {
    foreach (int threadId in suspendedThreads) {
      using var threadHandle = Interop.OpenThread(Interop.ThreadAccess.SUSPEND_RESUME, false, (uint)threadId);

      if (threadHandle.IsInvalid || Interop.ResumeThread(threadHandle.DangerousGetHandle()) == -1) {
        // If we fail to resume a thread we are in a bit of trouble because the target process is likely in a bad
        // state.  This shouldn't ever happen, but if it does there's nothing we can do about it.  We'll log an event
        // here but we won't throw an exception for a few reasons:
        //     1.  We really never expect this to happen.  Why would we be able to suspend a thread but not resume it?
        //     2.  We want to finish resuming threads.
        //     3.  There's nothing the caller can really do about it.

        Trace.WriteLine($"Failed to resume thread id:{threadId:id} in pid:{_pid:x}.");
      }
    }
  }

  private void Dispose(bool _) {
    lock (_sync) {
      if (_suspendedThreads != null) {
        int[] suspendedThreads = _suspendedThreads;
        _suspendedThreads = null;
        ResumeThreads(suspendedThreads);
      }
    }
  }

  public static class Interop {
    [DllImport(Kernel32LibraryName)]
    private static extern bool GetThreadContext(IntPtr hThread, IntPtr lpContext);

    [DllImport(Kernel32LibraryName, SetLastError = true)]
    internal static extern SafeWin32Handle OpenThread(ThreadAccess dwDesiredAccess,
                                                      [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle,
                                                      uint dwThreadId);

    [DllImport(Kernel32LibraryName, SetLastError = true)]
    internal static extern int SuspendThread(IntPtr hThread);

    [DllImport(Kernel32LibraryName, SetLastError = true)]
    internal static extern int ResumeThread(IntPtr hThread);

    private const string Kernel32LibraryName = "kernel32.dll";

    public enum ThreadAccess {
      SUSPEND_RESUME = 0x0002,
      THREAD_ALL_ACCESS = 0x1F03FF
    }

    public sealed class SafeWin32Handle : SafeHandleZeroOrMinusOneIsInvalid {
      [DllImport("kernel32.dll", SetLastError = true, PreserveSig = true)]
      [return: MarshalAs(UnmanagedType.Bool)]
      public static extern bool CloseHandle(IntPtr handle);

      public SafeWin32Handle() : base(true) {
      }

      public SafeWin32Handle(IntPtr handle)
        : this(handle, true) {
      }

      public SafeWin32Handle(IntPtr handle, bool ownsHandle)
        : base(ownsHandle) {
        SetHandle(handle);
      }

      protected override bool ReleaseHandle() {
        return CloseHandle(handle);
      }
    }
  }
}