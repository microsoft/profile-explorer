// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace ProfileExplorerExtension;

static class ClientInstance {
  public static Channel serverChannel_;
  public static DebugService.DebugServiceClient debugClient_;
  public static long debugSessionId_;
  public static bool debugSesssionInitialized_;
  public static AsyncPackage Package;
  public static bool isEnabled_;
  public static bool hadForcedShutdown_;
  private static readonly string DefaultProfileExplorerPath =
    @"C:\Program Files (x86)\Profile Explorer\ProfileExplorer.exe";

  static ClientInstance() {
    isEnabled_ = true;
  }

  public static bool IsEnabled => isEnabled_;
  public static bool AutoAttach { get; set; }
  public static bool IsConnected => debugSesssionInitialized_;

  public static bool ToggleEnabled() {
    isEnabled_ = !isEnabled_;
    Logger.Log($"Profile Explorer extension enabled: {isEnabled_}");
    return isEnabled_;
  }

  public static void Initialize(AsyncPackage package) {
    Package = package;
  }

  public static void ResetForcedShutdown() {
    hadForcedShutdown_ = false;
  }

  public static bool IsServerStarted() {
    try {
      CreateServerChannel();
      var timeout = DateTime.UtcNow.AddMilliseconds(100);

      return ThreadHelper.JoinableTaskFactory.Run(async () => {
        await serverChannel_.ConnectAsync(timeout);
        return true;
      });
    }
    catch (Exception ex) {
      //Logger.LogException(ex, "ClientInstance exception");
      return false;
    }
  }

  public static async Task<bool> WaitForDebugServer(DateTime timeout) {
    try {
      if (serverChannel_.State != ChannelState.Ready) {
        bool result = await serverChannel_.TryWaitForStateChangedAsync(
          serverChannel_.State, timeout);

        return result && serverChannel_.State == ChannelState.Ready ||
               serverChannel_.State == ChannelState.Idle;
      }

      return true;
    }
    catch (Exception ex) {
      Logger.LogException(ex, "ClientInstance exception");
      return false;
    }
  }

  public static DebugService.DebugServiceClient GetDebugClient(
    string processName, int processId) {
    try {
      if (serverChannel_ == null) {
        serverChannel_ =
          new Channel("127.0.0.1:50051", ChannelCredentials.Insecure);
      }

      if (debugClient_ == null) {
        Logger.Log("Connecting to Profile Explorer...");
        debugClient_ = new DebugService.DebugServiceClient(serverChannel_);

        var result = debugClient_.StartSession(new StartSessionRequest {
          Kind = ClientKind.Debugger,
          ProcessId = processId
        });

        debugSessionId_ = result.SessionId;
        Logger.Log("Connected to Profile Explorer instance");
      }

      return debugClient_;
    }
    catch (Exception ex) {
      Logger.LogException(ex, "Failed to connect to Profile Explorer");
      return null;
    }
  }

  public static async Task Shutdown(bool forced = false) {
    Logger.Log($"Shutdown force = {forced}");

    try {
      debugClient_?.EndSession(new EndSessionRequest());
    }
    catch (Exception ex) {
      //Logger.LogException(ex, "ClientInstance exception");
    }

    try {
      await serverChannel_?.ShutdownAsync();
    }
    catch (Exception ex) {
      //Logger.LogException(ex, "ClientInstance exception");
    }

    debugSesssionInitialized_ = false;
    serverChannel_ = null;
    debugClient_ = null;
    debugSessionId_ = 0;
    hadForcedShutdown_ = forced;
    AutoAttach = false;
  }

  public static async Task<bool> RunClientCommand(Action action) {
    bool retry = true;
    int retryCount = 0;

    while (retry && retryCount < 2) {
      if (!await SetupDebugSession()) {
        return false;
      }

      retry = false;

      try {
        action();
        return true;
      }
      catch (RpcException rpcEx) {
        Logger.LogException(rpcEx, "ClientInstance RPC exception");

        switch (rpcEx.StatusCode) {
          case StatusCode.Unavailable:
          case StatusCode.DeadlineExceeded:
          case StatusCode.Aborted: {
            await Shutdown();
            retry = true;
            retryCount++;
            break;
          }
        }
      }
      catch (Exception ex) {
        Logger.LogException(ex, "ClientInstance exception");
        break;
      }
    }

    await Shutdown(true);
    return false;
  }

