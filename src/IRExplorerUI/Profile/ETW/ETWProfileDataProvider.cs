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
using System.Threading;
using IRExplorerUI.Compilers;
using IRExplorerUI.Compilers.ASM;
using Microsoft.Diagnostics.Tracing;
using ProtoBuf;
using static SkiaSharp.HarfBuzz.SKShaper;
using System.Windows;
using IRExplorerUI.Profile;

namespace IRExplorerUI.Profile;

public sealed partial class ETWProfileDataProvider : IProfileDataProvider, IDisposable {
    private ProfileDataProviderOptions options_;
    private ProfileDataReport report_;
    private ISession session_;
    private ICompilerInfoProvider compilerInfo_;
    private ProfileData profileData_;

    private const int IMAGE_LOCK_COUNT = 64;
    private object lockObject;
    private object[] imageLocks;
    private ConcurrentDictionary<int, ModuleInfo> imageModuleMap_;

    public ETWProfileDataProvider(ISession session) {
        session_ = session;
        profileData_ = new ProfileData();

        // Data structs used for module loading.
        lockObject = new();
        imageModuleMap_ = new ConcurrentDictionary<int, ModuleInfo>();
        imageLocks = new object[IMAGE_LOCK_COUNT];

        for (int i = 0; i < imageLocks.Length; i++) {
            imageLocks[i] = new object();
        }
    }

    public static async Task<List<ProcessSummary>>
        FindTraceProcesses(string tracePath, ProfileDataProviderOptions options,
                           ProcessListProgressHandler progressCallback,
                           CancelableTask cancelableTask) {
        try {
            using var eventProcessor = new ETWEventProcessor(tracePath, options);
            return await Task.Run(() => eventProcessor.BuildProcessSummary(progressCallback, cancelableTask));
        }
        catch (Exception ex) {
            Trace.WriteLine($"Failed to open ETL file {tracePath}: {ex.Message}");
            return null;
        }
    }

