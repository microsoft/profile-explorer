using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using IRExplorerCore;
using IRExplorerCore.IR;
using IRExplorerCore.IR.Tags;
using System.Linq;
using System.Text;
using System.Collections.Concurrent;
using System.IO;
using IRExplorerUI.Compilers;
using IRExplorerUI.Compilers.ASM;
using IRExplorerUI.Profile.ETW;
using Microsoft.Diagnostics.Tracing;
using static SkiaSharp.HarfBuzz.SKShaper;

namespace IRExplorerUI.Profile;

public sealed class ETWProfileDataProvider : IProfileDataProvider, IDisposable {
    private ProfileDataProviderOptions options_;
    private ProfileDataReport report_;
    private ISession session_;
    private ICompilerInfoProvider compilerInfo_;
    private ProfileData profileData_;
    
    public ETWProfileDataProvider(ISession session) {
        session_ = session;
        profileData_ = new ProfileData();
    }

    public ProfileData LoadTrace(string tracePath, string imageName, 
        ProfileDataProviderOptions options,
        SymbolFileSourceOptions symbolOptions,
        ProfileDataReport report,
        ProfileLoadProgressHandler progressCallback,
        CancelableTask cancelableTask) {
        return LoadTraceAsync(tracePath, imageName, options, symbolOptions,
                              report, progressCallback, cancelableTask).Result;
    }
    
    private BinaryFileDescriptor FromProfileImage(ProfileImage image) {
        return new BinaryFileDescriptor() {
            ImageName = image.ModuleName,
            ImagePath = image.FilePath,
            Checksum = image.Checksum,
            TimeStamp = image.TimeStamp,
            ImageSize = image.Size,
        };
    }

    private BinaryFileDescriptor FromSummary(IRTextSummary summary) {
        return new BinaryFileDescriptor();
    }

    public static async Task<List<ProcessSummary>> FindTraceImages(string tracePath, ProfileDataProviderOptions options, 
                                                                        CancelableTask cancelableTask) {
        try {
            using var eventProcessor = new ETWEventProcessor(tracePath, options);
            return await Task.Run(() => eventProcessor.BuildProcessSummary(cancelableTask));
        }
        catch (Exception ex) {
            Trace.WriteLine($"Failed to open ETL file {tracePath}: {ex.Message}");
            return null;
        }
    }

    public async Task<ProfileData> LoadTraceAsync(string tracePath, string imageName,
        ProfileDataProviderOptions options,
        SymbolFileSourceOptions symbolOptions,
        ProfileDataReport report,
        ProfileLoadProgressHandler progressCallback,
        CancelableTask cancelableTask) {

        progressCallback?.Invoke(new ProfileLoadProgress(ProfileLoadStage.TraceLoading) {
            Total = 0,
            Current = 0
        });
        
        var profile = await Task.Run(() => {
            using var eventProcessor = new ETWEventProcessor(tracePath, options);
            return eventProcessor.ProcessEvents(progressCallback, cancelableTask);
        });

        var binSearchOptions = symbolOptions.WithSymbolPaths(imageName, tracePath);
        return await LoadTraceAsync(profile, imageName, options, binSearchOptions, 
                                    report, progressCallback, cancelableTask);
    }

    public async Task<ProfileData> LoadTraceAsync(RawProfileData prof, string imageName,
        ProfileDataProviderOptions options,
        SymbolFileSourceOptions symbolOptions,
        ProfileDataReport report,
        ProfileLoadProgressHandler progressCallback,
        CancelableTask cancelableTask) {

        var mainProcess = prof.FindProcess(imageName, true);

        if (mainProcess == null) {
            return null;
        }

        return await LoadTraceAsync(prof, mainProcess, options, symbolOptions, 
            report, progressCallback, cancelableTask);
    }

