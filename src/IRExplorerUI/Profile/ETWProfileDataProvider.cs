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
using Microsoft.Windows.EventTracing.Processes;
using System.Text;

namespace IRExplorerUI.Profile {
    public class ETWProfileDataProvider : IProfileDataProvider, IDisposable {
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

        //? TODO
        // - on new image
        // 

        class ModuleInfo {
            //? Session needed for loader
            private string DebugFilePath;
            private string BinarFilePatth;
            private IRTextSummary summary_;
            private PdbParser pdbParser_;
            private IDebugInfoProvider debugInfo_;
            private Dictionary<string, IRTextFunction> unmangledFuncNamesMap_;
        }

       

        private ICompilerInfoProvider compilerInfo_;
        private IRTextSummary summary_;
        private IRTextSectionLoader loader_;
        private ProfileData profileData_;
        private PdbParser pdbParser_;
        private IDebugInfoProvider debugInfo_;
        private Dictionary<string, IRTextFunction> unmangledFuncNamesMap_;

        static List<ProfileSample> temp_;

        //? TODO: Workaround for crash that happens when the finalizers are run
        //? and the COM object is released after it looks as being destroyed.
        // This will keep it alive during the entire process.
        private static ITraceProcessor trace; 

        public ETWProfileDataProvider(IRTextSummary summary, IRTextSectionLoader docLoader,
                                      ICompilerInfoProvider compilerInfo, string cvdumpPath) {
            summary_ = summary;
            loader_ = docLoader;
            compilerInfo_ = compilerInfo;
            pdbParser_ = new PdbParser(cvdumpPath);
            profileData_ = new ProfileData();
            events_ = new List<PerformanceCounterEvent>();
        }

        public ProfileData
        LoadTrace(string tracePath, string imageName, string symbolPath,
              bool markInlinedFunctions, ProfileLoadProgressHandler progressCallback,
              CancelableTask cancelableTask) {
            return LoadTraceAsync(tracePath, imageName, symbolPath, 
                                  markInlinedFunctions, progressCallback, cancelableTask).Result;
        }

        /*

        get
        {
            if ((eventRecord->EventHeader.Flags & 0x40) == 0)
            {
                return 4;
            }
            return 8;
        }

        public int ProfileSource => GetInt16At(HostOffset(8, 1));

        protected internal int HostOffset(int offset, int numPointers)
	        return offset + (PointerSize - 4) * numPointers;
        }
    }
        */

        private unsafe static long ReadInt64(ReadOnlySpan<byte> data, int offset = 0) {
            fixed (byte* dataPtr = data) {
                return *((long*)(dataPtr + offset));
            }
        }

        private unsafe static short ReadInt16(ReadOnlySpan<byte> data, int offset = 0) {
            fixed (byte* dataPtr = data) {
                return *((short*)(dataPtr + offset));
            }
        }

        private unsafe static int ReadInt32(ReadOnlySpan<byte> data, int offset = 0) {
            fixed (byte* dataPtr = data) {
                return *((int*)(dataPtr + offset));
            }
        }

        private unsafe static string ReadWideString(ReadOnlySpan<byte> data, int offset = 0) {
            fixed (byte* dataPtr = data) {
                var sb = new StringBuilder();

                while(offset < data.Length - 1) {
                    byte first = dataPtr[offset];
                    byte second = dataPtr[offset + 1];

                    if(first == 0 && second == 0) {
                        break; // Found string null terminator.
                    }

                    sb.Append((char)((short)first | ((short)second << 8)));
                    offset += 2;
                }

                return sb.ToString();
            }

            return null;
        }

        private class ProcessInfoCache {
            public class ImageInfo {
                public IImage Image { get; set; }
                public long AddressStart { get; set; }
                public long AddressEnd { get; set; }
            }

            private Dictionary<int, List<ImageInfo>> processMap_;
            Dictionary<long, IImage> ipImageCache_;