    public async Task<ProfileData> LoadTraceAsync(string tracePath, List<int> processIds,
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
            int acceptedProcessId = processIds.Count == 1 ? processIds[0] : 0;
            using var eventProcessor = new ETWEventProcessor(tracePath, options, acceptedProcessId);
            return eventProcessor.ProcessEvents(progressCallback, cancelableTask);
        });

        return await LoadTraceAsync(profile, processIds, options, symbolOptions,
                                    report, progressCallback, cancelableTask);
    }
    
    public async Task<ProfileData> LoadTraceAsync(RawProfileData profile, List<int> processIds,
                                                ProfileDataProviderOptions options,
                                                SymbolFileSourceOptions symbolOptions,
                                                ProfileDataReport report,
                                                ProfileLoadProgressHandler progressCallback,
                                                CancelableTask cancelableTask) {
        var mainProcess = profile.FindProcess(processIds[0]);
        report_ = report;
        report_.Process = mainProcess;
        report.TraceInfo = profile.TraceInfo;
        var mainProcessId = mainProcess.ProcessId;

        // Save process and thread info.
        profileData_.Process = mainProcess;

        foreach (var procId in processIds) {
            var proc = profile.FindProcess(procId);

            if (proc != null) {
                profileData_.AddThreads(proc.Threads(profile));
            }
        }

        try {
            options_ = options;
            var imageName = Utils.TryGetFileNameWithoutExtension(mainProcess.ImageFileName);

            if (options.HasBinarySearchPaths) {
                symbolOptions.InsertSymbolPaths(options.BinarySearchPaths);
            }

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

                if (cancelableTask is { IsCanceled: true }) {
                    return false;
                }

                // Process the samples.
                progressCallback?.Invoke(new ProfileLoadProgress(ProfileLoadStage.BinaryLoading) {
                    Total = 0,
                    Current = 0
                });

                profile.PrintProcess(mainProcessId);
                //profile.PrintSamples(mainProcessId);

                await LoadBinaryAndDebugFiles(profile, mainProcess, imageName, 
                                              symbolOptions, progressCallback, cancelableTask);
                if (cancelableTask is { IsCanceled: true }) {
                    return false;
                }

                var sw = Stopwatch.StartNew();

                // Split sample processing in multiple chunk, each done by another thread.
                int chunks = Math.Min(8, (Environment.ProcessorCount * 3) / 4);
#if DEBUG
                chunks = 1;
#endif
                int chunkSize = profile.ComputeSampleChunkLength(chunks);

                Trace.WriteLine($"Using {chunks} threads");
                var tasks = new List<Task>();
                var taskScheduler = new ConcurrentExclusiveSchedulerPair(TaskScheduler.Default, chunks);
                var taskFactory = new TaskFactory(taskScheduler.ConcurrentScheduler);

                //? TODO: Build call tree outside, with sample filtering
                var callTree = new ProfileCallTree();

                for (int k = 0; k < chunks; k++) {
                    int start = Math.Min(k * chunkSize, (int)profile.Samples.Count);
                    int end = Math.Min((k + 1) * chunkSize, (int)profile.Samples.Count);

                    if (start == end) {
                        continue;
                    }

                    tasks.Add(taskFactory.StartNew(() => {
                        ProcessSamplesChunk(profile, start, end, 
                                            processIds, options.IncludeKernelEvents, callTree, 
                                            symbolOptions, progressCallback, cancelableTask, chunks);
                    }));
                }

                await Task.WhenAll(tasks.ToArray());
                profileData_.CallTree = callTree;
                profileData_.Samples.Sort((a, b) => a.Sample.Time.CompareTo(b.Sample.Time));
                Trace.WriteLine($"Done processing samples in {sw.Elapsed}");

                if (options_.IncludePerformanceCounters) {
                    // Process performance counters.
                    ProcessPerformanceCounters(profile, processIds, symbolOptions, progressCallback, cancelableTask);
                }

                Trace.WriteLine($"Done in {totalSw.Elapsed}");
                return true;
            });

            if (cancelableTask is { IsCanceled: true }) {
                return null;
            }

            // Setup session documents.
            if (result) {
                var exeDocument = FindSessionDocuments(imageName, out var otherDocuments);

                if (exeDocument == null) {
                    Trace.WriteLine($"Failed to find main EXE document");
                    return null;
                }

                Trace.WriteLine($"Using exe document {exeDocument.ModuleName}");
                session_.SessionState.MainDocument = exeDocument;
                await session_.SetupNewSession(exeDocument, otherDocuments);
            }

            if (cancelableTask is { IsCanceled: true }) {
                return null;
            }

            // Trace.WriteLine($"Frames: {Interlocked.Read(ref ResolvedProfileStack.frames_)}");
            // Trace.WriteLine($"Unique: {ResolvedProfileStack.uniqueFrames_.Count}");
            // Trace.Flush();

            return result ? profileData_ : null;
        }
        catch (Exception ex) {
            Trace.TraceError($"Exception loading profile: {ex.Message}");
            Trace.WriteLine(ex.StackTrace);
            Trace.Flush();
            return null;
        }
    }

    private void ProcessSamplesChunk(RawProfileData profile, int start, int end, List<int> processIds,
                                     bool includeKernelEvents, ProfileCallTree callTree,
                                     SymbolFileSourceOptions symbolOptions,
                                     ProfileLoadProgressHandler progressCallback, 
                                     CancelableTask cancelableTask, int chunks) {
        int index = 0;
        var stackFuncts = new HashSet<IRTextFunction>();
        var stackModules = new HashSet<int>();

        foreach (var sample in profile.Samples.Enumerate(start, end, recompress: false)) {
            if (!includeKernelEvents && sample.IsKernelCode) {
                continue;
            }

            // Ignore other processes.
            var context = sample.GetContext(profile);

            if (!processIds.Contains(context.ProcessId)) {
                continue;
            }

            //? TODO: Replace %?
            if (index++ % 10000 == 0) {
                if (cancelableTask is { IsCanceled: true }) {
                    return;
                }

                progressCallback?.Invoke(new ProfileLoadProgress(ProfileLoadStage.TraceProcessing) {
                    Total = (int)profile.Samples.Count, Current = index * chunks
                });
            }

            // Count time for each sample.
            var sampleWeight = sample.Weight;
            var stack = sample.GetStack(profile);

            // Count time in the profile image.
            //? TODO: Avoid lock by summing per thread, accumulate at the end
            lock (profileData_) {
                profileData_.TotalWeight += sampleWeight;

                if (stack.IsUnknown) {
                    continue; // Ignore sample without a stack.
                }

                profileData_.ProfileWeight += sampleWeight; //? Remove, keep only TotalWeight?
            }

            // Process each stack frame to map it to a module:function
            // using the debug info. A stack is resolved only once, future
            // occurrences use the cached version.
            stackFuncts.Clear();
            stackModules.Clear();

            var resolvedStack = stack.GetOptionalData() as ResolvedProfileStack;

            if (resolvedStack != null) {
                ProcessResolvedStack(resolvedStack, sampleWeight, stackModules, stackFuncts);
            }
            else {
                resolvedStack = ProcessUnresolvedStack(stack, sampleWeight, context, profile, 
                                                       stackModules, stackFuncts, symbolOptions);
                stack.SetOptionalData(resolvedStack); // Cache resolved stack.
            }

            callTree.UpdateCallTree(sample, resolvedStack);

            //? TODO: Use multiple lists, merge and sort at the end to avoid lock.
            lock (this) {
                profileData_.Samples ??= new List<(ProfileSample, ResolvedProfileStack)>();
                profileData_.Samples.Add((sample, resolvedStack));
            }
        }
    }

    private ResolvedProfileStack ProcessUnresolvedStack(ProfileStack stack, TimeSpan sampleWeight, 
                                                        ProfileContext context, RawProfileData profile, 
                                                        HashSet<int> stackModules, HashSet<IRTextFunction> stackFuncts, 
                                                        SymbolFileSourceOptions symbolOptions) {
        bool isTopFrame = true;
        var resolvedStack = new ResolvedProfileStack(stack.FrameCount, context);
        var stackFrames = stack.FramePointers;
        bool isManagedCode = false;
        int frameIndex = 0;
        int pointerSize = profile.TraceInfo.PointerSize;

        bool trace = false;

        //? TODO: Stacks with >256 frames are truncated, inclusive time computation is not right then
        //? for ex it never gets to main. Easy example is a quicksort impl
        for (; frameIndex < stackFrames.Length; frameIndex++) {
            var frameIp = stackFrames[frameIndex];
            ProfileImage frameImage = null;
            isManagedCode = false;
            
            if (ETWEventProcessor.IsKernelAddress((ulong)frameIp, pointerSize)) {
                frameImage = profile.FindImageForIP(frameIp, ETWEventProcessor.KernelProcessId);
            }
            else {
                frameImage = profile.FindImageForIP(frameIp, context.ProcessId);
            }

            if (frameImage == null) {
                // Check if it's a .NET method, the JITted code may not mapped to any module.
                if (profile.HasManagedMethods(context.ProcessId)) {
                    var managedFunc = profile.FindManagedMethodForIP(frameIp, context.ProcessId);

                    if (managedFunc != null) {
                        frameImage = managedFunc.Image;
                        isManagedCode = true;
                    }
                }

                if (frameImage == null) {
                    if (trace) {
                        Trace.WriteLine($"  ! no frameImage");
                    }

                    resolvedStack.AddFrame(frameIp, 0, ResolvedProfileStackInfo.Unknown, frameIndex, stack);
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
            
            // Try to resolve the frame using the lists of processes/images and debug info.
            long frameRva = 0;
            long funcRva = 0;
            ModuleInfo module = null;
            FunctionDebugInfo funcDebugInfo = null;
            IRTextFunction textFunction = null;

            module = FindModuleInfo(profile, frameImage, context.ProcessId, symbolOptions);

            if (module == null) {
                if (trace) {
                    Trace.WriteLine($"  ! no module for {frameImage.ModuleName}");
                }

                resolvedStack.AddFrame(frameIp, 0, ResolvedProfileStackInfo.Unknown, frameIndex, stack);
                isTopFrame = false;
                continue;
            }

            if (isManagedCode) {
                frameRva = frameIp;
            }
            else {
                frameRva = frameIp - frameImage.BaseAddress;
            }

            // Find the function the sample belongs to.
            if (module.HasDebugInfo) {
                funcDebugInfo = module.FindFunctionDebugInfo(frameRva);
            }

            if (funcDebugInfo == null) {
                // No debug info available for the RVA, make a placeholder function
                // to have something to associate the sample with.
                textFunction = module.FindFunction(frameRva, out _);

                if (textFunction == null) {
                    var placeholderName = $"{frameIp:X}";
                    textFunction = module.AddPlaceholderFunction(placeholderName, frameIp);

                    if (trace) {
                        Trace.WriteLine($"New placeholder {placeholderName} in {frameImage.ModuleName} ");
                    }
                }

                funcDebugInfo = new FunctionDebugInfo(textFunction.Name, frameIp, 0);
            }

            // Find the corresponding text function in the module, which may
            // be set already above for placeholders.
            if (textFunction == null) {
                textFunction = module.FindFunction(funcDebugInfo.RVA, out bool isExternalFunc);

                if (textFunction == null) {
                    if (trace) {
                        Trace.WriteLine($"  ! no text func in frame RVA {frameRva} {frameImage.ModuleName}");
                    }

                    resolvedStack.AddFrame(frameIp, 0, ResolvedProfileStackInfo.Unknown, frameIndex, stack);
                    isTopFrame = false;
                    continue;
                }
            }

            // Create the function profile data, with the merged weight of all instances
            // of the func. across all call stacks.
            FunctionProfileData funcProfile = null;

            lock (profileData_) {
                //? TODO:  Use RW lock
                funcProfile = profileData_.GetOrCreateFunctionProfile(textFunction);
            }

            lock (funcProfile) {
                funcProfile.FunctionDebugInfo = funcDebugInfo;
                var offset = frameRva - funcRva;

                // Don't count the inclusive time for recursive functions multiple times.
                if (stackFuncts.Add(textFunction)) {
                    funcProfile.AddInstructionSample(offset, sampleWeight);
                    funcProfile.Weight += sampleWeight;
                }

                // Count the exclusive time for the top frame function.
                if (isTopFrame) {
                    funcProfile.ExclusiveWeight += sampleWeight;
                }

                //? TODO: Expensive, do as post-processing
                // Try to map the sample to all the inlined functions.
                //if (options_.MarkInlinedFunctions && module.HasDebugInfo &&
                //    textFunction.Sections.Count > 0) {
                //    ProcessInlineeSample(sampleWeight, offset, textFunction, module);
                //}

                var resolvedFrame = new ResolvedProfileStackInfo(funcDebugInfo, textFunction, 
                                                                  frameImage, module.IsManaged, funcProfile);
                resolvedStack.AddFrame(frameIp, frameRva, resolvedFrame, frameIndex, stack);
            }

            isTopFrame = false;
        }

        return resolvedStack;
    }

    private void ProcessResolvedStack(ResolvedProfileStack resolvedStack, TimeSpan sampleWeight, 
                                      HashSet<int> stackModules, HashSet<IRTextFunction> stackFuncts) {
        bool isTopFrame = true;

        foreach (var resolvedFrame in resolvedStack.StackFrames) {
            if (resolvedFrame.IsUnknown) {
                continue;
            }

            // Count exclusive time for each module in the executable.
            if (isTopFrame && stackModules.Add(resolvedFrame.Info.Image.Id)) {
                lock (lockObject) {
                    //? TODO: Avoid lock by summing per thread, accumulate at the end
                    //? TODO: Also, don't use mod name as key, use imageId
                    profileData_.AddModuleSample(resolvedFrame.Info.Image.ModuleName, sampleWeight);
                }
            }

            //Trace.WriteLine($"Resolved image {resolvedFrame.Image.ModuleName}m {resolvedFrame.DebugInfo.Name}, profile func {resolvedFrame.Profile.FunctionDebugInfo.Name}, rva {resolvedFrame.FrameRVA}, ip {resolvedFrame.FrameIP}");
            var funcRva = resolvedFrame.Info.DebugInfo.RVA;
            var frameRva = resolvedFrame.FrameRVA;
            var textFunction = resolvedFrame.Info.Function;
            var funcProfile = resolvedFrame.Info.Profile;

            lock (funcProfile) {
                var offset = frameRva - funcRva;

                // Don't count the inclusive time for recursive functions multiple times.
                if (stackFuncts.Add(textFunction)) {
                    funcProfile.AddInstructionSample(offset, sampleWeight);
                    funcProfile.Weight += sampleWeight;
                }

                // Count the exclusive time for the top frame function.
                if (isTopFrame) {
                    funcProfile.ExclusiveWeight += sampleWeight;
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

    private LoadedDocument FindSessionDocuments(string imageName, out List<LoadedDocument> otherDocuments) {
        LoadedDocument exeDocument = null;
        otherDocuments = new List<LoadedDocument>();

        foreach (var pair in imageModuleMap_) {
            if (pair.Value.ModuleDocument == null) {
                continue;
            }

            if (Utils.IsExecutableFile(pair.Value.ModuleDocument.BinaryFile?.FilePath)) {

                if (exeDocument == null) {
                    exeDocument = pair.Value.ModuleDocument;
                }
                else if (pair.Value.ModuleDocument.ModuleName.Contains(imageName, StringComparison.OrdinalIgnoreCase)) {
                    otherDocuments.Add(exeDocument);
                    exeDocument = pair.Value.ModuleDocument;
                    continue;
                }
            }

            otherDocuments.Add(pair.Value.ModuleDocument);
        }

        return exeDocument;
    }

    private async Task LoadBinaryAndDebugFiles(RawProfileData prof, ProfileProcess mainProcess, string mainImageName,
                                               SymbolFileSourceOptions symbolOptions,
                                               ProfileLoadProgressHandler progressCallback,
                                               CancelableTask cancelableTask) {
        var imageList = mainProcess.Images(prof).ToList();
        int imageLimit = imageList.Count;

        // Locate the referenced binary files in parallel. This will download them
        // from the symbol server if not yet on local machine and enabled.
        var binTaskList = new Task<BinaryFileSearchResult>[imageLimit];
        var pdbTaskList = new Task<DebugFileSearchResult>[imageLimit];

        for (int i = 0; i < imageLimit; i++) {
            var binaryFile = FromProfileImage(imageList[i]);
            binTaskList[i] = PEBinaryInfoProvider.LocateBinaryFile(binaryFile, symbolOptions);
            //? TODO: Immediately after bin download PDB can be too binTaskList[i].ContinueWith()
        }

        // Determine the compiler target for the new session.
        IRMode irMode = IRMode.Default;

        for (int i = 0; i < imageLimit; i++) {
            if (cancelableTask is { IsCanceled: true }) {
                return;
            }

            Debug.Assert(binTaskList[i] != null);
            var binaryFile = await binTaskList[i].ConfigureAwait(false);

            if (irMode == IRMode.Default && binaryFile is { Found: true }) {
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

            progressCallback?.Invoke(new ProfileLoadProgress(ProfileLoadStage.BinaryLoading) {
                Total = imageLimit,
                Current = i
            });
        }

        // Start a new session in the proper ASM mode.
        await session_.StartNewSession(mainImageName, SessionKind.FileSession,
                                       new ASMCompilerInfoProvider(irMode, session_)).ConfigureAwait(false);

        // Locate the needed debug files, in parallel. This will download them
        // from the symbol server if not yet on local machine and enabled.
        progressCallback?.Invoke(new ProfileLoadProgress(ProfileLoadStage.SymbolLoading) {
            Total = 0,
            Current = 0
        });

        for (int i = 0; i < imageLimit; i++) {
            if (cancelableTask is { IsCanceled: true }) {
                return;
            }

            var binaryFile = await binTaskList[i].ConfigureAwait(false);

            if (binaryFile is { Found: true }) {
                pdbTaskList[i] = session_.CompilerInfo.FindDebugInfoFile(binaryFile.FilePath);
            }
        }

        // Wait for the PDBs to be loaded.
        for (int i = 0; i < imageLimit; i++) {
            if (cancelableTask is { IsCanceled: true }) {
                return;
            }

            if (pdbTaskList[i] != null) {
                var pdbPath = await pdbTaskList[i].ConfigureAwait(false);
                Trace.WriteLine($"Loaded PDB: {pdbPath}");
            }

            progressCallback?.Invoke(new ProfileLoadProgress(ProfileLoadStage.SymbolLoading) {
                Total = imageLimit,
                Current = i
            });
        }
    }

    private bool IsAcceptedModule(string name) {
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

    private ModuleInfo FindModuleInfo(RawProfileData prof, ProfileImage queryImage, int processId,
                                      SymbolFileSourceOptions symbolOptions) {
        //? TODO: thread-local
        //if (queryImage == prevImage) {
        //    return prevModule;
        //}

        if (!imageModuleMap_.TryGetValue(queryImage.Id, out var imageModule)) {
            lock (imageLocks[queryImage.Id % IMAGE_LOCK_COUNT]) {
                if (imageModuleMap_.TryGetValue(queryImage.Id, out imageModule)) {
                    return imageModule;
                }

                Trace.WriteLine($"Start loading image {queryImage.FilePath}");
                imageModule = new ModuleInfo(report_, session_);

                if (!IsAcceptedModule(queryImage.FilePath)) {
                    imageModuleMap_.TryAdd(queryImage.Id, imageModule);

                    Trace.TraceInformation($"Ignore not whitelisted image {queryImage.FilePath}");
                    return null;
                }

                // Used with managed images.
                var imageDebugInfo = prof.GetDebugInfoForImage(queryImage, processId);

                if (imageDebugInfo != null) {
                    imageDebugInfo.SymbolOptions = symbolOptions;
                }

                if (imageModule.Initialize(FromProfileImage(queryImage), symbolOptions, imageDebugInfo)
                    .ConfigureAwait(false).GetAwaiter().GetResult()) {

                    if (!imageModule.InitializeDebugInfo().ConfigureAwait(false).GetAwaiter().GetResult()) {
                        Trace.TraceWarning($"Failed to load debug debugInfo for image: {queryImage.FilePath}");
                    }
                }

                imageModuleMap_.TryAdd(queryImage.Id, imageModule);
            }
        }

        //prevImage = queryImage;
        //prevModule = imageModule;
        return imageModule;
    }

    private void ProcessPerformanceCounters(RawProfileData prof, List<int> processIds,
                                            SymbolFileSourceOptions symbolOptions,
                                            ProfileLoadProgressHandler progressCallback,
                                            CancelableTask cancelableTask) {
        // Register the counters found in the trace.
        foreach (var counter in prof.PerformanceCounters) {
            var counterInfo = new PerformanceCounterInfo(counter.Id, counter.Name, counter.Frequency);

            lock (profileData_) {
                profileData_.RegisterPerformanceCounter(counterInfo);
            }
        }

        // Try to register the metrics.
        int metricIndex = 1000;
        int index = 0;

        foreach (var metric in options_.PerformanceMetrics) {
            if (metric.IsEnabled) {
                profileData_.RegisterPerformanceMetric(metricIndex++, metric);
            }
        }

        //? TODO: Parallel
        Trace.WriteLine($"Start process PMC at {DateTime.Now}");
        var sw = Stopwatch.StartNew();

        foreach (var counter in prof.PerformanceCountersEvents) {
            index++;

            if (index % 50000 == 0) {
                if (cancelableTask is { IsCanceled: true }) {
                    break;
                }

                progressCallback?.Invoke(new ProfileLoadProgress(ProfileLoadStage.PerfCounterProcessing) {
                    Total = (int)prof.PerformanceCountersEvents.Count, Current = index
                });
            }

            var context = counter.GetContext(prof);

            if(!processIds.Contains(context.ProcessId)) {
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

                var module = FindModuleInfo(prof, frameImage, context.ProcessId, symbolOptions);

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
                FunctionDebugInfo funcInfo = null;

                if (managedBaseAddress != 0) {
                    frameRva = counter.IP;
                    funcInfo = module.FindFunctionDebugInfo(frameRva);
                }
                else {
                    frameRva = counter.IP - frameImage.BaseAddress;
                    funcInfo = module.FindFunctionDebugInfo(frameRva);
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
                        profile = profileData_.GetOrCreateFunctionProfile(textFunction);
                    }

                    profile.AddCounterSample(offset, counter.CounterId, 1);
                }
            }
        }

        Trace.WriteLine($"Done process PMC in {sw.Elapsed}");
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
    //        // Add source location debugInfo only once, can be slow.
    //        module.FunctionDebugInfo.AnnotateSourceLocations(result.Function, textFunction.Name);
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
        //FunctionDebugInfo?.Dispose();
    }

}