    public async Task<ProfileData> LoadTraceAsync(RawProfileData prof, ProfileProcess mainProcess,
        ProfileDataProviderOptions options,
        SymbolFileSourceOptions symbolOptions,
        ProfileDataReport report,
        ProfileLoadProgressHandler progressCallback,
        CancelableTask cancelableTask) {
        report_ = report;
        report_.Process = mainProcess;
        report.TraceInfo = prof.TraceInfo;
        var mainProcessId = mainProcess.ProcessId;

        try {
            options_ = options;
            var imageName = Utils.TryGetFileNameWithoutExtension(mainProcess.ImageFileName);

            //var imageModuleMap = new Dictionary<int, ModuleInfo>();
            var imageModuleMap = new ConcurrentDictionary<int, ModuleInfo>();

            // The entire ETW processing must be done on the same thread.
            bool result = await Task.Run(async () => {
                Trace.WriteLine($"Init at {DateTime.Now}");

                var totalSw = Stopwatch.StartNew();
                
                // Start getting the function address data while the trace is loading.
                progressCallback?.Invoke(new ProfileLoadProgress(ProfileLoadStage.TraceLoading));

                ProfileImage prevImage = null;
                ModuleInfo prevModule = null;

                // Start getting the function address data while the trace is loading.
                progressCallback?.Invoke(new ProfileLoadProgress(ProfileLoadStage.TraceLoading));

                Trace.WriteLine($"Start load at {DateTime.Now}");

                if (cancelableTask != null && cancelableTask.IsCanceled) {
                    return false;
                }

                // Process the samples.
                int index = 0;
                var acceptedImages = new List<string>();

                bool IsAcceptedModule(string name) {
                    //name = Utils.TryGetFileNameWithoutExtension(name);
                    //name = name.ToLowerInvariant();

                    //if (acceptedImages.Contains(name)) {
                    //    Trace.WriteLine($"=> Accept image {name}");
                    //    return true;
                    //}

                    if (!options_.HasBinaryNameWhitelist) {
                        return true;
                    }

                    foreach (var file in options_.BinaryNameWhitelist) {
                        var fileName = Utils.TryGetFileNameWithoutExtension(file);

                        if (fileName.ToLowerInvariant() == name) {
                            return true;
                        }
                    }

                    return false;
                }

                var moduleLockObject = new object();
                const int IMAGE_LOCK_COUNT = 64;
                var imageLocks = new object[IMAGE_LOCK_COUNT];

                for (int i = 0; i < imageLocks.Length; i++) {
                    imageLocks[i] = new object();
                }

                ModuleInfo FindModuleInfo(ProfileImage queryImage, int processId, SymbolFileSourceOptions symbolOptions) {
                    //if (queryImage == prevImage) {
                    //    return prevModule;
                    //}
                    //else {
                    ModuleInfo imageModule = null;

                    if(!imageModuleMap.TryGetValue(queryImage.Id, out imageModule)) {
                        //Trace.WriteLine($"Lock2 {queryImage}");
                        //Trace.Flush();

                        lock (imageLocks[queryImage.Id % IMAGE_LOCK_COUNT]) {
                            if (!imageModuleMap.TryGetValue(queryImage.Id, out imageModule)) {
                                imageModule = new ModuleInfo(options_, report_, session_);

                                //? Needs some delay-load, can't disasm every dll for no reason
                                //? - now Initialize uses a whitelist
                                var sw2 = Stopwatch.StartNew();
                                Trace.WriteLine($"Start loading image {queryImage.FilePath}");

                                if (!IsAcceptedModule(queryImage.FilePath)) {
                                    imageModuleMap.TryAdd(queryImage.Id, imageModule);

                                    Trace.TraceInformation($"Ignore not whitelisted image {queryImage.FilePath}");
                                    return null;
                                }

                                // Used with managed images.
                                var imageDebugInfo = prof.GetDebugInfoForImage(queryImage, processId);

                                if (imageDebugInfo != null) {
                                    imageDebugInfo.SymbolOptions = symbolOptions;
                                }

                                if (imageModule.Initialize(FromProfileImage(queryImage), symbolOptions, imageDebugInfo).
                                    ConfigureAwait(false).GetAwaiter().GetResult()) {
                                    Trace.WriteLine($"  - Init in {sw2.Elapsed}");
                                    sw2.Restart();

                                    if (!imageModule.InitializeDebugInfo().ConfigureAwait(false).GetAwaiter().GetResult()) {
                                        Trace.TraceWarning($"Failed to load debug info for image: {queryImage.FilePath}");
                                    }
                                    else {
                                        Trace.WriteLine($"  - Loaded debug info in {sw2.Elapsed}");
                                    }
                                }
                                        
                                imageModuleMap.TryAdd(queryImage.Id, imageModule);
                            }
                        }
                    }

                    prevImage = queryImage;
                    prevModule = imageModule;
                    return imageModule;
                    //}
                }
                    
                Trace.WriteLine($"Start preload symbols {DateTime.Now}");

                progressCallback?.Invoke(new ProfileLoadProgress(ProfileLoadStage.SymbolLoading) {
                    Total = 0,
                    Current = 0
                });

                prof.PrintProcess(mainProcessId);
                //prof.PrintSamples(mainProcessId);

                var imageList = mainProcess.Images(prof).ToList();

                if (imageList == null || imageList.Count == 0) {
                    var imageSet = new HashSet<ProfileImage>();

                    foreach (var sample in prof.Samples) {
                        if (!options.IncludeKernelEvents &&
                            sample.IsKernelCode) {
                            continue; //? TODO: Is this all kernel?
                        }

                        var context = sample.GetContext(prof);

                        if (context.ProcessId != mainProcessId) {
                            continue;
                        }

                        var stack = sample.GetStack(prof);

                        if (stack == null || stack.IsUnknown) {
                            continue;
                        }

                        foreach (var frameIp in stack.FramePointers) {
                            //? Use Frame -> Resolved Frame cache
                            ProfileImage frameImage = prof.FindImageForIP(frameIp);

                            if (frameImage != null) {
                                imageSet.Add(frameImage);
                            }
                        }
                    }

                    imageList = imageSet.ToList();

                    foreach (var image in imageList) {
                        prof.AddImageToProcess(mainProcessId, image);
                    }
                }

                int imageLimit = imageList.Count;

#if true
                // Locate the referenced binary files. This will download them
                // from the symbol server if option activated and not yet on local machine.
                var binTaskList = new Task<BinaryFileSearchResult>[imageLimit];
                var pdbTaskList = new Task<DebugFileSearchResult>[imageLimit];

                for (int i = 0; i < imageLimit; i++) {
                    var imagePath = imageList[i].FilePath;

                    if (File.Exists(imagePath)) {
                        Trace.WriteLine($"Adding symbol path: {imagePath}");
                        symbolOptions.InsertSymbolPath(imagePath);
                    }
                }

                for (int i = 0; i < imageLimit; i++) {
                    var binaryFile = FromProfileImage(imageList[i]);
                    binTaskList[i] = PEBinaryInfoProvider.LocateBinaryFile(binaryFile, symbolOptions);
                    //? TODO: Immediately after bin download PDB can be too binTaskList[i].ContinueWith()
                }

                // Determine the compiler target for the new session.
                IRMode irMode = IRMode.Default;

                for (int i = 0; i < imageLimit; i++) {
                    if (binTaskList[i] == null) continue;
                    var binaryFile = await binTaskList[i].ConfigureAwait(false);

                    if (irMode == IRMode.Default && binaryFile != null && binaryFile.Found) {
                        var binaryInfo = binaryFile.BinaryFile;

                        if (binaryInfo != null) {
                            switch (binaryInfo.Architecture) {
                                case System.Reflection.PortableExecutable.Machine.Arm:
                                case System.Reflection.PortableExecutable.Machine.Arm64: {
                                    irMode = IRMode.ARM64;
                                    break;
                                }
                                case System.Reflection.PortableExecutable.Machine.I386:
                                case System.Reflection.PortableExecutable.Machine.Amd64: {
                                    irMode = IRMode.x86_64;
                                    break;
                                }
                            }
                        }
                    }
                }

                // Start a new session in the proper ASM mode.
                await session_.StartNewSession(imageName, SessionKind.FileSession, new ASMCompilerInfoProvider(irMode, session_)).ConfigureAwait(false);

                for (int i = 0; i < imageLimit; i++) {
                    var binaryFile = await binTaskList[i].ConfigureAwait(false);

                    if (binaryFile != null && binaryFile.Found) {
                        var name = Utils.TryGetFileNameWithoutExtension(imageList[i].FilePath);
                        acceptedImages.Add(name.ToLowerInvariant());

                        pdbTaskList[i] = session_.CompilerInfo.FindDebugInfoFile(binaryFile.FilePath);
                    }
                }

                // Wait for the PDBs to be loaded.
                //? TODO: Processing could continue
                for (int i = 0; i < imageLimit; i++) {
                    if (pdbTaskList[i] != null) {
                        var pdbPath = await pdbTaskList[i].ConfigureAwait(false);
                        Trace.WriteLine($"Loaded PDB: {pdbPath}");
                    }
                }
#else

                    for (int i = 0; i < imageLimit; i++) {
                        progressCallback?.Invoke(new ProfileLoadProgress(ProfileLoadStage.SymbolLoading) {
                            Total = imageLimit,
                            Current = i
                        });

                        var name = Utils.TryGetFileNameWithoutExtension(imageTimeList[i].Item1.FileName);
                        acceptedImages.Add(name.ToLowerInvariant());

                        var binaryFile = FromETLImage(imageTimeList[i].Item1);
                        var binaryFilePath = await session_.CompilerInfo.FindBinaryFile(binaryFile);

                        if (File.Exists(binaryFilePath)) {
                            Trace.WriteLine($"Loaded BIN: {binaryFilePath}");

                            var pdbPath = await session_.CompilerInfo.FindDebugInfoFile(binaryFilePath);
                            Trace.WriteLine($"Loaded PDB: {pdbPath}");
                        }
                    }
#endif
                //Trace.WriteLine($"Done preload symbols in {sw.Elapsed} at {DateTime.Now}");
                //Trace.Flush();
                //sw.Restart();

                //Trace.WriteLine($"Start process samples {DateTime.Now}");
                //Trace.Flush();
                //sw.Restart();

                // Trace.WriteLine($"Done process samples in {sw.Elapsed} at {DateTime.Now}");

                //MessageBox.Show("Start samples");

                int temp = 0;
                var sw = Stopwatch.StartNew();

                int chunks = Math.Min(16, (Environment.ProcessorCount * 3) / 4);

#if DEBUG
                chunks = 1;
#endif
                Trace.WriteLine($"Using {chunks} threads");

                var tasks = new List<Task>();
                var taskScheduler = new ConcurrentExclusiveSchedulerPair(TaskScheduler.Default, chunks);
                var taskFactory = new TaskFactory(taskScheduler.ConcurrentScheduler);

                int chunkSize = (int)(prof.Samples.Count / chunks);
                var lockObject = new object();

                //MessageBox.Show("Start tasks");

                //var resolvedStacks = new ConcurrentDictionary<int, ResolvedProfileStack>();

                var callTree = new ProfileCallTree();

                for (int k = 0; k < chunks; k++) {
                    int start = k * chunkSize;
                    int end = Math.Min((k + 1) * chunkSize, (int)prof.Samples.Count);

                    tasks.Add(taskFactory.StartNew(() => {
                        //{
                        var stackFuncts = new HashSet<IRTextFunction>();
                        var stackModules = new HashSet<int>();

                        for (int i = start; i < end; i++) {
                            index++;

                            var sample = prof.Samples[i]; //? Avoid copy, use ref

                            if (!options.IncludeKernelEvents &&
                                sample.IsKernelCode) {
                                continue; //? TODO: Is this all kernel?
                            }

                            var context = sample.GetContext(prof);

                            if (context.ProcessId != mainProcessId) {
                                continue;
                            }

                            //? TODO: Replace %?
                            if (index % 10000 == 0) {
                                if (cancelableTask != null && cancelableTask.IsCanceled) {
                                    return;
                                }

                                progressCallback?.Invoke(new ProfileLoadProgress(ProfileLoadStage.TraceProcessing) {
                                    Total = (int)prof.Samples.Count, Current = index
                                });
                            }

                            var sampleWeight = sample.Weight;

                            // Count time for each sample.
                            var stack = sample.GetStack(prof);

                            if (stack.IsUnknown) {
                                lock (profileData_) {
                                    profileData_.TotalWeight += sampleWeight; //? Lock
                                }

                                continue;
                            }

                            // Count time in the profile image.
                            lock (profileData_) {
                                profileData_.TotalWeight += sampleWeight; //? Lock
                                profileData_.ProfileWeight += sampleWeight; //? Remove, keep only TotalWeight?
                            }

                            bool isTopFrame = true;
                            stackFuncts.Clear();
                            stackModules.Clear();

                            var resolvedStack = stack.GetOptionalData() as ResolvedProfileStack;

                            if (resolvedStack != null) {
                                foreach (var resolvedFrame in resolvedStack.StackFrames) {
                                    if (resolvedFrame.IsUnknown) {
                                        // Can at least increment the module weight.
                                        if (isTopFrame) {
                                            var frameImage = prof.FindImageForIP(resolvedFrame.FrameIP, context);

                                            if (frameImage != null && stackModules.Add(frameImage.Id)) {

                                                //? TODO: Use info from FindModuleInfo
                                                lock (lockObject) {
                                                    profileData_.AddModuleSample(frameImage.ModuleName, sampleWeight);
                                                }
                                            }
                                        }

                                        continue;
                                    }

                                    // Count exclusive time for each module in the executable. 
                                    if (isTopFrame && stackModules.Add(resolvedFrame.Image.Id)) {
                                        lock (lockObject) {
                                            profileData_.AddModuleSample(resolvedFrame.Image.ModuleName, sampleWeight);
                                        }
                                    }

                                    //Trace.WriteLine($"Resolved image {resolvedFrame.Image.ModuleName}m {resolvedFrame.FunctionInfo.Name}, prof func {resolvedFrame.Profile.DebugInfo.Name}, rva {resolvedFrame.FrameRVA}, ip {resolvedFrame.FrameIP}");
                                    
                                    var frameRva = resolvedFrame.FrameRVA;
                                    var funcInfo = resolvedFrame.FunctionInfo;
                                    var funcRva = funcInfo.RVA;
                                    var textFunction = resolvedFrame.Function;
                                    var profile = resolvedFrame.Profile;

                                    lock (profile) {
                                        //profile.DebugInfo = funcInfo; //? REMOVE
                                        var offset = frameRva - funcRva;

                                        // Don't count the inclusive time for recursive functions multiple times.
                                        if (stackFuncts.Add(textFunction)) {
                                            profile.AddInstructionSample(offset, sampleWeight);
                                            profile.Weight += sampleWeight;
                                        }
                                        
                                        // Count the exclusive time for the top frame function.
                                        if (isTopFrame) {
                                            profile.ExclusiveWeight += sampleWeight;
                                        }

                                        //? TODO: Expensive, do as post-processing
                                        // Try to map the sample to all the inlined functions.
                                        //if (options_.MarkInlinedFunctions && resolvedFrame.Module.HasDebugInfo &&
                                        //    textFunction.Sections.Count > 0) {
                                        //    ProcessInlineeSample(sampleWeight, offset, textFunction, resolvedFrame.Module);
                                        //}
                                    }

                                    isTopFrame = false;
                                }
                            }
                            else {
                                resolvedStack = new ResolvedProfileStack(stack.FrameCount, context);
                                var stackFrames = stack.FramePointers;
                                long managedBaseAddress = 0;
                                int frameIndex = 0;

                                bool trace = false;

                                //? TODO: Stacks with >256 frames are truncated, inclusive time computation is not right then
                                //? for ex it never gets to main. Easy example is a quicksort impl
                                for(; frameIndex < stackFrames.Length; frameIndex++) {
                                    var frameIp = stackFrames[frameIndex];

                                    if (trace) {
                                        Trace.WriteLine($"  at frame {frameIndex}: {frameIp}");
                                    }

                                    ProfileImage frameImage = prof.FindImageForIP(frameIp, context);

                                    if (frameImage == null) {
                                        if (prof.HasManagedMethods(context.ProcessId)) {
                                            var managedFunc = prof.FindManagedMethodForIP(frameIp, context.ProcessId);
                                            
                                            if (managedFunc != null) {
                                                frameImage = managedFunc.Image;
                                                managedBaseAddress = 1;
                                            }
                                        }

                                        if (frameImage == null) {
                                            if (trace) {
                                                Trace.WriteLine($"  ! no frameImage");
                                            }

                                            resolvedStack.AddFrame(ResolvedProfileStackFrame.Unknown);
                                            isTopFrame = false;
                                            continue;
                                        }
                                    }
                                   
                                    // Count exclusive time for each module in the executable. 
                                    if (isTopFrame && stackModules.Add(frameImage.Id)) {
                                        lock (profileData_) {
                                            profileData_.AddModuleSample(frameImage.ModuleName, sampleWeight);
                                        }
                                    }

                                    long frameRva = 0;
                                    long funcRva = 0;
                                    string funcName = null;
                                    ModuleInfo module = null;
                                    DebugFunctionInfo funcInfo = null;

                                    //var modSw = Stopwatch.StartNew();
                                    module = FindModuleInfo(frameImage, context.ProcessId, symbolOptions);
                                    //modSw.Stop();
                                    //if (modSw.ElapsedMilliseconds > 500) {
                                    //    Trace.WriteLine($"=> Slow load {modSw.Elapsed}: {frameImage.Name}");
                                    //}

                                    if (module != null && module.HasDebugInfo) {
                                        if (managedBaseAddress != 0) {
                                            frameRva = frameIp;
                                            funcInfo = module.FindDebugFunctionInfo(frameRva);
                                        }
                                        else {
                                            frameRva = frameIp - frameImage.BaseAddress;
                                            funcInfo = module.FindDebugFunctionInfo(frameRva);
                                        }

                                        if (funcInfo != null) {
                                            funcName = funcInfo.Name;
                                            funcRva = funcInfo.RVA;

                                            if (trace) {
                                                Trace.WriteLine($"  => {funcName} at RVA {funcRva} in {frameImage.ModuleName} ");
                                            }
                                        }
                                        else {
                                            if (trace) {
                                                Trace.WriteLine($"  ! no funcInfo in frame RVA {frameRva} {frameImage.ModuleName}");
                                            }
                                        }
                                    }
                                    else {
                                        if (trace) {
                                            Trace.WriteLine($"  ! no module for {frameImage.ModuleName}");
                                            Trace.WriteLine($"      debug issue: {module != null && !module.HasDebugInfo}");
                                        }
                                    }

                                    //? Tracing
                                    //if (funcName != null && funcName.Contains("_PyObject_Malloc")) {
                                    //    Trace.WriteLine($"At {funcName}");
                                    //    trace = true;
                                    //}

                                    IRTextFunction textFunction = null;

                                    if (funcName == null) {
#if false
                                        if (managedBaseAddress == 0) {
                                            Trace.WriteLine($"=> No func for {frameImage.ModuleName}, frameIP {frameIp}");
                                            Trace.WriteLine($"     frameRVA {frameIp - frameImage.BaseAddress}");
                                            Trace.WriteLine($"     image: {frameImage}");
                                            if (module != null && module.sortedFuncList_ != null && module.sortedFuncList_.Count > 0) {
                                                Trace.WriteLine($"    RVA range: {module.sortedFuncList_[0].StartRVA:X},{ module.sortedFuncList_[^1].StartRVA:X}");
                                            }
                                        }

                                        bool addedPlaceholder = false;

                                        if (module != null) {
                                            var placeholderName = $"{frameIp:X}";
                                            textFunction = module.AddPlaceholderFunction(placeholderName, frameIp);
                                            funcInfo = new DebugFunctionInfo(placeholderName, frameIp, 0);
                                            addedPlaceholder = textFunction != null;
                                        }

                                        if(!addedPlaceholder) {
                                            resolvedStack.AddFrame(ResolvedProfileStackFrame.Unknown);
                                            isTopFrame = false;
                                            continue;
                                        }
#else
                                        //resolvedStack.AddFrame(ResolvedProfileStackFrame.Unknown);
                                        //isTopFrame = false;
#endif
                                    }
                                    else {
                                        textFunction = module.FindFunction(funcRva, out bool isExternalFunc);
                                    }

                                    if (textFunction == null) {
                                        resolvedStack.AddFrame(ResolvedProfileStackFrame.Unknown);
                                        isTopFrame = false;
                                        continue;
                                    }

                                    FunctionProfileData profile = null;

                                    //? TODO: Use RW lock
                                    lock (profileData_) {
                                        profile = profileData_.GetOrCreateFunctionProfile(textFunction, null);
                                    }

                                    lock (profile) {
                                        profile.DebugInfo = funcInfo;
                                        var offset = frameRva - funcRva;

                                        // Don't count the inclusive time for recursive functions multiple times.
                                        if (stackFuncts.Add(textFunction)) {
                                            profile.AddInstructionSample(offset, sampleWeight);
                                            profile.Weight += sampleWeight;
                                        }
                                        
                                        // Count the exclusive time for the top frame function.
                                        if (isTopFrame) {
                                            profile.ExclusiveWeight += sampleWeight;
                                        }

                                        //? TODO: Expensive, do as post-processing
                                        // Try to map the sample to all the inlined functions.
                                        //if (options_.MarkInlinedFunctions && module.HasDebugInfo &&
                                        //    textFunction.Sections.Count > 0) {
                                        //    ProcessInlineeSample(sampleWeight, offset, textFunction, module);
                                        //}

                                        resolvedStack.AddFrame(new ResolvedProfileStackFrame(frameIp, frameRva, funcInfo, 
                                                                                             textFunction, frameImage, module, profile));
                                    }
                                    //}

                                    isTopFrame = false;
                                }

                                stack.SetOptionalData(resolvedStack);
                            }

                            // Build call tree. Note that the call tree methods themselves are thread-safe.
                            bool isRootFrame = true;
                            ProfileCallTreeNode prevNode = null;

                            for (int k = resolvedStack.FrameCount - 1; k >= 0; k--) {
                                var resolvedFrame = resolvedStack.StackFrames[k];

                                if (resolvedFrame.IsUnknown) {
                                    continue;
                                }

                                ProfileCallTreeNode node = null;

                                if (isRootFrame) {
                                    node = callTree.AddRootNode(resolvedFrame.FunctionInfo, resolvedFrame.Function);
                                    isRootFrame = false;
                                }
                                else {
                                    node = callTree.AddChildNode(prevNode, resolvedFrame.FunctionInfo, resolvedFrame.Function);
                                }

                                node.AccumulateWeight(sampleWeight);

                                node.RecordSample(sample, resolvedFrame); //? Remove
                                prevNode = node;
                            }

                            if (prevNode != null) {
                                prevNode.AccumulateExclusiveWeight(sampleWeight);
                            }
                        }
                    }));
                    //}
                }

                await Task.WhenAll(tasks.ToArray());
                profileData_.CallTree = callTree;

                //callTree.Print();
                //prof.PrintSamples(0);

                Trace.WriteLine($"Done process samples in {sw.Elapsed}");

                //MessageBox.Show($"Done in {sw.Elapsed}");

                //Trace.WriteLine(callTree.Print());
                //Trace.Flush();
                //? END SAMPLES PROC

#if true
                //? TODO: Check options.IncludePerformanceCounters
                Trace.WriteLine($"Start process PMC at {DateTime.Now}");
                    //Trace.Flush();
                    sw.Restart();

                    // Process performance counters.
                    index = 0;

                    foreach (var counter in prof.PerformanceCounters) {
                        var counterInfo = new PerformanceCounterInfo(counter.Id, counter.Name, counter.Frequency);

                        lock (profileData_) {
                            profileData_.RegisterPerformanceCounter(counterInfo);
                        }
                    }

                    // Try to register the metrics.
                    int metricIndex = 1000;

                    foreach (var metric in options_.PerformanceMetrics) {
                        if (metric.IsEnabled) {
                            profileData_.RegisterPerformanceMetric(metricIndex++, metric);
                        }
                    }
                    
                    foreach (var counter in prof.PerformanceCountersEvents) {
                        index++;

                        if (index % 50000 == 0) {
                            if (cancelableTask != null && cancelableTask.IsCanceled) {
                                break;
                            }
                            progressCallback?.Invoke(new ProfileLoadProgress(ProfileLoadStage.PerfCounterProcessing) {
                                Total = (int)prof.PerformanceCountersEvents.Count, Current = index
                            });
                        }

                        var context = counter.GetContext(prof);

                        if (context.ProcessId != mainProcessId) {
                            continue;
                        }

                        int managedBaseAddress = 0;
                        ProfileImage frameImage = prof.FindImageForIP(counter.IP, context);

                        if (frameImage == null) {
                            if (prof.HasManagedMethods(context.ProcessId)) {
                                var managedFunc = prof.FindManagedMethodForIP(counter.IP, context.ProcessId);

                                if (managedFunc != null) {
                                    frameImage = managedFunc.Image;
                                    managedBaseAddress = 1;
                                }
                            }
                        }

                        if (frameImage != null) {
                            lock (profileData_) {
                                profileData_.AddModuleCounter(frameImage.ModuleName, counter.CounterId, 1);
                            }

                            var module = FindModuleInfo(frameImage, context.ProcessId, symbolOptions);

                            if (module == null) {
                                continue;
                            }
                            else if (!module.Initialized || !module.HasDebugInfo) {
                                //Trace.WriteLine($"Uninitialized module {image.FileName}");
                                continue;
                            }

                            long frameRva = 0;
                            long funcRva = 0;
                            string funcName = null;
                            DebugFunctionInfo funcInfo = null;

                            if (managedBaseAddress != 0) {
                                frameRva = counter.IP;
                                funcInfo = module.FindDebugFunctionInfo(frameRva);
                            }
                            else {
                                frameRva = counter.IP - frameImage.BaseAddress;
                                funcInfo = module.FindDebugFunctionInfo(frameRva);
                            }

                            if (funcInfo != null) {
                                funcName = funcInfo.Name;
                                funcRva = funcInfo.RVA;
                            }

                            if (funcName == null) {
                                continue;
                            }

                            var textFunction = module.FindFunction(funcRva, out bool isExternalFunc);

                            if (textFunction != null) {
                                var offset = frameRva - funcRva;

                                FunctionProfileData profile = null;

                                //? TODO: Use RW lock
                                lock (profileData_) {
                                    profile = profileData_.GetOrCreateFunctionProfile(textFunction, null);
                                }

                                profile.AddCounterSample(offset, counter.CounterId, 1);
                            }
                        }
                    }

                    Trace.WriteLine($"Done process PMC in {sw.Elapsed}");
#endif

                //var data = StateSerializer.Serialize(@"C:\work\pmc.dat", proto);
                //Trace.WriteLine($"Serialized");
                //Trace.Flush();

                //GC.Collect();
                //GC.WaitForPendingFinalizers();
                //
                //long memory2 = GC.GetTotalMemory(true);
                //Trace.WriteLine($"Diff: {(memory2 - memory) / 1024}, kb");
                //Trace.Flush();

                Trace.WriteLine($"Done in {totalSw.Elapsed}");
                //Trace.Flush();
                return true;
            });

            if (cancelableTask != null && cancelableTask.IsCanceled) {
                return null;
            }

            // Add 
            if (result) {
                //ShowGraph(traceProfile);
                LoadedDocument exeDocument = null;
                var otherDocuments = new List<LoadedDocument>();

                foreach (var pair in imageModuleMap) {
                    if (pair.Value.ModuleDocument == null) {
                        continue;
                    }

                    if (Utils.IsExecutableFile(pair.Value.ModuleDocument.BinaryFile?.FilePath)) {
                        if (exeDocument == null) {
                            exeDocument = pair.Value.ModuleDocument;
                            continue;
                        }
                        else if (pair.Value.ModuleDocument.ModuleName.Contains(imageName)) {
                            otherDocuments.Add(exeDocument);
                            exeDocument = pair.Value.ModuleDocument;
                            continue;
                        }
                    }

                    otherDocuments.Add(pair.Value.ModuleDocument);
                }

                if (exeDocument == null) {
                    Trace.WriteLine($"Failed to find document for process {imageName}");
                    return null;
                }

                Trace.WriteLine($"Using exe document {exeDocument?.ModuleName}");
                session_.SessionState.MainDocument = exeDocument;
                await session_.SetupNewSession(exeDocument, otherDocuments);
            }

            if (cancelableTask != null && cancelableTask.IsCanceled) {
                return null;
            }

            return result ? profileData_ : null;
        }
        catch (Exception ex) {
            Trace.TraceError($"Exception loading profile: {ex.Message}");
            Trace.WriteLine(ex.StackTrace);
            Trace.Flush();
            return null;
        }
    }

