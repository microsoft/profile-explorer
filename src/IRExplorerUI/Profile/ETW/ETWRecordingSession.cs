using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using IRExplorerCore;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using Microsoft.Diagnostics.Tracing.Session;

namespace IRExplorerUI.Profile.ETW {
    public class ETWRecordingSession : IDisposable {
        private TraceEventSession session_;
        private string sessionName_;

        public static bool RequiresElevation => TraceEventSession.IsElevated() != true;

        public float SamplingFrequency { get; set; }

        public ETWRecordingSession(string sessionName = null) {
            Debug.Assert(!RequiresElevation);
            sessionName_ = sessionName;
        }

        //? TODO: Filter by name or by ProcID

        public Task<RawProfileData> StartRecording(ProfileLoadProgressHandler progressCallback, CancelableTask cancelableTask) {
            // The entire ETW processing must be done on the same thread.
            return Task.Run(() => {
                if (!StartSession()) {
                    return null;
                }

                bool handleDotNetEvents = true;

                RawProfileData profile = null;
                var capturedEvents = KernelTraceEventParser.Keywords.ImageLoad |
                                     KernelTraceEventParser.Keywords.Process |
                                     KernelTraceEventParser.Keywords.Thread |
                                     //KernelTraceEventParser.Keywords.ContextSwitch |
                                     KernelTraceEventParser.Keywords.Profile;

                try {
                    session_.EnableKernelProvider(capturedEvents, capturedEvents); // With stack sampling.
                    session_.EnableProvider(SymbolTraceEventParser.ProviderGuid, TraceEventLevel.Verbose);

                    if (handleDotNetEvents) {
                        session_.EnableProvider(
                            ClrTraceEventParser.ProviderGuid,
                            TraceEventLevel.Verbose,
                            (ulong)(ClrTraceEventParser.Keywords.Jit |
                                    ClrTraceEventParser.Keywords.JittedMethodILToNativeMap));
                        session_.EnableProvider(
                            ClrRundownTraceEventParser.ProviderGuid,
                            TraceEventLevel.Verbose,
                            (ulong)(ClrTraceEventParser.Keywords.Jit |
                                    ClrTraceEventParser.Keywords.JittedMethodILToNativeMap));
                    }

                    using var eventProcessor = new ETWEventProcessor(session_.Source, true, handleDotNetEvents);
                    profile = eventProcessor.ProcessEvents(progressCallback, cancelableTask);
                }
                catch (Exception ex) {
                    Trace.TraceError($"Failed ETW event capture: {ex.Message}");
                }
                finally {
                    StopSession();
                }

                return profile;
            });
        }

        private bool StartSession() {
            // Start a new in-memory session.
            try {
                Debug.Assert(session_ == null);
                session_ = new TraceEventSession(sessionName_ ?? $"IRX-ETW-{Guid.NewGuid()}");
                session_.CpuSampleIntervalMSec = 1000.0f / SamplingFrequency;
                return true;
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed to start ETW capture session: {ex.Message}");
                StopSession();
                return false;
            }
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
