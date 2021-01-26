using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Windows.EventTracing;
using Microsoft.Windows.EventTracing.Cpu;
using Microsoft.Windows.EventTracing.Symbols;
using IRExplorerCore;

namespace IRExplorerUI.Utilities {
    public class FunctionProfileData {
        public string SourceFilePath { get; set; }
        public Duration TotalWeight { get; set; }
        public Dictionary<int, Duration> SourceLineWeight { get; set; }

        public FunctionProfileData(string filePath) {
            SourceFilePath = filePath;
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
            TotalWeight = Duration.Zero;
        }
        
        public Dictionary<IRTextFunction, FunctionProfileData> FunctionProfiles { get; }
        public Duration TotalWeight { get; set; }

        public double ScaleFunctionWeight(Duration weight) {
            return (double)weight.Nanoseconds / (double)TotalWeight.Nanoseconds;
        }

        public async Task<bool> LoadTrace(string tracePath, string imageName, string symbolPath) {
            try {
                // Extract just the file name.
                imageName = Path.GetFileName(imageName);

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
                                    profile = new FunctionProfileData(symbol.SourceFileName);
                                    FunctionProfiles[textFunction] = profile;
                                }
                                
                                profile.AddSample(symbol.SourceLineNumber + 1, sample.Weight);
                                TotalWeight += sample.Weight;
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