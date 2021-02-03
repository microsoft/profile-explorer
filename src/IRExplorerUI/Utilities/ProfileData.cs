using System;
using System.Collections.Generic;
using System.Diagnostics;
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
                imageName = Path.GetFileNameWithoutExtension(imageName);

                using var trace = TraceProcessor.Create(tracePath);
                IPendingResult<ISymbolDataSource> pendingSymbolData = trace.UseSymbols();
                IPendingResult<ICpuSampleDataSource> pendingCpuSamplingData = trace.UseCpuSamplingData();

                trace.Process();

                // Load symbols.
                ISymbolDataSource symbolData = pendingSymbolData.Result;
                ICpuSampleDataSource cpuSamplingData = pendingCpuSamplingData.Result;
                await symbolData.LoadSymbolsAsync(SymCachePath.Automatic, new RawSymbolPath(symbolPath));

                
                HashSet<string> images = new HashSet<string>();

                foreach (var sample in cpuSamplingData.Samples) {
                    if (sample.IsExecutingDeferredProcedureCall == true || 
                        sample.IsExecutingInterruptServicingRoutine == true) {
                        continue;
                    }

                    if(!images.Contains(sample.Process.ImageName)) {
                        images.Add(sample.Process.ImageName);
                        Trace.TraceWarning($"image: {sample.Process.ImageName}");
                    }

                    if (!sample.Process.ImageName.Contains(imageName, StringComparison.OrdinalIgnoreCase) ||
                        sample.Stack == null) {
                        continue;
                    }

                    //? TODO: parallel
                    foreach (var frame in sample.Stack.Frames) {
                        // Ignore samples targeting other images loaded in the process.
                        if (frame.Image == null ||
                            !frame.Image.FileName.Contains(imageName, StringComparison.OrdinalIgnoreCase)) {
                            continue;
                        }

                        var symbol = frame.Symbol;

                        if (symbol != null) {
                            //? TODO: FunctionName is unmangled, summary has mangled names
                            List<IRTextFunction> functs = null;

                            if (symbol.FunctionName.Contains("::")) {
                                //? TODO: Hacky way of dealing with manged C++ names
                                var parts = symbol.FunctionName.Split("::", StringSplitOptions.RemoveEmptyEntries);
                                functs = summary_.FindAllFunctions(parts);
                            }
                            else {
                                functs = summary_.FindAllFunctions(symbol.FunctionName);
                            }

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
                        else {
                            Trace.WriteLine("Could not find debug info for\n");
                            Trace.WriteLine($"   image: {frame.Image.FileName}");
                            Trace.WriteLine($"   pdb path: {frame.Image.Pdb.Path}");
                            Trace.WriteLine($"   pdb loaded: {frame.Image.Pdb.IsLoaded}");
                            Trace.WriteLine($"   pdb id: {frame.Image.Pdb.Id}");
                            Trace.Flush();
                        }
                    }
                }
            }
            catch (Exception ex) {
                Trace.WriteLine($"Exception loading profile: {ex.Message}");
                Trace.Flush();
                return false;
            }

            return true;
        }
    }
}