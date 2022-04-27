using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using IRExplorerCore;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;

namespace IRExplorerUI.Profile.ETW {
    public class ETWEventCapture : IDisposable {
        private TraceEventSession session_;
        private string sessionName_;

        public static bool RequiresElevation => TraceEventSession.IsElevated() != true;

        public ETWEventCapture(string sessionName = null) {
            Debug.Assert(!RequiresElevation);
            sessionName_ = sessionName;
        }

        public Task<RawProfileData> StartCapture(CancelableTask cancelableTask) {
            // The entire ETW processing must be done on the same thread.
            return Task.Run(() => {
                if (!StartSession()) {
                    return null;
                }

                RawProfileData profile = null;

                try {
                    session_.CpuSampleIntervalMSec = 0.125f;

                    //?session_.FileName
                    session_.EnableKernelProvider(
                        KernelTraceEventParser.Keywords.All);
                        //KernelTraceEventParser.Keywords.ImageLoad |
                        //KernelTraceEventParser.Keywords.Process |
                        //KernelTraceEventParser.Keywords.Thread |
                        //KernelTraceEventParser.Keywords.Profile);
                    session_.EnableProvider(SymbolTraceEventParser.ProviderGuid);

                    //session_.EnableProvider(
                    //    ClrTraceEventParser.ProviderGuid,
                    //    TraceEventLevel.Verbose,
                    //    (ulong)(ClrTraceEventParser.Keywords.Jit | ClrTraceEventParser.Keywords.JittedMethodILToNativeMap));

                    // session_.Source.Process();

                    using var eventProcessor = new ETWEventProcessor(session_.Source);
                    profile = eventProcessor.ProcessEvents(cancelableTask);
                }
                catch (Exception ex) {
                    Trace.TraceError($"Failed ETW event capture: {ex.Message}");
                }
                finally {
                    session_.Stop();
                    session_.Dispose();
                    session_ = null;
                }

                return profile;
            });
        }

        private bool StartSession() {
            session_ = new TraceEventSession(sessionName_ ?? $"IRX-ETW-Capture-{Guid.NewGuid()}");
            return true;
        }

        public void Dispose() {
            session_?.Dispose();
        }
    }
}
