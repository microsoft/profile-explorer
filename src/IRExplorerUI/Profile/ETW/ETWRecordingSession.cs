using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IRExplorerCore;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using Microsoft.Diagnostics.Tracing.Session;

namespace IRExplorerUI.Profile.ETW {
    public class ETWRecordingSession : IDisposable {
        private TraceEventSession session_;
        private ProfileRecordingSessionOptions options_;
        private string sessionName_;

        public static bool RequiresElevation => TraceEventSession.IsElevated() != true;
        
        public ETWRecordingSession(ProfileRecordingSessionOptions options, string sessionName = null) {
            Debug.Assert(!RequiresElevation);
            options_ = options;
            sessionName_ = sessionName;
        }

        public Task<RawProfileData> StartRecording(ProfileLoadProgressHandler progressCallback, CancelableTask cancelableTask) {
            int acceptedProcessId = 0;
            Process profiledProcess = null;
            WindowsThreadSuspender threadSuspender = null;

            switch (options_.SessionKind) {
                case ProfileSessionKind.StartProcess: {
                    (profiledProcess, acceptedProcessId) = StartProfiledApplication();

                    if (profiledProcess == null) {
                        return null;
                    }
                    CreateApplicationExitTask(profiledProcess, cancelableTask);

                    // Suspend the application before the session starts,
                    // then resume it once everything is set up.
                    try {
                        threadSuspender = new WindowsThreadSuspender(acceptedProcessId);
                    }
                    catch (Exception ex) {
                        Trace.TraceError($"Failed to suspend application threads {options_.ApplicationPath}: {ex.Message}\n{ex.StackTrace}");
                    }
                    break;
                }
                case ProfileSessionKind.SystemWide: {
                    // ETW sessions are system-wide by default.
                    break;
                }
                default: {
                    throw new NotImplementedException();
                }
            }
            
            //var pmcs = TraceEventProfileSources.GetInfo();


                // The entire ETW processing must be done on the same thread.
            return Task.Run(() => {
                if (!StartSession()) {
                    return null;
                }


                RawProfileData profile = null;
                var capturedEvents = KernelTraceEventParser.Keywords.ImageLoad |
                                     KernelTraceEventParser.Keywords.Process |
                                     KernelTraceEventParser.Keywords.Thread |
                                     //KernelTraceEventParser.Keywords.ContextSwitch |
                                     KernelTraceEventParser.Keywords.Profile;

                try {
                    session_.EnableKernelProvider(capturedEvents, capturedEvents); // With stack sampling.
                    session_.EnableProvider(SymbolTraceEventParser.ProviderGuid, TraceEventLevel.Verbose);

                    if (options_.ProfileDotNet) {
                        session_.EnableProvider(
                            ClrTraceEventParser.ProviderGuid,
                            TraceEventLevel.Verbose,
                            (ulong)(ClrTraceEventParser.Keywords.Jit |
                                    ClrTraceEventParser.Keywords.JittedMethodILToNativeMap));
                        session_.EnableProvider(
                            ClrRundownTraceEventParser.ProviderGuid,
                            TraceEventLevel.Verbose,
                            (ulong)(ClrRundownTraceEventParser.Keywords.Jit |
                                    ClrRundownTraceEventParser.Keywords.JittedMethodILToNativeMap));
                    }

                    //? / The Profile event requires the SeSystemProfilePrivilege to succeed, so set it.  
                    //if ((flags & (KernelTraceEventParser.Keywords.Profile | KernelTraceEventParser.Keywords.PMCProfile)) != 0) {
                    //    TraceEventNativeMethods.SetPrivilege(TraceEventNativeMethods.SE_SYSTEM_PROFILE_PRIVILEGE);

                    // Resume all profiled app threads and start waiting for it to close.
                    threadSuspender?.Dispose();
                    using var eventProcessor = new ETWEventProcessor(session_.Source, true,
                                                                      acceptedProcessId, options_.ProfileDotNet);
                    profile = eventProcessor.ProcessEvents(progressCallback, cancelableTask);
                }
                catch (Exception ex) {
                    Trace.TraceError($"Failed ETW event capture: {ex.Message}\n{ex.StackTrace}");
                    threadSuspender?.Dispose(); // This resume all profiled app threads.
                    profiledProcess?.Dispose();
                }
                finally {
                    StopSession();
                    profiledProcess?.Dispose();
                }

                return profile;
            });
        }

        private bool StartSession() {
            // Start a new in-memory session.
            try {
                Debug.Assert(session_ == null);
                session_ = new TraceEventSession(sessionName_ ?? $"IRX-ETW-{Guid.NewGuid()}");
                session_.CpuSampleIntervalMSec = 1000.0f / options_.SamplingFrequency;
                return true;
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed to start ETW capture session: {ex.Message}\n{ex.StackTrace}");
                StopSession();
                return false;
            }
        }

        private (Process, int) StartProfiledApplication() {
            try {
                var procInfo = new ProcessStartInfo(options_.ApplicationPath) {
                    Arguments = options_.ApplicationArguments,
                    WorkingDirectory = options_.HasWorkingDirectory ?
                        options_.WorkingDirectory :
                        Utils.TryGetDirectoryName(options_.ApplicationPath),
                    Verb = "runas"
                    //RedirectStandardError = false,
                    //RedirectStandardOutput = true
                };

                var process = new Process { StartInfo = procInfo, EnableRaisingEvents = true };
                process.Start();
                return (process, process.Id);
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed to start profiled application {options_.ApplicationPath}: {ex.Message}\n{ex.StackTrace}");
            }

            return (null, 0);
        }

        private void CreateApplicationExitTask(Process process, CancelableTask task) {
            Task.Run(() => {
                try {
                    while (!task.IsCanceled && !task.IsCompleted) {
                        if (process.WaitForExit(100)) {
                            break;
                        }
                    }

                    StopSession();
                }
                catch (Exception ex) {
                    Trace.TraceError($"Failed to wait for profiled application exit {options_.ApplicationPath}: {ex.Message}\n{ex.StackTrace}");
                }
            });
        }

        private void StopSession() {
            session_?.Stop();
            session_?.Dispose();
            session_ = null;
        }

        public void Dispose() {
            session_?.Dispose();
        }
    }
}
