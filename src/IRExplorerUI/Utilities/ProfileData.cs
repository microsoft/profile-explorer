using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Windows.EventTracing;
using Microsoft.Windows.EventTracing.Cpu;
using Microsoft.Windows.EventTracing.Symbols;
using IRExplorerCore;

namespace IRExplorerUI.Utilities {
    public class FunctionProfileData {
        public Duration TotalWeight;
        public Dictionary<int, Duration> SourceLineWeight;

        public FunctionProfileData() {
            TotalWeight = Duration.Zero;
            SourceLineWeight = new Dictionary<int, Duration>();
        }

        public void AddSample(int sourceLine, Duration weight) {
            TotalWeight += weight;

            if (SourceLineWeight.TryGetValue(sourceLine, out var currentWeight)) {
                SourceLineWeight[sourceLine] = currentWeight + weight;
            }
            else {
                SourceLineWeight[sourceLine] = weight;
            }
        }

        public double ScaleLineWeight(Duration weight) {
            return (double)weight.Nanoseconds / (double)TotalWeight.Nanoseconds;
        }
    }

    public class ProfileData {
        private IRTextSummary summary_;

        public ProfileData(IRTextSummary summary) {
            summary_ = summary;
            FunctionProfiles = new Dictionary<IRTextFunction, FunctionProfileData>();
        }
        
        public Dictionary<IRTextFunction, FunctionProfileData> FunctionProfiles { get; }

        public async Task<bool> LoadTrace(string tracePath, string imageName, string symbolPath) {
            try {
                using var trace = TraceProcessor.Create(tracePath);
                IPendingResult<ISymbolDataSource> pendingSymbolData = trace.UseSymbols();
                IPendingResult<ICpuSampleDataSource> pendingCpuSamplingData = trace.UseCpuSamplingData();

                trace.Process();

                // Load symbols.
                ISymbolDataSource symbolData = pendingSymbolData.Result;
                ICpuSampleDataSource cpuSamplingData = pendingCpuSamplingData.Result;
                await symbolData.LoadSymbolsAsync(SymCachePath.Automatic, new RawSymbolPath(symbolPath));

                foreach (var sample in cpuSamplingData.Samples) {
                    if (sample.IsExecutingDeferredProcedureCall == true || 
                        sample.IsExecutingInterruptServicingRoutine == true) {
                        continue;
                    }

                    if (sample.Process.ImageName != imageName ||
                        sample.Stack == null) {
                        continue;
                    }

                    //? TODO: parallel
                    foreach (var frame in sample.Stack.Frames) {
                        var symbol = frame.Symbol;

                        if (symbol != null) {
                            //? TODO: FunctionName is unmangled, summary has mangled names
                            var functs = summary_.FindAllFunctions(symbol.FunctionName);

                            foreach (var textFunction in functs) {
                                if (!FunctionProfiles.TryGetValue(textFunction, out var profile)) {
                                    profile = new FunctionProfileData();
                                    FunctionProfiles[textFunction] = profile;
                                }

                                profile.AddSample(symbol.SourceLineNumber + 1, sample.Weight);
                            }

                            break;
                        }
                    }
                }
            }
            catch (Exception ex) {
                return false;
            }

            return true;
        }
    }
}