using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Grpc.Core;
using EnvDTE;
using EnvDTE80;
using System.IO;
using Microsoft.VisualStudio.Shell.Interop;

namespace IRExplorerExtension
{
    internal static class ClientInstance
    {
        static readonly string DefaultIRExplorerPath = @"C:\Program Files (x86)\IR Explorer\irexplorer.exe";

        public static Channel serverChannel_;
        public static DebugService.DebugServiceClient debugClient_;
        public static long debugSessionId_;
        public static bool debugSesssionInitialized_;
        public static AsyncPackage Package;
        public static bool isEnabled_;

        static ClientInstance()
        {
            isEnabled_ = true;
        }

        public static bool IsEnabled => isEnabled_;

        public static bool ToggleEnabled()
        {
            isEnabled_ = !isEnabled_;
            Logger.Log($"IR Explorer extension enabled: {isEnabled_}");
            return isEnabled_;
        }

        public static bool IsConnected => debugSesssionInitialized_;

        public static void Initialize(AsyncPackage package)
        {
            Package = package;
        }

        public static bool IsServerStarted()
        {
            try
            {
                CreateServerChannel();
                var timeout = DateTime.UtcNow.AddMilliseconds(100);
                return ThreadHelper.JoinableTaskFactory.Run(async () =>
                {
                    await serverChannel_.ConnectAsync(timeout);
                    return true;
                });
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "ClientInstance exception");
                return false;
            }
        }

