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
    public class ProfileData {
        private IRTextSummary summary_;

        public ProfileData(IRTextSummary summary) {
            summary_ = summary;
            FunctionProfiles = new Dictionary<IRTextFunction, FunctionProfileData>();
            TotalWeight = TimeSpan.Zero;
        }
        
        public Dictionary<IRTextFunction, FunctionProfileData> FunctionProfiles { get; }
        public TimeSpan TotalWeight { get; set; }

        public double ScaleFunctionWeight(TimeSpan weight) {
            return (double)weight.Ticks / (double)TotalWeight.Ticks;
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

                foreach (var sample in cpuSamplingData.Samples) {
                    if (sample.IsExecutingDeferredProcedureCall == true || 
                        sample.IsExecutingInterruptServicingRoutine == true) {
                        continue;
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

                                var rva = frame.Address;
                                var functionRVA = symbol.AddressRange.BaseAddress;
                                var offset = rva.Value - functionRVA.Value;
                                profile.AddInstructionSample(offset, sample.Weight.TimeSpan);
                                profile.AddLineSample(symbol.SourceLineNumber, sample.Weight.TimeSpan);
                                profile.Weight += sample.Weight.TimeSpan;
                            }

                            TotalWeight += sample.Weight.TimeSpan;
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