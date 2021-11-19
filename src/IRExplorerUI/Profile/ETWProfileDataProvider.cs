﻿#define USE_STREAMING

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Windows.EventTracing;
using Microsoft.Windows.EventTracing.Cpu;
using Microsoft.Windows.EventTracing.Symbols;
using IRExplorerCore;
using IRExplorerCore.IR;
using IRExplorerCore.IR.Tags;
using System.Linq;
using System.Threading;
using Microsoft.Windows.EventTracing.Processes;
using System.Text;
using System.Collections;
using ProtoBuf;

namespace IRExplorerUI.Profile {
    public partial class ETWProfileDataProvider : IProfileDataProvider, IDisposable {
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


        private ProfileDataProviderOptions options_;
        private ISession session_;
        private ICompilerInfoProvider compilerInfo_;
        private IRTextSummary mainSummary_;
        private ModuleInfo mainModule_;
        private ProfileData profileData_;

        //private PdbParser pdbParser_;
        //private IDebugInfoProvider DebugInfo;
        //private Dictionary<string, IRTextFunction> unmangledFuncNamesMap_;

        static List<ProfileSample> temp_;

        //? TODO: Workaround for crash that happens when the finalizers are run
        //? and the COM object is released after it looks as being destroyed.
        // This will keep it alive during the entire process.
        private static ITraceProcessor trace; 

        public ETWProfileDataProvider(IRTextSummary summary, ISession session) {
            mainSummary_ = summary;
            session_ = session;
            profileData_ = new ProfileData();

            //events_ = new List<PerformanceCounterEvent>();
            events_ = new ChunkedList<PerformanceCounterEvent>();
        }

        public ProfileData LoadTrace(string tracePath, string imageName, 
              ProfileDataProviderOptions options, ProfileLoadProgressHandler progressCallback,
              CancelableTask cancelableTask) {
            return LoadTraceAsync(tracePath, imageName, options, progressCallback, cancelableTask).Result;
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
            private Dictionary<int, int> threadProcessMap_; // threadId -> processId
            private Dictionary<string, int> processNameMap_;
            private Dictionary<int, Dictionary<long, IImage>> ipImageCache_; // processId -> {IP -> image}


            public ProcessInfoCache(IReadOnlyList<IProcess> procs, IReadOnlyList<IThread> threads) {
                processMap_ = new Dictionary<int, List<ImageInfo>>(procs.Count);
                processNameMap_ = new Dictionary<string, int>(procs.Count);
                threadProcessMap_ = new Dictionary<int, int>();
                ipImageCache_ = new Dictionary<int, Dictionary<long, IImage>>(procs.Count);

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
                        //Trace.WriteLine($"Image {image.FilePath}: {imageList[^1].AddressStart} - {imageList[^1].AddressEnd}");
                    }

                    var imageName = Utils.TryGetFileNameWithoutExtension(proc.ImageName);
                    processNameMap_[imageName] = proc.Id;
                }

                foreach (var thread in threads) {
                    if (!threadProcessMap_.TryAdd(thread.Id, thread.ProcessId)) {
                        //? TODO: A thread ID could end up being reused during the run,
                        //? HistoryDictionary used by etl lib for this doing lookup by {threadId, timestamp}
                    }
                }
            }

            public int FindProcess(string imageName) {
                if(processNameMap_.TryGetValue(imageName, out int id)) {
                    return id;
                }

                // Do another search using a substring, this is needed
                // for SPEC benchmarks because the runner gives a diff. name to the binary.
                foreach (var pair in processNameMap_) {
                    if (pair.Key.Contains(imageName)) {
                        return pair.Value;
                    }
                }

                return -1;
            }

            public IImage FindImageForIP(long ip, int threadId, int searchedProcessId) {
                if (!threadProcessMap_.TryGetValue(threadId, out int processId)) {
                    return null;
                }

                if (processId != searchedProcessId ||
                    !processMap_.TryGetValue(processId, out var imageList)) {
                    return null;
                }

                // Lookup into the IP -> image cache first.
                if (!ipImageCache_.TryGetValue(processId, out var imageCache)) {
                    imageCache = new Dictionary<long, IImage>();
                    ipImageCache_[processId] = imageCache;
                }

                if (imageCache.TryGetValue(ip, out var cachedImage)) {
                    return cachedImage;
                }

                //? TODO: Use a range tree or do a binary search
                foreach (var image in imageList) {
                    if (ip >= image.AddressStart &&
                        ip < image.AddressEnd) {
                        imageCache[ip] = image.Image;
                        return image.Image;
                    }
                }

                return null;
            }
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

