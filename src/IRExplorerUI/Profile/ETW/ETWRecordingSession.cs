using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IRExplorerCore;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using Microsoft.Diagnostics.Tracing.Session;

namespace IRExplorerUI.Profile {
    public sealed class ETWRecordingSession : IDisposable {
        //private static readonly string ProfilerPath = "irexplorer_profiler.dll";
        //private static readonly string ProfilerPath = @"D:\DotNextMoscow2019\x64\Release\irexplorer_profiler.dll";
        private static readonly string ProfilerPath = @"D:\DotNextMoscow2019\x64\Debug\irexplorer_profiler.dll";
        private static readonly string ProfilerGuid = "{805A308B-061C-47F3-9B30-F785C3186E81}";

        private TraceEventSession session_;
        private DiagnosticsClient diagClient_;
        private ProfilerNamedPipeServer pipeServer_;

        private ProfileRecordingSessionOptions options_;
        private ProfileDataProviderOptions providerOptions_;
        private ProfileLoadProgressHandler progressCallback_;
        private DateTime lastEventTime_;
        private string sessionName_;
        private string profilerPath_;

        public static bool RequiresElevation => TraceEventSession.IsElevated() != true;

        public ETWRecordingSession(ProfileDataProviderOptions providerOptions,
                                   string sessionName = null) {
            Debug.Assert(!RequiresElevation);
            options_ = providerOptions.RecordingSessionOptions;
            providerOptions_ = providerOptions;

            if (options_.RecordPerformanceCounters) {
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

        public static List<PerformanceCounterConfig> BuiltinPerformanceCounters {
            get {
                var list = new List<PerformanceCounterConfig>();

                try {
                    var counters = TraceEventProfileSources.GetInfo();

                    foreach (var counter in counters) {
                        // Filter out the Timer.
                        if (counter.Value.ID == 0) {
                            continue;
                        }

                        list.Add(new PerformanceCounterConfig(counter.Value.ID, counter.Value.Name,
                                                              counter.Value.Interval,
                                                              counter.Value.MinInterval,
                                                              counter.Value.MaxInterval, true));
                    }
                }
                catch (Exception ex) {
                    Trace.TraceError($"Failed to get CPU perf counters: {ex.Message}");
                }

                return list;
            }
        }

        public static List<PerformanceMetricConfig> BuiltinPerformanceMetrics {
            get {
                //? TODO: Configurable list
                var list = new List<PerformanceMetricConfig>();
                list.Add(new PerformanceMetricConfig("DCacheMiss", "DcacheAccesses", "DcacheMisses", true, "Data cache miss percentage"));
                list.Add(new PerformanceMetricConfig("ICacheMiss", "ICFetch", "ICMiss", true, "Instruction cache miss percentage"));
                list.Add(new PerformanceMetricConfig("MispredBr", "BranchInstructions", "BranchMispredictions", true, "Branch misprediction percentage"));
                list.Add(new PerformanceMetricConfig("CPI", "InstructionRetired", "TotalCycles", false, "Clockticks per Instructions retired rate"));
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
            // Start a task that runs the ETW session and captures the events.
            RawProfileData profile = null;

            var eventTask = Task.Run(() => {
                try {
                    if (!CreateSession(cancelableTask)) {
                        return null;
                    }

                    // Start the profiled application.
                    switch (options_.SessionKind) {
                        case ProfileSessionKind.StartProcess: {
                            (profiledProcess, acceptedProcessId) = StartProfiledApplication();

                            if (profiledProcess == null) {
                                sessionStarted.Set(); // Unblock waiting task below.
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
                        case ProfileSessionKind.AttachToProcess: {
                            try {
                                profiledProcess = Process.GetProcessById(acceptedProcessId);
                                acceptedProcessId = options_.TargetProcessId;
                            }
                            catch (Exception ex) {
                                Trace.WriteLine($"Failed to attach to process {options_.TargetProcessId}");
                                StopSession();
                                return null;
                            }

                            if (options_.ProfileDotNet) {
                                if (!AttachProfiler(acceptedProcessId)) {
                                    StopSession();
                                    return null;
                                }
                            }

                            // Start task that waits for the process to exit,
                            // which will stop the ETW session.

                            //? TODO: Doesns't work for attached process!
                            //? CreateApplicationExitTask(profiledProcess, cancelableTask);
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

                    if (options_.RecordPerformanceCounters && options_.PerformanceCounters.Count > 0) {
                        // Enable the CPU perf. counters to collect.
                        capturedEvents |= KernelTraceEventParser.Keywords.PMCProfile;
                        EnablePerformanceCounters();
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

                        if (options_.SessionKind == ProfileSessionKind.AttachToProcess) {
                            // Needed when attaching to a running .net app.
                            Trace.WriteLine("Enable ClrRundownTraceEventParser");

                            session_.EnableProvider(
                                ClrRundownTraceEventParser.ProviderGuid,
                                TraceEventLevel.Verbose,
                                (ulong)(ClrRundownTraceEventParser.Keywords.Jit |
                                        ClrRundownTraceEventParser.Keywords.JittedMethodILToNativeMap |
                                        ClrRundownTraceEventParser.Keywords.Loader |
                                        ClrRundownTraceEventParser.Keywords.StartEnumeration));
                        }
                    }

                    // Start the ETW session.
                    using var eventProcessor =
                        new ETWEventProcessor(session_.Source, providerOptions_,
                                              isRealTime: true, acceptedProcessId,
                                              options_.ProfileChildProcesses,
                                              options_.ProfileDotNet, pipeServer_);

                    sessionStarted.Set();
                    progressCallback_ = progressCallback;
                    lastEventTime_ = DateTime.MinValue;
                    return eventProcessor.ProcessEvents(SessionProgressHandler, cancelableTask);
                }
                catch (Exception ex) {
                    Trace.TraceError($"Failed ETW event capture: {ex.Message}\n{ex.StackTrace}");
                    threadSuspender?.Dispose(); // This resume all profiled app threads.
                    sessionStarted.Set(); // Unblock waiting task below.
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

        private void EnablePerformanceCounters() {
            var enabledCounters = options_.EnabledPerformanceCounters;

            if (enabledCounters.Count == 0) {
                return;
            }

            var counterIds = new int[enabledCounters.Count];
            var frequencyCounts = new int[enabledCounters.Count];
            int index = 0;

            foreach (var counter in enabledCounters) {
                counterIds[index] = counter.Id;
                frequencyCounts[index] = counter.Interval;
                index++;
                Trace.WriteLine($"Enabling counter {counter.Name}");
            }

            TraceEventProfileSources.Set(counterIds, frequencyCounts);
        }

        private bool CreateSession(CancelableTask cancelableTask) {
            if (options_.ProfileDotNet) {
                profilerPath_ = Path.Combine(App.ApplicationDirectory, ProfilerPath);

                try {
                    pipeServer_ = new ProfilerNamedPipeServer();
                }
                catch (Exception ex) {
                    Trace.TraceError($"Failed to start named pipe: {ex.Message}\n{ex.StackTrace}");
                    StopSession();
                    return false;
                }
            }

            // Start a new in-memory session.
            try {
                Debug.Assert(session_ == null);
                session_ = new TraceEventSession(sessionName_);
                session_.BufferSizeMB = Math.Max(session_.BufferSizeMB, 128);
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
                    if (!SetupStartupDotNetProfiler(procInfo)) {
                        Trace.TraceError($"Failed to setup managed profiler");
                    }
                }

                if (options_.EnableEnvironmentVars) {
                    foreach (var pair in options_.EnvironmentVariables) {
                        procInfo.EnvironmentVariables[pair.Value] = pair.Variable;
                    }
                }

                var process = new Process { StartInfo = procInfo, EnableRaisingEvents = true };
                process.Start();
                Trace.WriteLine($"Started process {options_.ApplicationPath}");

                return (process, process.Id);
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed to start profiled application {options_.ApplicationPath}: {ex.Message}\n{ex.StackTrace}");
            }

            return (null, 0);
        }

        private bool SetupStartupDotNetProfiler(ProcessStartInfo procInfo) {
            procInfo.EnvironmentVariables["CORECLR_ENABLE_PROFILING"] = "1";
            procInfo.EnvironmentVariables["CORECLR_PROFILER"] = ProfilerGuid;
            procInfo.EnvironmentVariables["CORECLR_PROFILER_PATH"] = profilerPath_;
            Trace.WriteLine($"Using managed profiler {profilerPath_}");
            return true;
        }

        bool AttachProfiler(int processId) {
            try {
                diagClient_ = new DiagnosticsClient(processId);
                //var profilerArgs = Encoding.ASCII.GetBytes(managedAsmDir_);
                var profilerArgs = new byte[0];
                diagClient_.AttachProfiler(TimeSpan.FromSeconds(10), Guid.Parse(ProfilerGuid), profilerPath_, profilerArgs);
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed to attach profiler to process {processId}: {ex.Message}");
                return false;
            }

            return true;
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
                try {
                    pipeServer_?.Stop();
                    session_.Stop();
                    session_.Dispose();
                }
                catch (Exception ex) {
                    Trace.TraceError($"Failed to stop ETW session: {ex.Message}");
                }

                session_ = null;
            }
        }

        public void Dispose() {
            StopSession();
        }
    }
}