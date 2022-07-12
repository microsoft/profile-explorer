﻿using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IRExplorerCore;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;

namespace IRExplorerUI.Profile.ETW {
    public class ETWRecordingSession : IDisposable {
        private TraceEventSession session_;
        private ProfileRecordingSessionOptions options_;
        private string sessionName_;
        private string managedAsmDir_;

        public static bool RequiresElevation => TraceEventSession.IsElevated() != true;
        
        public ETWRecordingSession(ProfileRecordingSessionOptions options, string sessionName = null) {
            Debug.Assert(!RequiresElevation);
            options_ = options;
            sessionName_ = sessionName ?? $"IRX-ETW-{Guid.NewGuid()}";
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

                    // Start task that waits for the process to exit,
                    // which will stop the ETW session.
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
            RawProfileData profile = null;
            var capturedEvents = KernelTraceEventParser.Keywords.ImageLoad |
                                 KernelTraceEventParser.Keywords.Process |
                                 KernelTraceEventParser.Keywords.Thread |
                                 //KernelTraceEventParser.Keywords.ContextSwitch |
                                 KernelTraceEventParser.Keywords.Profile;

            Trace.WriteLine($"=> Start task {DateTime.Now}");

            var eventTask = Task.Run(() => {
                try {
                    if (!CreateSession()) {
                        return null;
                    }

                    session_.EnableKernelProvider(capturedEvents, capturedEvents); // With stack sampling.
                    session_.EnableProvider(SymbolTraceEventParser.ProviderGuid, TraceEventLevel.Verbose);

                    if (options_.ProfileDotNet) {
                        session_.EnableProvider(
                            ClrTraceEventParser.ProviderGuid,
                            TraceEventLevel.Verbose,
                            (ulong)(ClrTraceEventParser.Keywords.Jit |
                                    ClrTraceEventParser.Keywords.JittedMethodILToNativeMap |
                                    ClrTraceEventParser.Keywords.Loader));
                        //session_.EnableProvider(
                        //    ClrRundownTraceEventParser.ProviderGuid,
                        //    TraceEventLevel.Verbose,
                        //    (ulong)(ClrRundownTraceEventParser.Keywords.Jit |
                        //            ClrRundownTraceEventParser.Keywords.JittedMethodILToNativeMap));
                    }


                    //? The Profile event requires the SeSystemProfilePrivilege to succeed, so set it.  
                    //if ((flags & (KernelTraceEventParser.Keywords.Profile | KernelTraceEventParser.Keywords.PMCProfile)) != 0) {
                    //    TraceEventNativeMethods.SetPrivilege(TraceEventNativeMethods.SE_SYSTEM_PROFILE_PRIVILEGE);

                    // Start the ETW session.
                    using var eventProcessor =
                        new ETWEventProcessor(session_.Source, true, acceptedProcessId,
                            options_.ProfileDotNet, managedAsmDir_);

                    Trace.WriteLine($"=> ProcessEvents");
                    return eventProcessor.ProcessEvents(progressCallback, cancelableTask);
                }
                catch (Exception ex) {
                    Trace.TraceError($"Failed ETW event capture: {ex.Message}\n{ex.StackTrace}");
                    threadSuspender?.Dispose(); // This resume all profiled app threads.
                    return null;
                }
                finally {
                    StopSession();
                    profiledProcess?.Dispose();
                    profiledProcess = null;
                }
            });

            return Task.Run(() => {
                try {
                    var sw = Stopwatch.StartNew();
                    Trace.WriteLine($"=> Start tracing {DateTime.Now}");

                    // Resume all profiled app threads and start waiting for it to close,
                    // then wait for ETW session to complete.
                    Trace.WriteLine($"=> Resume app");
                    threadSuspender?.Dispose();

                    Trace.WriteLine($"=> Wait for results {DateTime.Now}");
                    eventTask.Wait();
                    profile = eventTask.Result;
                    Trace.WriteLine($"Tracing took {sw.ElapsedMilliseconds}");
                    Trace.Flush();
                }
                catch (Exception ex) {
                    Trace.TraceError($"Failed ETW event capture: {ex.Message}\n{ex.StackTrace}");
                    threadSuspender?.Dispose(); // This resume all profiled app threads.
                }
                finally {
                    StopSession();
                    profiledProcess?.Dispose();
                    profiledProcess = null;
                }
                
                return profile;
            });
        }

        private bool CreateSession() {
            // Start a new in-memory session.
            try {
                Debug.Assert(session_ == null);
                session_ = new TraceEventSession(sessionName_);
                //session_.BufferSizeMB = 256;
                session_.CpuSampleIntervalMSec = 1000.0f / options_.SamplingFrequency;

                Trace.WriteLine("Started ETW session:");
                Trace.WriteLine($"   Buffer size: {session_.BufferSizeMB} MB");
                Trace.WriteLine($"   Sampling freq: {session_.CpuSampleIntervalMSec} ms / {options_.SamplingFrequency}");
                Trace.Flush();
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
                    //? The Process object must have the UseShellExecute property set to false in order to use environment variables
                    //UseShellExecute = true,
                    Verb = "runas"
                    //RedirectStandardError = false,
                    //RedirectStandardOutput = true
                };

                if (options_.ProfileDotNet) {
                    var profilerPath = Path.Combine(App.ApplicationDirectory, "irexplorer_profiler.dll");

                    try {
                        var tempPath = Path.GetTempPath();
                        managedAsmDir_ = Path.Combine(tempPath, sessionName_);
                        Directory.CreateDirectory(managedAsmDir_);
                        Trace.WriteLine($"Using managed ASM dir {managedAsmDir_}");
                    }
                    catch (Exception ex) {
                        Trace.TraceError($"Failed to create session ASM dir:{managedAsmDir_}: {ex.Message}\n{ex.StackTrace}");
                    }

                    procInfo.EnvironmentVariables["CORECLR_ENABLE_PROFILING"] = "1";
                    procInfo.EnvironmentVariables["CORECLR_PROFILER"] = "{805A308B-061C-47F3-9B30-F785C3186E81}";
                    procInfo.EnvironmentVariables["CORECLR_PROFILER_PATH"] = profilerPath;
                    procInfo.EnvironmentVariables["IRX_MANAGED_ASM_DIR"] = managedAsmDir_;
                    Trace.WriteLine($"Using managed profiler {profilerPath}");
                }

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

                    //? TODO: Extra time needed? Some way to get notified about last ETW event for proc
                    //? Alternative is to use ETW file
                    Thread.Sleep(5000);

                    Trace.WriteLine($"=> Stop");
                    var sw = Stopwatch.StartNew();
                    
                    StopSession();
                    Trace.WriteLine($"=> Stop took {sw.ElapsedMilliseconds}");
                    Trace.Flush();
                }
                catch (Exception ex) {
                    Trace.TraceError($"Failed to wait for profiled application exit {options_.ApplicationPath}: {ex.Message}\n{ex.StackTrace}");
                }
            });
        }

        private void StopSession() {
            if (session_ != null) {
                session_.Flush();
                session_.Stop();
                session_.Dispose();
                session_ = null;
            }
        }

        public void Dispose() {
            StopSession();
        }
    }
}
