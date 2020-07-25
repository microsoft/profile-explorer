// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace IRExplorerExtension {
    internal static class ClientInstance {
        private static readonly string DefaultIRExplorerPath =
            @"C:\Program Files (x86)\IR Explorer\irexplorer.exe";

        public static Channel serverChannel_;
        public static DebugService.DebugServiceClient debugClient_;
        public static long debugSessionId_;
        public static bool debugSesssionInitialized_;
        public static AsyncPackage Package;
        public static bool isEnabled_;
        public static bool hadForcedShutdown_;

        static ClientInstance() {
            isEnabled_ = true;
        }

        public static bool IsEnabled => isEnabled_;

        public static bool AutoAttach { get; set; }
        public static bool IsConnected => debugSesssionInitialized_;

        public static bool ToggleEnabled() {
            isEnabled_ = !isEnabled_;
            Logger.Log($"IR Explorer extension enabled: {isEnabled_}");
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

                    return (result && serverChannel_.State == ChannelState.Ready) ||
                           serverChannel_.State == ChannelState.Idle;
                }
                else {
                    return true;
                }
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
                    Logger.Log("Connecting to IR Explorer...");
                    debugClient_ = new DebugService.DebugServiceClient(serverChannel_);

                    var result = debugClient_.StartSession(new StartSessionRequest {
                        Kind = ClientKind.Debugger,
                        ProcessId = processId
                    });

                    debugSessionId_ = result.SessionId;
                    Logger.Log("Connected to IR Explorer instance");
                }

                return debugClient_;
            }
            catch (Exception ex) {
                Logger.LogException(ex, "Failed to connect to IR Explorer");
                return null;
            }
        }

        private static void CreateServerChannel() {
            if (serverChannel_ == null) {
                serverChannel_ = new Channel("127.0.0.1:50051", ChannelCredentials.Insecure);
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

        internal static void PauseCurrentElementHandling() {
            RunClientCommand(() => {
                debugClient_.SetSessionState(new SessionStateRequest {
                    State = SessionState.Paused
                });
            }).Wait();
        }

        internal static void ResumeCurrentElementHandling() {
            RunClientCommand(() => {
                debugClient_.SetSessionState(new SessionStateRequest {
                    State = SessionState.Listening
                });
            }).Wait();
        }

        public static void UpdateIR() {
            RunClientCommand(() => { DebuggerInstance.UpdateIR(); }).Wait();
        }

        public static void UpdateCurrentStackFrame() {
            RunClientCommand(() => {
                debugClient_.UpdateCurrentStackFrame(new CurrentStackFrameRequest {
                    CurrentFrame = DebuggerInstance.GetCurrentStackFrame()
                });
            }).Wait();
        }

        public static void ClearTemporaryHighlighting() {
            RunClientCommand(() => debugClient_.ClearTemporaryHighlighting(
                                 new ClearHighlightingRequest {
                                     Highlighting = HighlightingType.Temporary
                                 })).Wait();
        }

        private static string GetIRExplorerPath() {
#if false
            return @"C:\personal\projects\compiler_studio\Client\bin\Debug\net5.0\irexplorer.exe";
#endif
            string path = NativeMethods.GetFullPathFromWindows("irexplorer.exe");

            if (File.Exists(path)) {
                return path;
            }

            return File.Exists(DefaultIRExplorerPath) ? DefaultIRExplorerPath : null;
        }

        private static async Task<bool> StartIRExplorerAsync() {
            if (IsServerStarted()) {
                return true;
            }

            try {
                string irxArgs = "-grpc-server";
                string irxPath = GetIRExplorerPath();
                IRExplorerExtensionPackage.SetStatusBar("Starting IR Explorer...", true);
                Logger.Log($"Starting IR Explorer: {irxPath}");

                if (irxPath == null) {
                    VsShellUtilities.ShowMessageBox(
                        Package,
                        "Could not find IR Explorer, make sure it is properly installed and found on PATH",
                        "IR Explorer extension", OLEMSGICON.OLEMSGICON_WARNING,
                        OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);

                    Logger.LogError("irexplorer.exe not found");
                    IRExplorerExtensionPackage.SetStatusBar("Could not find IR Explorer");
                    return false;
                }

                var psi = new ProcessStartInfo(irxPath, irxArgs);
                var process = Process.Start(psi);
                bool result = process != null && 
                              await System.Threading.Tasks.Task.Run(() => process.WaitForInputIdle(30000));

                if (result) {
                    Logger.Log("Started IR Explorer instance");
                    IRExplorerExtensionPackage.SetStatusBar("IR Explorer started");
                }
                else {
                    Logger.LogError("Failed to start IR Explorer instance");
                    IRExplorerExtensionPackage.SetStatusBar("Failed to start IR Explorer");
                }

                return result;
            }
            catch (Exception ex) {
                VsShellUtilities.ShowMessageBox(
                    Package, $"Failed to start IR Explorer, exception {ex.Message}",
                    "IR Explorer extension", OLEMSGICON.OLEMSGICON_WARNING,
                    OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);

                Logger.LogException(ex, "Failed to start IR Explorer");
                return false;
            }
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
                if (!await StartIRExplorerAsync()) {
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
    }
}
