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
    public class ETWProfileDataProvider : IProfileDataProvider {
        class ProcessProgressTracker : IProgress<TraceProcessingProgress> {
            private ProfileLoadProgressHandler callback_;

            public ProcessProgressTracker(ProfileLoadProgressHandler callback) {
                callback_ = callback;
            }

            public void Report(TraceProcessingProgress value) {
                callback_?.Invoke(new ProfileLoadProgress(ProfileLoadStage.TraceLoading) {
                    Total = value.TotalPasses,
                    Current = value.CurrentPass
                });
            }
        }

        class SymbolProgressTracker : IProgress<SymbolLoadingProgress> {
            private ProfileLoadProgressHandler callback_;

            public SymbolProgressTracker(ProfileLoadProgressHandler callback) {
                callback_ = callback;
            }

            public void Report(SymbolLoadingProgress value) {
                callback_?.Invoke(new ProfileLoadProgress(ProfileLoadStage.SymbolLoading) {
                    Total = value.ImagesTotal,
                    Current = value.ImagesProcessed
                });
            }
        }

        private IRTextSummary summary_;
        private IRTextSectionLoader loader_;
        private ProfileData profileData_;

        public ETWProfileDataProvider(IRTextSummary summary, IRTextSectionLoader docLoader) {
            summary_ = summary;
            loader_ = docLoader;
            profileData_ = new ProfileData();
        }

        public async Task<ProfileData> 
            LoadTrace(string tracePath, string imageName, string symbolPath,
                     bool markInlinedFunctions, ProfileLoadProgressHandler progressCallback,
                     CancelableTask cancelableTask) {
            try {
                // Extract just the file name.
                imageName = Path.GetFileNameWithoutExtension(imageName);
                
                
                // The entire ETW processing must be done on the same thread.
                bool result = await Task.Run(async () => {
                    //var settings = new TraceProcessorSettings {
                    //    AllowLostEvents = true
                    //};

                    using var trace = TraceProcessor.Create(tracePath);
                    IPendingResult<ISymbolDataSource> pendingSymbolData = trace.UseSymbols();
                    IPendingResult<ICpuSampleDataSource> pendingCpuSamplingData = trace.UseCpuSamplingData();

                    trace.Process(new ProcessProgressTracker(progressCallback));

                    if (cancelableTask != null && cancelableTask.IsCanceled) {
                        return false;
                    }

                    // Load symbols.
                    ISymbolDataSource symbolData = pendingSymbolData.Result;
                    ICpuSampleDataSource cpuSamplingData = pendingCpuSamplingData.Result;
                    await symbolData.LoadSymbolsAsync(SymCachePath.Automatic, new RawSymbolPath(symbolPath),
                        new SymbolProgressTracker(progressCallback));

                    if (cancelableTask != null && cancelableTask.IsCanceled) {
                        return false;
                    }

                    int index = 0;
                    var totalSamples = cpuSamplingData.Samples.Count;
                    var prevFuncts = new Dictionary<string, List<IRTextFunction>>();

                    //? TODO: parallel
                    foreach (var sample in cpuSamplingData.Samples) {
                        if (sample.IsExecutingDeferredProcedureCall == true ||
                            sample.IsExecutingInterruptServicingRoutine == true) {
                            continue;
                        }

                        if (index % 1000 == 0) {
                            if (cancelableTask != null && cancelableTask.IsCanceled) {
                                return false;
                            }

                            progressCallback?.Invoke(new ProfileLoadProgress(ProfileLoadStage.TraceProcessing) {
                                Total = totalSamples,
                                Current = index
                            });
                        }

                        index++;

                        if (!sample.Process.ImageName.Contains(imageName, StringComparison.OrdinalIgnoreCase) ||
                            sample.Stack == null) {
                            continue;
                        }

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
                                var funcName = symbol.FunctionName;

                                if (funcName.Contains("::")) {
                                    //? TODO: Hacky way of dealing with manged C++ names
                                    var parts = funcName.Split("::", StringSplitOptions.RemoveEmptyEntries);

                                    if (!prevFuncts.TryGetValue(funcName, out functs)) {
                                        functs = summary_.FindAllFunctions(parts);
                                        prevFuncts[funcName] = functs;
                                    }
                                }
                                else {
                                    // Avoid linear search
                                    if (!prevFuncts.TryGetValue(funcName, out functs)) {
                                        functs = summary_.FindAllFunctions(funcName);
                                        prevFuncts[funcName] = functs;
                                    }
                                }

                                var sampleWeight = sample.Weight.TimeSpan;

                                foreach (var textFunction in functs) {
                                    var profile = profileData_.GetOrCreateFunctionProfile(textFunction, symbol.SourceFileName);
                                    var rva = frame.Address;
                                    var functionRVA = symbol.AddressRange.BaseAddress;
                                    var offset = rva.Value - functionRVA.Value;
                                    profile.AddInstructionSample(offset, sampleWeight);
                                    profile.AddLineSample(symbol.SourceLineNumber, sampleWeight);
                                    profile.Weight += sampleWeight;

                                    if (markInlinedFunctions && textFunction.Sections.Count > 0) {
                                        // Load current function.
                                        var result = loader_.LoadSection(textFunction.Sections[^1]);
                                        var metadataTag = result.Function.GetTag<AddressMetadataTag>();
                                        bool hasInstrOffsetMetadata =
                                            metadataTag != null && metadataTag.OffsetToElementMap.Count > 0;

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
                                                        var inlineeProfile = profileData_.GetOrCreateFunctionProfile(inlineeTextFunc,
                                                            symbol.SourceFileName);
                                                        inlineeProfile.AddLineSample(inlinee.Line,
                                                            sampleWeight);
                                                        inlineeProfile.Weight += sampleWeight;
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }

                                profileData_.TotalWeight += sampleWeight;
                                break;
                            }
                            else {
                                Trace.WriteLine($"Could not find debug info for image: {frame.Image.FileName}");
                            }
                        }
                    }

                    return true;
                });

                // Free memory of parsed functions that may not be loaded again.
                if (markInlinedFunctions) {
                    loader_.ResetCache();
                }

                return result ? profileData_ : null;
            }
            catch (Exception ex) {
                Trace.WriteLine($"Exception loading profile: {ex.Message}");
                return null;
            }
        }
    }
}