            public ProcessInfoCache(IReadOnlyList<IProcess> procs) {
                processMap_ = new Dictionary<int, List<ImageInfo>>(procs.Count);
                ipImageCache_ = new Dictionary<long, IImage>();

                var imageSet = new Dictionary<string, ImageInfo>();

                foreach (var proc in procs) {
                   // Trace.WriteLine($"PROC {proc.ImageName}");

                    var imageList = new List<ImageInfo>(proc.Images.Count);
                    processMap_[proc.Id] = imageList;

                    foreach (var image in proc.Images) {
                        var item = new ImageInfo() {
                            Image = image,
                            AddressStart = image.AddressRange.BaseAddress.Value,
                            AddressEnd = image.AddressRange.BaseAddress.Value +
                                                    image.AddressRange.Size.Bytes
                        };
                        imageList.Add(item);

                        //Trace.WriteLine($"Image {image.FileName}: {imageList[^1].AddressStart} - {imageList[^1].AddressEnd}");

                        if (!string.IsNullOrEmpty(image.FileName)) {
                            if (imageSet.TryGetValue(image.FileName, out var other)) {
                                if (other.AddressStart != item.AddressStart ||
                                    other.AddressEnd != item.AddressEnd) {
                                    Trace.WriteLine("MISMATCH!");
                                }
                            }
                            imageSet[image.FileName] = item;
                        }
                    }
                }
            }

            public IImage FindImageForIP(long ip, int processId = -1) {
                if(processId != -1) {
                    // todo
                }

                if(ipImageCache_.TryGetValue(ip, out var cachedImage)) {
                    return cachedImage;
                }

                //? TODO: Needs a range tree
                foreach(var imageList in processMap_.Values) {
                    foreach(var image in imageList) {
                        if (ip >= image.AddressStart &&
                            ip < image.AddressEnd) {
                            ipImageCache_[ip] = image.Image;
                            return image.Image;
                        }
                    }
                }

                return null;
            }
        }

        private class PerformanceCounterEvent {
            public long IP;
            public long RVA;
            public int ProcessId; //? threadId
            public short ProfilerSource;
        }

        private PerformanceCounterInfo ReadPerformanceCounterInfo(ReadOnlySpan<byte> data) {
            // counter id : 4
            // frequency : 4 min
            // frequency: 4 max
            // name : 12+
            var counter = new PerformanceCounterInfo();
            counter.Id = ReadInt32(data, 0);
            counter.Frequency = ReadInt32(data, 4);
            // counter.Frequency = ReadInt32(data, 8);
            counter.Name = ReadWideString(data, 12);
            return counter;
        }

        private List<PerformanceCounterEvent> events_;
        
        private void CollectPerformanceCounterEvents(EventContext e) {
            if (e.Event.Id == 47) {
                if (e.Event.Data.Length > 0) {
                    events_.Add(new PerformanceCounterEvent() {
                        IP = ReadInt64(e.Event.Data), 
                        ProfilerSource = ReadInt16(e.Event.Data, 12), 
                        ProcessId = e.Event.ProcessId.HasValue ? e.Event.ProcessId.Value : -1
                    });
                }
            }
            else if (e.Event.Id == 0x49) {
                if (e.Event.Data.Length > 0) {
                    var counter = ReadPerformanceCounterInfo(e.Event.Data);
                    profileData_.RegisterPerformanceCounter(counter);
                }
            }
        }
        