        //? Use some chunked list to avoid reallocation and LOH
        private ChunkedList<PerformanceCounterEvent> events_;
        private long times_;
        private long memory_;
        
        
        private void CollectPerformanceCounterEvents(EventContext e) {
            /*
             *internal override unsafe void FixupData()
    {
      if (this.eventRecord->EventHeader.ThreadId == -1)
        this.eventRecord->EventHeader.ThreadId = this.GetInt32At(this.HostOffset(4, 1));
      if (this.eventRecord->EventHeader.ProcessId != -1)
        return;
      this.eventRecord->EventHeader.ProcessId = this.state.ThreadIDToProcessID(this.ThreadID, this.TimeStampQPC);
    }

    public override unsafe int ProcessID
    {
      get
      {
        int processId = this.eventRecord->EventHeader.ProcessId;
        if (processId == -1)
          processId = this.state.ThreadIDToProcessID(this.ThreadID, this.TimeStampQPC);
        return processId;
      }
    }



            if ((tracking & ParserTrackingOptions.ThreadToProcess) != 0 && (state.callBacksSet & ParserTrackingOptions.ThreadToProcess) == 0)
	{
		state.callBacksSet |= ParserTrackingOptions.ThreadToProcess;
		ThreadStartGroup += delegate(ThreadTraceData data)
		{
			state.threadIDtoProcessID.Add((ulong)data.ThreadID, 0L, data.ProcessID);
		};
		ThreadEndGroup += delegate(ThreadTraceData data)
		{
			int value2;
			if (source.IsRealTime)
			{
				state.threadIDtoProcessID.Remove((ulong)data.ThreadID);
			}
			else if (!state.threadIDtoProcessID.TryGetValue((ulong)data.ThreadID, data.TimeStampQPC, out value2))
			{
				if (state.threadIDtoProcessIDRundown == null)
				{
					state.threadIDtoProcessIDRundown = new HistoryDictionary<int>(100);
				}
				state.threadIDtoProcessIDRundown.Add((ulong)data.ThreadID, -data.TimeStampQPC, data.ProcessID);
			}
             *
             * --------------------------------
             *
             * 
// Microsoft.Diagnostics.Tracing.Parsers.KernelTraceEventParser
using System;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;

protected internal override void EnumerateTemplates(Func<string, string, EventFilterResponse> eventsToObserve, Action<TraceEvent> callback)
{
	if (s_templates == null)
	{
		s_templates = new TraceEvent[194]
		{
			new EventTraceHeaderTraceData(null, 65535, 0, "EventTrace", EventTraceTaskGuid, 0, "Header", ProviderGuid, ProviderName, null),
		};
	}
             */

            if (e.Event.Id == 47) {
                if (e.Event.Data.Length > 0) {
                    events_.Add(new PerformanceCounterEvent() {
                        Time = e.Event.Timestamp.Nanoseconds,
                        IP = ReadInt64(e.Event.Data), 
                        ProfilerSource = ReadInt16(e.Event.Data, 12), 
                        ProcessId = e.Event.ProcessId ?? -1,
                        ThreadId = e.Event.ThreadId ?? ReadInt32(e.Event.Data, 8),
                    });

                    //if (times_++ % 50000 == 0) {
                    //    Trace.WriteLine($"Events {times_}, {(double)times_ / 1000000} M");
                    //    long memory2 = GC.GetTotalMemory(true);
                    //    Trace.WriteLine($"    Mem usage: {(double)(memory2 - memory_) / (1024 * 1024 * 1024)} GB");
                    //    var objSize = GetSizeOfObject(new PerformanceCounterEvent());
                    //    Trace.WriteLine($"    Events mem: {(double)((long)events_.Count * objSize) / (1024 * 1024 * 1024)} GB, event size: {objSize} b");
                    //    Trace.Flush();
                    //}
                }
            }
            else if (e.Event.Id == 50) {
                ; // DispatcherReadyThreadTraceData needs THREAD+PROC xperf
            }
            else if (e.Event.Id == 0x49) {
                if (e.Event.Data.Length > 0) {
                    var counter = ReadPerformanceCounterInfo(e.Event.Data);
                    profileData_.RegisterPerformanceCounter(counter);

                    //if (times_++ % 50000 == 0) {
                    //    Trace.WriteLine($"Events {times_}, {(double)times_ / 1000000} M");
                    //    long memory2 = GC.GetTotalMemory(true);
                    //    Trace.WriteLine($"    Mem usage: {(double)(memory2 - memory_) / (1024 * 1024 * 1024)} GB");
                    //    var objSize = GetSizeOfObject(new PerformanceCounterEvent());
                    //    Trace.WriteLine($"    Events mem: {(double)((long)events_.Count * objSize) / (1024 * 1024 * 1024)} GB, event size: {objSize} b");
                    //    Trace.Flush();
                    //}
                }
            }
        }