  public static async Task UpdateIR() {
    await RunClientCommand(() => { DebuggerInstance.UpdateIR(); });
  }

  public static async Task UpdateCurrentStackFrame() {
    await RunClientCommand(() => {
      debugClient_.UpdateCurrentStackFrame(new CurrentStackFrameRequest {
        CurrentFrame = DebuggerInstance.GetCurrentStackFrame()
      });
    });
  }

  public static async Task ClearTemporaryHighlighting() {
    await RunClientCommand(() => debugClient_.ClearTemporaryHighlighting(
                             new ClearHighlightingRequest {
                               Highlighting = HighlightingType.Temporary
                             }));
    ;
  }

  public static async Task<bool> SetupDebugSession() {
    if (debugSesssionInitialized_) {
      return true;
    }

    if (hadForcedShutdown_) {
      return false;
    }

    if (DebuggerInstance.InBreakMode) {
      //var stackFrame = debugger_.CurrentStackFrame as EnvDTE90a.StackFrame2;
      //var function = stackFrame.FunctionName;
      //var lineNumber = stackFrame.LineNumber;
      //var file = stackFrame.FileName;
      if (!await StartProfileExplorerAsync()) {
        return false;
      }

      var client =
        GetDebugClient(DebuggerInstance.ProcessName, DebuggerInstance.ProcessId);

      if (client == null) {
        await Shutdown(true);
        return false;
      }

      debugSesssionInitialized_ = true;
      return true;
    }

    return false;
  }

  internal static async Task PauseCurrentElementHandling() {
    await RunClientCommand(() => {
      debugClient_.SetSessionState(new SessionStateRequest {
        State = SessionState.Paused
      });
    });
  }

  internal static async Task ResumeCurrentElementHandling() {
    await RunClientCommand(() => {
      debugClient_.SetSessionState(new SessionStateRequest {
        State = SessionState.Listening
      });
    });
  }

  private static void CreateServerChannel() {
    if (serverChannel_ == null) {
      serverChannel_ = new Channel("127.0.0.1:50051", ChannelCredentials.Insecure);
    }
  }

  private static string GetProfileExplorerPath() {
#if false
            return @"C:\personal\projects\compiler_studio\Client\bin\Debug\net5.0\ProfileExplorer.exe";
#endif
    string path = NativeMethods.GetFullPathFromWindows("ProfileExplorer.exe");

    if (File.Exists(path)) {
      return path;
    }

    return File.Exists(DefaultProfileExplorerPath) ? DefaultProfileExplorerPath : null;
  }

  private static async Task<bool> StartProfileExplorerAsync() {
    if (IsServerStarted()) {
      return true;
    }

    try {
      string args = "-grpc-server";
      string path = GetProfileExplorerPath();
      ProfileExplorerExtensionPackage.SetStatusBar("Starting Profile Explorer...", true);
      Logger.Log($"Starting Profile Explorer: {path}");

      if (path == null) {
        VsShellUtilities.ShowMessageBox(
          Package,
          "Could not find Profile Explorer, make sure it is properly installed and found on PATH",
          "Profile Explorer extension", OLEMSGICON.OLEMSGICON_WARNING,
          OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);

        Logger.LogError("ProfileExplorer.exe not found");
        ProfileExplorerExtensionPackage.SetStatusBar("Could not find Profile Explorer");
        return false;
      }

      var psi = new ProcessStartInfo(path, args);
      var process = Process.Start(psi);
      bool result = process != null &&
                    await Task.Run(() => process.WaitForInputIdle(30000));

      if (result) {
        Logger.Log("Started Profile Explorer instance");
        ProfileExplorerExtensionPackage.SetStatusBar("Profile Explorer started");
      }
      else {
        Logger.LogError("Failed to start Profile Explorer instance");
        ProfileExplorerExtensionPackage.SetStatusBar("Failed to start Profile Explorer");
      }

      return result;
    }
    catch (Exception ex) {
      VsShellUtilities.ShowMessageBox(
        Package, $"Failed to start Profile Explorer, exception {ex.Message}",
        "Profile Explorer extension", OLEMSGICON.OLEMSGICON_WARNING,
        OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);

      Logger.LogException(ex, "Failed to start Profile Explorer");
      return false;
    }
  }
}