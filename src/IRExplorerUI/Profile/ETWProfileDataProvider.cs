using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Windows.EventTracing;
using Microsoft.Windows.EventTracing.Cpu;
using Microsoft.Windows.EventTracing.Symbols;
using IRExplorerCore;
using IRExplorerCore.IR;
using IRExplorerCore.IR.Tags;

namespace IRExplorerUI.Profile {
    //? TODO: Add interface
    public class ETWProfileDataProvider {
        private IRTextSummary summary_;
        private IRTextSectionLoader loader_;

        public ETWProfileDataProvider(IRTextSummary summary, IRTextSectionLoader docLoader) {
            summary_ = summary;
            loader_ = docLoader;
            FunctionProfiles = new Dictionary<IRTextFunction, FunctionProfileData>();
            TotalWeight = TimeSpan.Zero;
        }
        
        public Dictionary<IRTextFunction, FunctionProfileData> FunctionProfiles { get; }
        public TimeSpan TotalWeight { get; set; }

        public double ScaleFunctionWeight(TimeSpan weight) {
            return (double)weight.Ticks / (double)TotalWeight.Ticks;
        }

        public async Task<bool> LoadTrace(string tracePath, string imageName, string symbolPath,
                                        bool markInlinedFunctions) {
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
                                var profile = GetOrCreateFunctionProfile(textFunction, symbol.SourceFileName);
                                var rva = frame.Address;
                                var functionRVA = symbol.AddressRange.BaseAddress;
                                var offset = rva.Value - functionRVA.Value;
                                profile.AddInstructionSample(offset, sample.Weight.TimeSpan);
                                profile.AddLineSample(symbol.SourceLineNumber, sample.Weight.TimeSpan);
                                profile.Weight += sample.Weight.TimeSpan;

                                if (markInlinedFunctions && textFunction.Sections.Count > 0) {
                                    // Load current function.
                                    var result = loader_.LoadSection(textFunction.Sections[^1]);
                                    var metadataTag = result.Function.GetTag<AddressMetadataTag>();
                                    bool hasInstrOffsetMetadata = metadataTag != null && metadataTag.OffsetToElementMap.Count > 0;

                                    // Try to find instr. referenced by RVA, then go over all inlinees.
                                    if (hasInstrOffsetMetadata &&
                                        metadataTag.OffsetToElementMap.TryGetValue(offset, out var rvaInstr)) {
                                        var lineInfo = rvaInstr.GetTag<SourceLocationTag>();

                                        if (lineInfo != null && lineInfo.HasInlinees) {
                                            // For each inlinee, add the sample to its line.
                                            foreach (var inlinee in lineInfo.Inlinees) {
                                                var inlineeTextFunc = summary_.FindFunction(inlinee.Function);

                                                if (inlineeTextFunc != null) {
                                                    //? TODO: Inlinee can be in another source file
                                                    var inlineeProfile = GetOrCreateFunctionProfile(inlineeTextFunc, symbol.SourceFileName);
                                                    inlineeProfile.AddLineSample(inlinee.Line, sample.Weight.TimeSpan);
                                                    inlineeProfile.Weight += sample.Weight.TimeSpan;
                                                }
                                            }
                                        }
                                    }
                                }
                            }

                            TotalWeight += sample.Weight.TimeSpan;
                            break;
                        }
                        else {
                            Trace.WriteLine("Could not find debug info for\n");
                            Trace.WriteLine($"   image: {frame.Image.FileName}");
                            Trace.Flush();
                        }
                    }
                }

                if (markInlinedFunctions) {
                    loader_.ResetCache();
                }
            }
            catch (Exception ex) {
                Trace.WriteLine($"Exception loading profile: {ex.Message}");
                Trace.Flush();
                return false;
            }

            return true;
        }

        private FunctionProfileData GetOrCreateFunctionProfile(IRTextFunction textFunction, string sourceFile) {
            if (!FunctionProfiles.TryGetValue(textFunction, out var profile)) {
                profile = new FunctionProfileData(sourceFile);
                FunctionProfiles[textFunction] = profile;
            }

            return profile;
        }
    }
}