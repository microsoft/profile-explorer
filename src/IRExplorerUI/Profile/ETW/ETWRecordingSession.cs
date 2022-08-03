using System;
using System.Collections.Generic;
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
        private ProfileDataProviderOptions providerOptions_;
        private ProfileLoadProgressHandler progressCallback_;
        private DateTime lastEventTime_;
        private string sessionName_;
        private string managedAsmDir_;

        public static bool RequiresElevation => TraceEventSession.IsElevated() != true;

        public ETWRecordingSession(ProfileDataProviderOptions providerOptions,
                                   string sessionName = null) {
            Debug.Assert(!RequiresElevation);
            options_ = providerOptions.RecordingSessionOptions;
            providerOptions_ = providerOptions;

            if (options_.EnablePerformanceCounters) {
                // To record CPU perf. counters, a kernel session is needed.
                sessionName_ = KernelTraceEventParser.KernelSessionName;
            }
            else {
                sessionName_ = sessionName ?? $"IRX-ETW-{Guid.NewGuid()}";
            }
        }

        void SessionProgressHandler(ProfileLoadProgress info) {
            // Record the last time samples were processed, this is used in
            // CreateApplicationExitTask to keep the ETW session running after the app exits,
            // but ETW events for the app are still incoming.
            lastEventTime_ = DateTime.UtcNow;
            progressCallback_?.Invoke(info);
        }

        public static List<PerformanceCounterInfo> BuiltinPerformanceCounters {
            get {
                var list = new List<PerformanceCounterInfo>();

                try {
                    var counters = TraceEventProfileSources.GetInfo();

                    foreach (var counter in counters) {
                        list.Add(new PerformanceCounterInfo(counter.Value.ID, counter.Value.Name,
                                                            counter.Value.MinInterval));
                    }
                }
                catch (Exception ex) {
                    Trace.TraceError($"Failed to get CPU perf counters: {ex.Message}");
                }
                
                return list;
            }
        }

        public Task<RawProfileData> StartRecording(ProfileLoadProgressHandler progressCallback, 
                                                   CancelableTask cancelableTask) {
            int acceptedProcessId = 0;
            Process profiledProcess = null;
            WindowsThreadSuspender threadSuspender = null;
            var sessionStarted = new ManualResetEvent(false);
            
            // The entire ETW processing must be done on the same thread.
            RawProfileData profile = null;

            // Start a task that runs the ETW session and captures the events.
            var eventTask = Task.Run(() => {
                try {
                    if (!CreateSession()) {
                        return null;
                    }

                    // Start the profiled application.
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

                    var capturedEvents = KernelTraceEventParser.Keywords.ImageLoad |
                                                KernelTraceEventParser.Keywords.Process |
                                                KernelTraceEventParser.Keywords.Thread |
                                                //KernelTraceEventParser.Keywords.ContextSwitch |
                                                KernelTraceEventParser.Keywords.Profile;

                    if (options_.EnablePerformanceCounters && options_.PerformanceCounters.Count > 0) {
                        // Enable the CPU perf. counters to collect.
                        capturedEvents |= KernelTraceEventParser.Keywords.PMCProfile;

                        var counterIds = new int[options_.PerformanceCounters.Count];
                        var frequencyCounts = new int[options_.PerformanceCounters.Count];
                        int index = 0;

                        foreach (var counter in options_.PerformanceCounters) {
                            counterIds[index] = counter.Counter.Id;
                            frequencyCounts[index] = 65536;
                            //? TODO: frequencyCounts[0] = counter.Frequency;
                            index++;

                            Trace.WriteLine($"Enabled counter {counter.Counter.Name}");
                        }

                        TraceEventProfileSources.Set(counterIds, frequencyCounts);
                        session_.EnableKernelProvider(capturedEvents, capturedEvents); // With stack sampling.

                    }
                    else {
                        session_.EnableKernelProvider(capturedEvents, capturedEvents); // With stack sampling.
                        session_.EnableProvider(SymbolTraceEventParser.ProviderGuid, TraceEventLevel.Verbose);
                    }

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

                    // Start the ETW session.
                    using var eventProcessor =
                        new ETWEventProcessor(session_.Source, providerOptions_,
                                     true, acceptedProcessId,
                                              options_.ProfileChildProcesses,
                                              options_.ProfileDotNet, managedAsmDir_);

                    sessionStarted.Set();
                    progressCallback_ = progressCallback;
                    lastEventTime_ = DateTime.MinValue;
                    return eventProcessor.ProcessEvents(SessionProgressHandler, cancelableTask);
                }
                catch (Exception ex) {
                    Trace.TraceError($"Failed ETW event capture: {ex.Message}\n{ex.StackTrace}");
                    threadSuspender?.Dispose(); // This resume all profiled app threads.
                    sessionStarted.Set();
                    return null;
                }
                finally {
                    StopSession();
                    profiledProcess?.Dispose();
                    profiledProcess = null;
                }
            });

            // Start a task that waits for the ETW session task to complete.
            return Task.Run(() => {
                try {
                    // Wait until the ETW session task starts.
                    while (!cancelableTask.IsCanceled &&
                           !sessionStarted.WaitOne(100)) { }

                    // Resume all profiled app threads and start waiting for it to close,
                    // then wait for ETW session to complete.
                    threadSuspender?.Dispose();
                    profile = eventTask.Result;
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
                Trace.WriteLine($"=> started {options_.ApplicationPath}");

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
                    
                    // Once the app exits, wait until no more ETW events are arriving
                    // to be processed, then stop the session.
                    int waitCount = 0;

                    while (!task.IsCanceled) {
                        // If no events arrived yet at all, wait a while longer.
                        if (lastEventTime_ == DateTime.MinValue) {
                            Thread.Sleep(500);

                            if (waitCount++ > 10) break;
                        }
                        else {
                            var timeSinceLastSample = DateTime.UtcNow - lastEventTime_;

                            if (timeSinceLastSample.TotalMilliseconds > 1000) {
                                break;
                            }

                            Thread.Sleep(100);
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