    //private void ProcessInlineeSample(TimeSpan sampleWeight, long sampleOffset, 
    //    IRTextFunction textFunction, ModuleInfo module) {
    //    return; //? TODO: Reimplement

    //    // Load current function.
    //    var loader = module.ModuleDocument.Loader;
    //    var result = loader.LoadSection(textFunction.Sections[^1]);
    //    var metadataTag = result.Function.GetTag<AssemblyMetadataTag>();
    //    bool hasInstrOffsetMetadata =
    //        metadataTag != null && metadataTag.OffsetToElementMap.Count > 0;

    //    if (hasInstrOffsetMetadata && !result.IsCached) {
    //        // Add source location info only once, can be slow.
    //        module.DebugInfo.AnnotateSourceLocations(result.Function, textFunction.Name);
    //    }

    //    // Try to find instr. referenced by RVA, then go over all inlinees.
    //    if (!hasInstrOffsetMetadata ||
    //        !metadataTag.OffsetToElementMap.TryGetValue(sampleOffset, out var rvaInstr)) {
    //        return;
    //    }

    //    var lineInfo = rvaInstr.GetTag<SourceLocationTag>();

    //    if (lineInfo == null || !lineInfo.HasInlinees) {
    //        return;
    //    }

    //    // For each inlinee, add the sample to its line.
    //    foreach (var inlinee in lineInfo.Inlinees) {
    //        if (!module.unmangledFuncNamesMap_.TryGetValue(inlinee.Function, out var inlineeTextFunc)) {
    //            // The function may have been inlined at all call sites
    //            // and not be found in the binary, make a dummy func. for it.
    //            inlineeTextFunc = new IRTextFunction(inlinee.Function);
    //            module.Summary.AddFunction(inlineeTextFunc);
    //            module.unmangledFuncNamesMap_[inlinee.Function] = inlineeTextFunc;
    //        }

