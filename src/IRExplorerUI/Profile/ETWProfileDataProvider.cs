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
using System.Linq;
using System.Threading;
using IRExplorerUI.Compilers;

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

        private ICompilerInfoProvider compilerInfo_;
        private IRTextSummary summary_;
        private IRTextSectionLoader loader_;
        private ProfileData profileData_;
        private PdbParser pdbParser_;
        
        //? TODO: Workaround for crash that happens when the finalizers are run
        //? and the COM object is released after it looks as being destroyed.
        // T his will keep it alive during the entire process.
        private static ITraceProcessor trace; 

        public ETWProfileDataProvider(IRTextSummary summary, IRTextSectionLoader docLoader,
                                      ICompilerInfoProvider compilerInfo, string cvdumpPath) {
            summary_ = summary;
            loader_ = docLoader;
            compilerInfo_ = compilerInfo;
            pdbParser_ = new PdbParser(cvdumpPath);
            profileData_ = new ProfileData();
        }

        private new Dictionary<string, IRTextFunction> CreateDemangledNameMapping() {
            var map = new Dictionary<string, IRTextFunction>();

            foreach (var func in summary_.Functions) {
                var demangledName = compilerInfo_.NameProvider.DemangleFunctionName(func.Name,
                    FunctionNameDemanglingOptions.OnlyName |
                    FunctionNameDemanglingOptions.NoReturnType |
                    FunctionNameDemanglingOptions.NoSpecialKeywords);
                map[demangledName] = func;
            }

            return map;
        }

        private (Dictionary<long, IRTextFunction>, Dictionary<long, string>)
            BuildAddressFunctionMap(string symbolPath) {
            var addressFuncMap = new Dictionary<long, IRTextFunction>();
            var externalsFuncMap = new Dictionary<long, string>();
            
            foreach(var (funcName, address) in pdbParser_.Parse(symbolPath)) {
                var func = summary_.FindFunction(funcName);
                
                if (func != null) {
                    addressFuncMap[address] = func;
                }
                else {
                    externalsFuncMap[address] = funcName;
                }
            }

            return (addressFuncMap, externalsFuncMap);
        }
        
        public async Task<ProfileData> 
            LoadTrace(string tracePath, string imageName, string symbolPath,
                      bool markInlinedFunctions, ProfileLoadProgressHandler progressCallback,
                      CancelableTask cancelableTask) {
            try {
                loader_.ResetCache();

                // Extract just the file name.
                imageName = Path.GetFileNameWithoutExtension(imageName);
               
                // The entire ETW processing must be done on the same thread.
                bool result = await Task.Run(async () => {

                    var debugFile = @"E:\spec\spec2017\benchspec\leela\default\build_base_msvc-diff.0000\leela_s.pdb";
                    using var debugInfo = new DebugInfoProvider();
                    bool hasDebugInfo = debugInfo.LoadDebugInfo(debugFile);

                    var unmangledMap = BuildUnmangledFunctionNameMap();

                    // Start getting the function address data while the trace is loading.
                    progressCallback?.Invoke(new ProfileLoadProgress(ProfileLoadStage.TraceLoading));
                    var debugTask = Task.Run(() => BuildAddressFunctionMap(symbolPath));

                    // Load the trace.
                    var settings = new TraceProcessorSettings {
                        AllowLostEvents = true
                    };

                    trace = TraceProcessor.Create(tracePath, settings);
                    IPendingResult<ISymbolDataSource> pendingSymbolData = trace.UseSymbols();
                    IPendingResult<ICpuSampleDataSource> pendingCpuSamplingData = trace.UseCpuSamplingData();

                    trace.Process(new ProcessProgressTracker(progressCallback));

                    if (cancelableTask != null && cancelableTask.IsCanceled) {
                        return false;
                    }

                    // Load symbols.
                    ISymbolDataSource symbolData = pendingSymbolData.Result;
                    ICpuSampleDataSource cpuSamplingData = pendingCpuSamplingData.Result;
                    await symbolData.LoadSymbolsAsync(SymCachePath.Automatic, new RawSymbolPath(new FileInfo(symbolPath).DirectoryName),
                        new SymbolProgressTracker(progressCallback));

                    if (cancelableTask != null && cancelableTask.IsCanceled) {
                        return false;
                    }

                    // Wait for the function address data to be ready.
                    var (addressFuncMap, externalsFuncMap) = await debugTask;

                    if (addressFuncMap.Count == 0) {
                        return false; // If we have no functions from the pdb, there's nothing more to do.
                    }

                    // Process the samples.
                    int index = 0;
                    var totalSamples = cpuSamplingData.Samples.Count;
                    var prevFuncts = new Dictionary<string, List<IRTextFunction>>();
                    var demangledFuncNames = new Lazy<Dictionary<string, IRTextFunction>>(CreateDemangledNameMapping, LazyThreadSafetyMode.None);

                    Dictionary<string, IRTextFunction> externalFuncNames = new Dictionary<string, IRTextFunction>();

                    var stackFuncts = new HashSet<IRTextFunction>();
                    var stackModules = new HashSet<string>();
                    
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
                        
                        var sampleWeight = sample.Weight.TimeSpan;
                        var moduleName = sample.Process.ImageName;
                        
                        // Consider only the profiled executable.
                        if (!moduleName.Contains(imageName, StringComparison.OrdinalIgnoreCase)) {
                            continue;
                        }

                        profileData_.TotalWeight += sampleWeight;

                        if (sample.Stack == null) {
                            continue;
                        }
                        
                        // Count time in the profile image.
                        profileData_.ProfileWeight += sampleWeight;
                        IRTextFunction prevStackFunc = null;
                        FunctionProfileData prevStackProfile = null;

                        stackFuncts.Clear();
                        stackModules.Clear();
                        
                        var stackFrames = sample.Stack.Frames;
                        bool isTopFrame = true;
                        
                        foreach (var frame in stackFrames) {
                            // Count exclusive time for each module in the executable. 
                            if (isTopFrame &&
                                frame.Symbol?.Image?.FileName != null &&
                                stackModules.Add(frame.Symbol.Image.FileName)) {
                                profileData_.AddModuleSample(frame.Symbol.Image.FileName, sampleWeight);
                            }

                            // Ignore samples targeting modules loaded in the executable that are not it.
                            if (frame.Image == null) {
                                prevStackFunc = null;
                                prevStackProfile = null;
                                isTopFrame = false;
                                continue;
                            }
                            
                            if (!frame.Image.FileName.Contains(imageName, StringComparison.OrdinalIgnoreCase)) {
                                prevStackFunc = null;
                                isTopFrame = false;
                                continue;
                            }
                            
                            var symbol = frame.Symbol;
                            
                            if (symbol == null) {
                                Trace.WriteLine($"Could not find debug info for image: {frame.Image.FileName}");
                                prevStackFunc = null;
                                isTopFrame = false;
                                continue;
                            }
                            
                            // Search for a function with the matching demangled name.
                            var funcName = symbol.FunctionName;
                            var funcAddress = symbol.AddressRange.BaseAddress.Value -
                                              symbol.Image.AddressRange.BaseAddress.Value;
                            funcAddress -= 4096; // An extra page size is always added...

                            // Try to use the precise address -> function mapping from cvdump.
                            if (!addressFuncMap.TryGetValue(funcAddress, out var textFunction)) {
                                if (!demangledFuncNames.Value.TryGetValue(funcName, out textFunction)) {
                                    //? TODO: For external functs that don't have IR, record the timing somehow
                                    //? - maybe create a dummy func for it
                                    // Check if it's a known external function.
                                    if (!externalsFuncMap.TryGetValue(funcAddress, out var externalFuncName)) {
                                        prevStackFunc = null;
                                        isTopFrame = false;
                                        continue;
                                    }

                                    if (!externalFuncNames.TryGetValue(externalFuncName, out textFunction)) {
                                        // Create a dummy external function that will have no sections. 
                                        textFunction = new IRTextFunction(externalFuncName);
                                        summary_.AddFunction(textFunction);
                                        externalFuncNames[externalFuncName] = textFunction;
                                    }
                                }
                            }

                            var profile = profileData_.GetOrCreateFunctionProfile(textFunction, symbol.SourceFileName);
                            var rva = frame.Address;
                            var functionRVA = symbol.AddressRange.BaseAddress;
                            var offset = rva.Value - functionRVA.Value;

                            // Don't count the inclusive time for recursive functions multiple times.
                            if (stackFuncts.Add(textFunction)) {
                                profile.AddInstructionSample(offset, sampleWeight);
                                profile.AddLineSample(symbol.SourceLineNumber, sampleWeight);
                                profile.Weight += sampleWeight;

                                // Add the previous stack frame function as a child
                                // and current frame as its parent.
                                if (prevStackFunc != null) {
                                    profile.AddChildSample(prevStackFunc, sampleWeight);
                                }
                            }

                            if (prevStackFunc != null) {
                                prevStackProfile.AddCallerSample(textFunction, sampleWeight);
                            }
                            
                            // Count the exclusive time for the top frame function.
                            if (isTopFrame) {
                                profile.ExclusiveWeight += sampleWeight;
                            }
                            
                            //? TODO: Enable after more testing
                            markInlinedFunctions = true;
                            
                            if (markInlinedFunctions && textFunction.Sections.Count > 0) {
                                // Load current function.
                                var result = loader_.LoadSection(textFunction.Sections[^1]);
                                var metadataTag = result.Function.GetTag<AssemblyMetadataTag>();
                                bool hasInstrOffsetMetadata =
                                    metadataTag != null && metadataTag.OffsetToElementMap.Count > 0;

                                if (hasInstrOffsetMetadata && !result.IsCached) {
                                    if(hasDebugInfo) {
                                        debugInfo.AnnotateSourceLocations(result.Function, textFunction.Name);
                                    }
                                }

                                // Try to find instr. referenced by RVA, then go over all inlinees.
                                if (hasInstrOffsetMetadata &&
                                    metadataTag.OffsetToElementMap.TryGetValue(offset, out var rvaInstr)) {
                                    var lineInfo = rvaInstr.GetTag<SourceLocationTag>();

                                    if (lineInfo != null && lineInfo.HasInlinees) {
                                        // For each inlinee, add the sample to its line.
                                        foreach (var inlinee in lineInfo.Inlinees) {
                                            if(!unmangledMap.TryGetValue(inlinee.Function, out var inlineeTextFunc)) { 
                                                inlineeTextFunc = new IRTextFunction(inlinee.Function);
                                                summary_.AddFunction(inlineeTextFunc);
                                                unmangledMap[inlinee.Function] = inlineeTextFunc;
                                            }

                                            var inlineeProfile = profileData_.GetOrCreateFunctionProfile(
                                                                    inlineeTextFunc, inlinee.FilePath);
                                            inlineeProfile.AddLineSample(inlinee.Line, sampleWeight);
                                            inlineeProfile.Weight += sampleWeight;
                                            inlineeProfile.ExclusiveWeight += sampleWeight;
                                        }
                                    }
                                }
                            }

                            isTopFrame = false;
                            prevStackFunc = textFunction;
                            prevStackProfile = profile;
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
                Trace.TraceError($"Exception loading profile: {ex.Message}");
                return null;
            }
        }

        private Dictionary<string, IRTextFunction> BuildUnmangledFunctionNameMap() {
            var map = new Dictionary<string, IRTextFunction>(summary_.Functions.Count);

            foreach (var function in summary_.Functions) {
                var unmangledName = DebugInfoProvider.DemangleFunctionName(function.Name);
                map[unmangledName] = function;
            }

            return map;
        }
    }
}