        public async Task<ProfileData> 
            LoadTraceAsync(string tracePath, string imageName, string symbolPath,
                      bool markInlinedFunctions, ProfileLoadProgressHandler progressCallback,
                      CancelableTask cancelableTask) {
            try {
                // Extract just the file name.
                imageName = Path.GetFileNameWithoutExtension(imageName);
               
                // The entire ETW processing must be done on the same thread.
                bool result = await Task.Run(async () => {
                    // Start getting the function address data while the trace is loading.
                    progressCallback?.Invoke(new ProfileLoadProgress(ProfileLoadStage.TraceLoading));
                    var funcAddressTask = Task.Run(() => BuildAddressFunctionMap(symbolPath));
                    var debugInfoTask = Task.Run(() => SetupDebugInfo(symbolPath));

                    // Load the trace.
                    var settings = new TraceProcessorSettings {
                        AllowLostEvents = true
                    };

                    trace = TraceProcessor.Create(tracePath, settings);
                    var pendingSymbolData = trace.UseSymbols();
                    var pendingCpuSamplingData = trace.UseCpuSamplingData();
                    var procs = trace.UseProcesses();

                    Trace.WriteLine($"Start load at {DateTime.Now}");
                    Trace.Flush();

                    trace.Use(CollectPerformanceCounterEvents);
                    trace.Process(new ProcessProgressTracker(progressCallback));
                    
                    Trace.WriteLine($"After process load at {DateTime.Now}");
                    Trace.Flush();

                    var sw = Stopwatch.StartNew();
                    var allProcs = procs.Result.Processes;
                    var ipImageCache = new Dictionary<long, IImage>();
                    var binaryCounters = new List<PerformanceCounterEvent>();

                    IImage mainImage = null;
                    var procCache = new ProcessInfoCache(allProcs);

                    for(int i = 0; i < events_.Count; i++) {
                        var counter = events_[i];
                        var image = procCache.FindImageForIP(counter.IP, counter.ProcessId);

                        if (image != null) {
                            profileData_.AddModuleCounter(image.FileName, counter.ProfilerSource, 1);

                            // Filter counters for current binary.
                            if (image.FileName.Contains(imageName)) {
                                counter.RVA = counter.IP - image.AddressRange.BaseAddress.Value;
                                binaryCounters.Add(counter);
                                mainImage = image;
                            }
                        }
                    }

                    Trace.WriteLine($"Done PMC in {sw.Elapsed} at {DateTime.Now}");
                    Trace.Flush();


                    if (cancelableTask != null && cancelableTask.IsCanceled) {
                        return false;
                    }

                    var cpuSamplingData = pendingCpuSamplingData.Result;

                    // Load symbols.
                    try {
                        var symbolData = pendingSymbolData.Result;
                        await symbolData.LoadSymbolsAsync(SymCachePath.Automatic,
                            new RawSymbolPath(new FileInfo(symbolPath).DirectoryName),
                            new SymbolProgressTracker(progressCallback));
                    }
                    catch (Exception ex) {
                        Trace.TraceWarning($"Failed to load symbols: {ex.Message}");
                    }

                    if (cancelableTask != null && cancelableTask.IsCanceled) {
                        return false;
                    }

                    // Wait for the function address data to be ready.
                    var (addressFuncMap, externalsFuncMap) = await funcAddressTask;
                    bool hasDebugInfo = await debugInfoTask;

                    if (addressFuncMap.Count == 0) {
                        Trace.TraceWarning($"Failed to get debug info");
                        //return false; // If we have no functions from the pdb, there's nothing more to do.
                    }


                    Trace.WriteLine($"Start process PMC {binaryCounters.Count} at {DateTime.Now}");
                    Trace.Flush();

                    sw.Restart();

                    // Process the samples.
                    int index = 0;

                    var demangledFuncNames = new Lazy<Dictionary<string, IRTextFunction>>(this.CreateDemangledNameMapping, LazyThreadSafetyMode.None);

                    var externalFuncNames = new Dictionary<string, IRTextFunction>();

                    var stackFuncts = new HashSet<IRTextFunction>();
                    var stackModules = new HashSet<string>();
                    int hits = 0;

                    foreach (var counter in binaryCounters) {
                        var (funcName, funcRVA) = debugInfo_.FindFunctionByRVA(counter.RVA);

                        if (funcName == null) {
                            continue;
                        }

                        hits++;

                        // Try to use the precise address -> function mapping from cvdump.
                        if (!addressFuncMap.TryGetValue(funcRVA, out var textFunction)) {
                            if (!demangledFuncNames.Value.TryGetValue(funcName, out textFunction)) {
                                continue;
                            }
                        }

                        if (textFunction != null) {
                            var funcOffset = counter.RVA - funcRVA;
                            var profile = profileData_.GetOrCreateFunctionProfile(textFunction, "");
                            profile.AddCounterSample(funcOffset, counter.ProfilerSource, 1);
                        }
                    }


                    Trace.WriteLine($"Done process PMC hits {hits} in {sw.Elapsed} at {DateTime.Now}");
                    Trace.Flush();


                    //? TODO: parallel

                    var samples = cpuSamplingData.Samples;
                    var totalSamples = samples.Count;

                    foreach (var sample in samples) {
                        if (sample.IsExecutingDeferredProcedureCall == true ||
                            sample.IsExecutingInterruptServicingRoutine == true) {
                            continue;
                        }

                        if (index % 10000 == 0) {
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
                                frame.Image?.FileName != null &&
                                stackModules.Add(frame.Image?.FileName)) {
                                profileData_.AddModuleSample(frame.Image?.FileName, sampleWeight);
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
                            var functionRVA = symbol.AddressRange.BaseAddress;
                            var offset = frame.Address.Value - functionRVA.Value;

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
                            
                            // Try to map the sample to all the inlined functions.
                            if (markInlinedFunctions && hasDebugInfo && 
                                textFunction.Sections.Count > 0) {
                                ProcessInlineeSample(sampleWeight, offset, textFunction);
                            }

                            isTopFrame = false;
                            prevStackFunc = textFunction;
                            prevStackProfile = profile;
                        }
                    }

                    //GC.Collect();
                    //GC.WaitForPendingFinalizers();
                    
                    //long memory2 = GC.GetTotalMemory(true);

                    //Trace.WriteLine($"Diff: {(memory2 - memory) / 1024}, kb");
                    //Trace.Flush();

                    return true;
                });

                trace.Dispose();
                //trace = null;
                return result ? profileData_ : null;
            }
            catch (Exception ex) {
                Trace.TraceError($"Exception loading profile: {ex.Message}");
                return null;
            }
        }