        public async Task<ProfileData> LoadTraceAsync(string tracePath, string imageName, 
                      ProfileDataProviderOptions options, ProfileLoadProgressHandler progressCallback,
                      CancelableTask cancelableTask) {
            try {
                options_ = options;
                mainModule_ = new ModuleInfo(options, session_);

                // Extract just the file name.
                imageName = Utils.TryGetFileNameWithoutExtension(imageName);

                times_ = 0;
                memory_ = GC.GetTotalMemory(true);

                var imageModuleMap = new Dictionary<IImage, ModuleInfo>();


                // The entire ETW processing must be done on the same thread.
                bool result = await Task.Run(async () => {
                    Trace.WriteLine($"Init at {DateTime.Now}");
                    //Trace.Flush();

                    // map IImage -> moduleInfo
                    // assume moduleInfo doesn't change, check prev iimage

                    IImage prevImage = null;
                    ModuleInfo prevModule = null;

                    // Start getting the function address data while the trace is loading.
                    progressCallback?.Invoke(new ProfileLoadProgress(ProfileLoadStage.TraceLoading));

                    // Setup module for main binary.
                    mainModule_.Initialize(mainSummary_);

                    // Load the trace.
                    var settings = new TraceProcessorSettings {
                        AllowLostEvents = true
                    };

                    trace = TraceProcessor.Create(tracePath, settings);
                    var pendingSymbolData = trace.UseSymbols();
                    var pendingCpuSamplingData = trace.UseCpuSamplingData();
                    var threads = trace.UseThreads();
                    var procs = trace.UseProcesses();

                    var sw = Stopwatch.StartNew();

                    Trace.WriteLine($"Start load at {DateTime.Now}");
                    //Trace.Flush();

                    //trace.Use(CollectPerformanceCounterEvents);
                    trace.Process(new ProcessProgressTracker(progressCallback));

                    Trace.WriteLine($"After process load in {sw.Elapsed}");
                    Trace.WriteLine($"PMC events: {events_.Count}");
                    //Trace.Flush();

                    var allProcs = procs.Result.Processes;
                    var allThreads = threads.Result.Threads;


                    var procCache = new ProcessInfoCache(allProcs, allThreads);
                    var mainProcessId = procCache.FindProcess(imageName);


                    //Trace.Flush();


                    if (cancelableTask != null && cancelableTask.IsCanceled) {
                        return false;
                    }

                    var cpuSamplingData = pendingCpuSamplingData.Result;

                    // Load symbols.
                    try {
                        var symbolData = pendingSymbolData.Result;

                        foreach (var path in options_.Symbols.SymbolSearchPaths) {
                            var directory = Utils.TryGetDirectoryName(path);
                            await symbolData.LoadSymbolsAsync(SymCachePath.Automatic,
                                new RawSymbolPath(directory),
                                new SymbolProgressTracker(progressCallback));
                        }
                    }
                    catch (Exception ex) {
                        Trace.TraceWarning($"Failed to load symbols: {ex.Message}");
                    }

                    if (cancelableTask != null && cancelableTask.IsCanceled) {
                        return false;
                    }

                    // Process the samples.
                    int index = 0;
                    var stackFuncts = new HashSet<IRTextFunction>();
                    var stackModules = new HashSet<string>();

                    var samples = cpuSamplingData.Samples;
                    var totalSamples = samples.Count;
                    temp_ = new List<ProfileSample>();

                    //var proto = new ProtoProfile();

                    async Task<ModuleInfo> FindModuleInfo(IImage queryImage) {
                        ModuleInfo module = null;

                        if (queryImage == prevImage) {
                            module = prevModule;
                        }
                        else {
                            if (!imageModuleMap.TryGetValue(queryImage, out var imageModule)) {
                                if (queryImage.FileName.Contains(imageName, StringComparison.OrdinalIgnoreCase)) {
                                    // This is the main image, add it to the map.
                                    imageModule = mainModule_;
                                }
                                else {
                                    imageModule = new ModuleInfo(options_, session_);
                                }

                                imageModuleMap[queryImage] = imageModule;

                                //? Needs some delay-load, can't disasm every dll for no reason
                                //? - now Initialize uses a whitelist
                                var sw2 = Stopwatch.StartNew();
                                Trace.WriteLine($"Start loading image {queryImage.FileName}");

                                //? TODO: New doc not registerd properly with session, reload crashes
                                if (await imageModule.Initialize(queryImage.FileName, queryImage.Path)) {
                                    Trace.WriteLine($"  - Init in {sw.Elapsed}");
                                    sw2.Restart();

                                    if (!await imageModule.InitializeDebugInfo()) {
                                        Trace.TraceWarning($"Failed to load debug info for image: {queryImage.FileName}");
                                    }
                                    else {
                                        Trace.WriteLine($"  - Loaded debug info in {sw.Elapsed}");
                                    }
                                }
                            }

                            module = imageModule;
                            prevImage = queryImage;
                            prevModule = module;
                        }

                        return module;
                    }

                    Trace.WriteLine($"Start process samples {DateTime.Now}");
                    Trace.Flush();
                    sw.Restart();

                    foreach (var sample in samples) {
                        //var pf = new ProfileSample() {
                        //    RVA = sample.InstructionPointer.Value,
                        //    Time = sample.Timestamp.Nanoseconds,
                        //    Weight = sample.Weight.TimeSpan,
                        //    ProcessId = sample.Process?.Id ?? -1,
                        //    ThreadId = sample.Thread?.Id ?? -1,
                        //    ImageId = sample.Image?.ProcessId ?? -1, //? Needs some other ID, maybethe md5?
                        //    IsUserCode = !(sample.IsExecutingDeferredProcedureCall == true ||
                        //                   sample.IsExecutingInterruptServicingRoutine == true) 
                        //};
                        
                        //proto.Samples.Add(pf);
                        //continue;

                        if (sample.IsExecutingDeferredProcedureCall == true ||
                            sample.IsExecutingInterruptServicingRoutine == true) {
                            continue; //? TODO: Is this all kernel?
                        }

                        if (sample.Process.Id != mainProcessId) {
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

                        // Count time for each sample.
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

                        bool track = false;

                        //? TODO: Stacks with >256 frames are truncated, inclusive time computation is not right then
                        //? for ex it never gets to main. Easy example is a quicksort impl
                        foreach (var frame in stackFrames) {
                            // Count exclusive time for each module in the executable. 
                            if (isTopFrame &&
                                frame.Image?.FileName != null &&
                                stackModules.Add(frame.Image?.FileName)) {

                                //? TODO: Hack for SPEC runner using a diff binary name
                                if (frame.Image.FileName.Contains(imageName, StringComparison.OrdinalIgnoreCase)) {
                                    profileData_.AddModuleSample(imageName, sampleWeight);
                                }
                                else {
                                    profileData_.AddModuleSample(frame.Image.FileName, sampleWeight);
                                }
                            }

                            if (frame.Image == null) {
                                prevStackFunc = null;
                                prevStackProfile = null;
                                isTopFrame = false;
                                continue;
                            }
                            
                            var module = await FindModuleInfo(frame.Image);
                            var symbol = frame.Symbol;


                            // Search for a function with the matching RVA.

                            long frameRva = frame.RelativeVirtualAddress.Value;
                            long funcRva = 0;
                            string funcName = null;
                            string sourceFileName = null;
                            int sourceLineNumber = 0;

                            if (symbol != null) {
                                //? TODO: This will be removed with sampling redesign
                                 funcName = symbol.FunctionName;

                                 funcRva = symbol.AddressRange.BaseAddress.Value -
                                           symbol.Image.AddressRange.BaseAddress.Value;
                                 sourceLineNumber = symbol.SourceLineNumber;
                                 sourceFileName = symbol.SourceFileName;
                                //funcAddress -= 4096; // An extra page size is always added...
                            }
                            else if(module.HasDebugInfo) {
                                //(funcName, funcRva) = module.FindFunctionByRVA(frameRva);
                                (sourceFileName, sourceLineNumber) = module.DebugInfo.FindFunctionSourceFilePathByRVA(frameRva);

                                //? An extra cache could help
                                var other = module.FindFunctionByRVA2(frameRva);
                                funcName = other.Name;
                                funcRva = other.RVA;

                                //? PubSym overwrites func in tree, keep func
                                //? WppInitKm

                                //if (funcName != null) {
                                //    if (funcName == "WppInitKm") {
                                //        ;
                                //    }

                               // if (other.Name != funcName) {
                               //         Trace.WriteLine($"=> Mismatch func {funcName} at RVA {funcRva} with frame {frameRva}, module {frame.Image.FileName}");
                               //         Trace.WriteLine($"      tree func {other.Name} at RVA {other.RVA} with frame {frameRva}, module {frame.Image.FileName}");

                               //         (funcName, funcRva) = module.FindFunctionByRVA(frameRva);
                               //     }
                               //     //    }
                               //     //    else {
                               //     //        ;
                               //     //    }
                               //// }

                                //if (funcName != null) {
                                //    Trace.WriteLine($"=> Found func without sym {funcName} at RVA {funcRva} with frame {frameRva}, module {frame.Image.FileName}");
                                //}
                                //else {
                                //    Trace.WriteLine($"=> NotFound func without sym at RVA {funcRva} with frame RVA {frameRva}, frame {frame.Address.Value}, module {frame.Image.FileName}");
                                //}
                            }

                            if (funcName == null) {
                                prevStackFunc = null;
                                prevStackProfile = null;
                                isTopFrame = false;
                                continue;
                            }

                            //if (funcName.Contains("RtlpHeapGenerateRandomValue32")) {
                            //    track = true;
                            //    Trace.WriteLine($"Found tracked: {funcName} at RVA {funcRva} with frame {frameRva}");
                            //}
                            

                            var textFunction = module.FindFunction(funcRva, out bool isExternalFunc);

                            if (textFunction == null) {
                                if (track) {
                                    Trace.WriteLine($"  - Skip frame {funcName}");
                                }

                                prevStackFunc = null;
                                prevStackProfile = null;
                                isTopFrame = false;
                                continue;
                            }
                            else {
                                if (track) {
                                    Trace.WriteLine($"  - Walking frame {funcName}");
                                }
                            }

                            //? TODO: Everything here should work only on addresses (func profile as
                            //? {address-image} id), including stack checks. With data collected, background task (or on demand) disasm binary and creates the IRTextFunc ds.
                            //? - samples are already not based on IRElement, only offsets
                            var profile = profileData_.GetOrCreateFunctionProfile(textFunction, sourceFileName);
                            var offset = frameRva - funcRva;

                            // Don't count the inclusive time for recursive functions multiple times.
                            if (stackFuncts.Add(textFunction)) {
                                profile.AddInstructionSample(offset, sampleWeight);
                                profile.AddLineSample(sourceLineNumber, sampleWeight);
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
                            if (options_.MarkInlineFunctions && module.HasDebugInfo &&
                                textFunction.Sections.Count > 0) {
                                //ProcessInlineeSample(sampleWeight, offset, textFunction, module);
                            }

                            isTopFrame = false;
                            prevStackFunc = textFunction;
                            prevStackProfile = profile;
                        }

                        if (track) {
                            Trace.WriteLine($"   < End tracked frames: {stackFrames.Count}");
                        }
                    }

                    Trace.WriteLine($"Done process samples in {sw.Elapsed} at {DateTime.Now}");
                    Trace.Flush();
                    // END SAMPLES PROC


                    Trace.WriteLine($"Start process PMC at {DateTime.Now}");
                    Trace.Flush();
                    sw.Restart();

                    // Process performance counters.
                    index = 0;

                    foreach (var counter in events_) {
                        if (index % 10000 == 0) {
                            if (cancelableTask != null && cancelableTask.IsCanceled) {
                                return false;
                            }

                            progressCallback?.Invoke(new ProfileLoadProgress(ProfileLoadStage.PerfCounterProcessing) {
                                Total = events_.Count,
                                Current = index
                            });
                        }

                        index++;
                        var image = procCache.FindImageForIP(counter.IP, counter.ThreadId, mainProcessId);

                        if (image != null) {
                            profileData_.AddModuleCounter(image.FileName, counter.ProfilerSource, 1);

                            var module = await FindModuleInfo(image);

                            if (module == null) {
                                continue;
                            }
                            else if (!module.Initialized || !module.HasDebugInfo) {
                                //Trace.WriteLine($"Uninitialized module {image.FileName}");
                                continue;
                            }

                            long counterRVA = counter.IP - image.AddressRange.BaseAddress.Value;
                            var funcInfo = module.FindFunctionByRVA2(counterRVA);

                            if (funcInfo.Name == null) {
                                continue;
                            }

                            var textFunction = module.FindFunction(funcInfo.RVA);

                            if (textFunction != null) {
                                var funcOffset = counterRVA - funcInfo.RVA;
                                var profile = profileData_.GetOrCreateFunctionProfile(textFunction, "");
                                profile.AddCounterSample(funcOffset, counter.ProfilerSource, 1);
                            }
                        }
                    }
                    
                    Trace.WriteLine($"Done process PMC in {sw.Elapsed} at {DateTime.Now}");
                    Trace.Flush();
                    long memory = GC.GetTotalMemory(true);

                    //proto.PerfCounters = events_;

                    //foreach (var proc in allProcs) {
                    //    var procProfile = new ProfileProcess() { Id = proc.Id, Name = proc.ImageName };

                    //    foreach (var image in proc.Images) {
                    //        var imageProfile = new ProfileImage() {
                    //            Id = image.ProcessId,
                    //            Name = image.FileName,
                    //            Path = image.Path,
                    //            // ...
                    //        };
                    //        procProfile.Images.Add(imageProfile);
                    //    }

                    //    proto.Processes.Add(procProfile);
                    //}



                    //var data = StateSerializer.Serialize(@"C:\work\pmc.dat", proto);
                    //Trace.WriteLine($"Serialized");
                    //Trace.Flush();

                    //GC.Collect();
                    //GC.WaitForPendingFinalizers();
                    //
                    //long memory2 = GC.GetTotalMemory(true);
                    //Trace.WriteLine($"Diff: {(memory2 - memory) / 1024}, kb");
                    //Trace.Flush();

                    return true;
                });

                // Add 
                foreach (var pair in imageModuleMap) {
                    if (pair.Value != mainModule_ && pair.Value.Summary != null) {
                        Trace.WriteLine(
                            $"Add other {pair.Value.ModuleDocument.FileName}, {pair.Value.Summary.ModuleName}");
                        session_.AddOtherSummary(pair.Value.Summary);
                    }
                }


                trace.Dispose();
                //trace = null;
                return result ? profileData_ : null;
            }
            catch (Exception ex) {
                Trace.TraceError($"Exception loading profile: {ex.Message}");
                Trace.WriteLine(ex.StackTrace);
                Trace.Flush();
                return null;
            }
        }

        private void ProcessInlineeSample(TimeSpan sampleWeight, long sampleOffset, 
                                          IRTextFunction textFunction, ModuleInfo module) {
            // Load current function.
            var loader = module.ModuleDocument.Loader;
            var result = loader.LoadSection(textFunction.Sections[^1]);
            var metadataTag = result.Function.GetTag<AssemblyMetadataTag>();
            bool hasInstrOffsetMetadata =
                metadataTag != null && metadataTag.OffsetToElementMap.Count > 0;

            if (hasInstrOffsetMetadata && !result.IsCached) {
                // Add source location info only once, can be slow.
                module.DebugInfo.AnnotateSourceLocations(result.Function, textFunction.Name);
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
                if (!module.unmangledFuncNamesMap_.TryGetValue(inlinee.Function, out var inlineeTextFunc)) {
                    // The function may have been inlined at all call sites
                    // and not be found in the binary, make a dummy func. for it.
                    inlineeTextFunc = new IRTextFunction(inlinee.Function);
                    module.Summary.AddFunction(inlineeTextFunc);
                    module.unmangledFuncNamesMap_[inlinee.Function] = inlineeTextFunc;
                }

                var inlineeProfile = profileData_.GetOrCreateFunctionProfile(
                                        inlineeTextFunc, inlinee.FilePath);
                inlineeProfile.AddLineSample(inlinee.Line, sampleWeight);
                inlineeProfile.Weight += sampleWeight;
            }
        }

        public void Dispose() {
            //DebugInfo?.Dispose();
        }
    }
}