    //        var inlineeProfile = profileData_.GetOrCreateFunctionProfile(
    //            inlineeTextFunc, inlinee.FilePath);
    //        inlineeProfile.AddLineSample(inlinee.Line, sampleWeight);
    //        inlineeProfile.Weight += sampleWeight;
    //    }
    //}

    public void Dispose() {
        //DebugInfo?.Dispose();
    }
}

public class ResolvedProfileStackFrame {
    public long FrameIP { get; set; }
    public long FrameRVA { get; set; }
    public DebugFunctionInfo FunctionInfo { get; set; }
    public IRTextFunction Function { get; set; }
    public ProfileImage Image { get; set; }
    public ModuleInfo Module { get; set; }
    public FunctionProfileData Profile { get; set; }
    public bool IsUnknown => Image == null;

    public ResolvedProfileStackFrame() {}

    public ResolvedProfileStackFrame(long frameIP, long frameRVA, DebugFunctionInfo functionInfo, IRTextFunction function,
        ProfileImage image, ModuleInfo module, FunctionProfileData profile = null) {
        FrameIP = frameIP;
        FrameRVA = frameRVA;
        FunctionInfo = functionInfo;
        Function = function;
        Image = image;
        Module = module;
        Profile = profile;
    }
        
    public static ResolvedProfileStackFrame Unknown => new ResolvedProfileStackFrame();
}

public class ResolvedProfileStack {
    public List<ResolvedProfileStackFrame> StackFrames { get; set; }
    public ProfileContext Context { get; set; }
    public int FrameCount => StackFrames.Count;

    private static ConcurrentDictionary<long, ResolvedProfileStackFrame> frameInstances_ = new();

    public void AddFrame(ResolvedProfileStackFrame frame) {
        var existingFrame = frameInstances_.GetOrAdd(frame.FrameIP, frame);
        StackFrames.Add(existingFrame);
    }

    public ResolvedProfileStack(int frameCount, ProfileContext context) {
        StackFrames = new List<ResolvedProfileStackFrame>(frameCount);
        Context = context;
    }
}