        private void ProcessInlineeSample(TimeSpan sampleWeight, long sampleOffset, 
                                          IRTextFunction textFunction) {
            // Load current function.
            var result = loader_.LoadSection(textFunction.Sections[^1]);
            var metadataTag = result.Function.GetTag<AssemblyMetadataTag>();
            bool hasInstrOffsetMetadata =
                metadataTag != null && metadataTag.OffsetToElementMap.Count > 0;

            if (hasInstrOffsetMetadata && !result.IsCached) {
                // Add source location info only once, can be slow.
                debugInfo_.AnnotateSourceLocations(result.Function, textFunction.Name);
            }

            // Try to find instr. referenced by RVA, then go over all inlinees.
            if (!hasInstrOffsetMetadata ||
                !metadataTag.OffsetToElementMap.TryGetValue(sampleOffset, out var rvaInstr)) {
                return;
            }

            var lineInfo = rvaInstr.GetTag<SourceLocationTag>();

            if (lineInfo == null || !lineInfo.HasInlinees) {
                return;
            }

            // For each inlinee, add the sample to its line.
            foreach (var inlinee in lineInfo.Inlinees) {
                if (!unmangledFuncNamesMap_.TryGetValue(inlinee.Function, out var inlineeTextFunc)) {
                    // The function may have been inlined at all call sites
                    // and not be found in the binary, make a dummy func. for it.
                    inlineeTextFunc = new IRTextFunction(inlinee.Function);
                    summary_.AddFunction(inlineeTextFunc);
                    unmangledFuncNamesMap_[inlinee.Function] = inlineeTextFunc;
                }

                var inlineeProfile = profileData_.GetOrCreateFunctionProfile(
                                        inlineeTextFunc, inlinee.FilePath);
                inlineeProfile.AddLineSample(inlinee.Line, sampleWeight);
                inlineeProfile.Weight += sampleWeight;
            }
        }

        //? Merge with BuildUnmangledFunctionNameMap
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

            foreach (var (funcName, address) in pdbParser_.Parse(symbolPath)) {
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

        private Dictionary<string, IRTextFunction> BuildUnmangledFunctionNameMap() {
            var map = new Dictionary<string, IRTextFunction>(summary_.Functions.Count);

            foreach (var function in summary_.Functions) {
                var unmangledName = PDBDebugInfoProvider.DemangleFunctionName(function.Name);
                map[unmangledName] = function;
            }

            return map;
        }

        private bool SetupDebugInfo(string symbolPath) {
            unmangledFuncNamesMap_ = BuildUnmangledFunctionNameMap();
            debugInfo_ = new PDBDebugInfoProvider();
            return debugInfo_.LoadDebugInfo(symbolPath);
        }

        public void Dispose() {
            debugInfo_?.Dispose();
        }
    }
}