        public static async Task<bool> WaitForDebugServer(DateTime timeout)
        {
            try
            {
                if (serverChannel_.State != ChannelState.Ready)
                {
                    var result = await serverChannel_.TryWaitForStateChangedAsync(serverChannel_.State, timeout);
                    return result && serverChannel_.State == ChannelState.Ready ||
                                     serverChannel_.State == ChannelState.Idle;
                }
                else
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "ClientInstance exception");
                return false;
            }
        }

        public static DebugService.DebugServiceClient GetDebugClient(string processName, int processId)
        {
            try
            {
                if (serverChannel_ == null)
                {
                    serverChannel_ = new Channel("127.0.0.1:50051", ChannelCredentials.Insecure);
                }

                if (debugClient_ == null)
                {
                    Logger.Log("Connecting to IR Explorer...");
                    debugClient_ = new DebugService.DebugServiceClient(serverChannel_);
                    var result = debugClient_.StartSession(new StartSessionRequest()
                    {
                        Kind = ClientKind.Debugger,
                        ProcessId = processId
                    });

                    debugSessionId_ = result.SessionId;
                    Logger.Log("Connected to IR Explorer instance");
                }

                return debugClient_;
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "ClientInstance exception");
                return null;
            }
        }

        private static void CreateServerChannel()
        {
            if (serverChannel_ == null)
            {
                serverChannel_ = new Channel("127.0.0.1:50051", ChannelCredentials.Insecure);
            }
        }

        public static void Shutdown()
        {
            try
            {
                if (debugClient_ != null)
                {
                    debugClient_.EndSession(new EndSessionRequest());
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "ClientInstance exception");
            }

            try
            {
                if (serverChannel_ != null)
                {
                    serverChannel_.ShutdownAsync().Wait();
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "ClientInstance exception");
            }

            debugSesssionInitialized_ = false;
            serverChannel_ = null;
            debugClient_ = null;
            debugSessionId_ = 0;
        }

        public static async Task<bool> RunClientCommand(Action action)
        {
            bool retry = true;
            int retryCount = 0;

            while (retry && retryCount < 2)
            {
                if (!SetupDebugSession())
                {
                    return false;
                }

                retry = false;

                try
                {
                    action();
                    return true;
                }
                catch (RpcException rpcEx)
                {
                    Logger.LogException(rpcEx, "ClientInstance RPC exception");

                    switch (rpcEx.StatusCode)
                    {
                        case StatusCode.Unavailable:
                        case StatusCode.DeadlineExceeded:
                        case StatusCode.Aborted:
                            {
                                Shutdown();
                                retry = true;
                                retryCount++;
                                break;
                            }
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogException(ex, "ClientInstance exception");
                    return false;
                }
            }

            return false;
        }

        internal static void PauseCurrentElementHandling()
        {
            RunClientCommand(() =>
            {
                debugClient_.SetSessionState(new SessionStateRequest()
                {
                    State = SessionState.Paused
                });
            }).Wait();
        }

        internal static void ResumeCurrentElementHandling()
        {
            RunClientCommand(() =>
            {
                debugClient_.SetSessionState(new SessionStateRequest()
                {
                    State = SessionState.Listening
                });
            }).Wait();
        }

        public static void UpdateIR()
        {
            RunClientCommand(() =>
            {
                DebuggerInstance.UpdateIR();
            }).Wait();
        }

        public static void UpdateCurrentStackFrame()
        {
            RunClientCommand(() =>
            {
                debugClient_.UpdateCurrentStackFrame(new CurrentStackFrameRequest()
                {
                    CurrentFrame = DebuggerInstance.GetCurrentStackFrame()
                });
            }).Wait();
        }

        public static void ClearTemporaryHighlighting()
        {
            RunClientCommand(() =>
                debugClient_.ClearTemporaryHighlighting(new ClearHighlightingRequest() {
                Highlighting = HighlightingType.Temporary
            })).Wait();
        }


        static string GetIRExplorerPath()
        {
#if false
            return @"C:\personal\projects\compiler_studio\Client\bin\Release\net5.0\irexplorer.exe";
#endif
            var path = NativeMethods.GetFullPathFromWindows("irexplorer.exe");

            if(File.Exists(path))
            {
                return path;
            }

           if(File.Exists(DefaultIRExplorerPath))
            {
                return DefaultIRExplorerPath;
            }

            return null;
        }

        static bool StartIRExplorer()
        {

            if(IsServerStarted())
            {
                return true;
            }

            try
            {
                string irxArgs = "-grpc-server";
                string irxPath = GetIRExplorerPath();
                Logger.Log($"Starting IR Explorer: {irxPath}");

                if (irxPath == null)
                {
                    VsShellUtilities.ShowMessageBox(Package,
                       "Could not find IR Explorer, make sure it is properly installed and found on PATH",
                       "IR Explorer extension",
                       OLEMSGICON.OLEMSGICON_WARNING,
                       OLEMSGBUTTON.OLEMSGBUTTON_OK,
                       OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                    Logger.LogError("irexplorer.exe not found");
                    return false;
                }

                var psi = new System.Diagnostics.ProcessStartInfo(irxPath, irxArgs);
                var process = System.Diagnostics.Process.Start(psi);
                bool result = process.WaitForInputIdle(30000);

                if(result)
                {
                    Logger.Log("Started IR Explorer instance");
                }

                return result;
            }
            catch (Exception ex)
            {
                VsShellUtilities.ShowMessageBox(Package,
                   $"Failed to start IR Explorer, exception {ex.Message}",
                   "IR Explorer extension",
                   OLEMSGICON.OLEMSGICON_WARNING,
                   OLEMSGBUTTON.OLEMSGBUTTON_OK,
                   OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                Logger.LogException(ex, "Failed to start IR Explorer");
                return false;
            }
        }

        public static bool SetupDebugSession()
        {
            if(debugSesssionInitialized_)
            {
                return true;
            }

            if (DebuggerInstance.InBreakMode)
            {
                //var stackFrame = debugger_.CurrentStackFrame as EnvDTE90a.StackFrame2;
                //var function = stackFrame.FunctionName;
                //var lineNumber = stackFrame.LineNumber;
                //var file = stackFrame.FileName;

                //? Logic to launch IRexp instance 
                //? that starts server on startup
                if(!StartIRExplorer())
                {
                    return false;
                }

                var client = GetDebugClient(DebuggerInstance.ProcessName, DebuggerInstance.ProcessId);

                if (client == null)
                {
                    return false;
                }

                debugSesssionInitialized_ = true;
                return true;
            }

            return false;
        }
    }
}
