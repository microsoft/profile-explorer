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

    private const int IMAGE_LOCK_COUNT = 64;
    private object[] imageLocks;
    private ConcurrentDictionary<int, ModuleInfo> imageModuleMap_;

    public ETWProfileDataProvider(ISession session) {
        session_ = session;
        profileData_ = new ProfileData();

        // Data structs used for module loading.
        imageModuleMap_ = new ConcurrentDictionary<int, ModuleInfo>();
        imageLocks = new object[IMAGE_LOCK_COUNT];

        for (int i = 0; i < imageLocks.Length; i++) {
            imageLocks[i] = new object();
        }
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

        imageName = Utils.TryGetFileNameWithoutExtension(imageName);
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

                if (cancelableTask != null && cancelableTask.IsCanceled) {
                    return false;
                }

                // Process the samples.

                Trace.WriteLine($"Start preload symbols {DateTime.Now}");

                progressCallback?.Invoke(new ProfileLoadProgress(ProfileLoadStage.BinaryLoading) {
                    Total = 0,
                    Current = 0
                });

                prof.PrintProcess(mainProcessId);
                //prof.PrintSamples(mainProcessId);

                await LoadBinaryAndDebugFiles(prof, mainProcess, imageName, symbolOptions, progressCallback, cancelableTask);

                if (cancelableTask != null && cancelableTask.IsCanceled) {
                    return false;
                }

                var sw = Stopwatch.StartNew();
                int chunks = Math.Min(16, (Environment.ProcessorCount * 3) / 4);

#if DEBUG
                chunks = 1;
#endif

                Trace.WriteLine($"Using {chunks} threads");
                //? TODO: Round up to chunk size to avoid thread issues
                //?    CompressedList API to give chunk size for T

                var tasks = new List<Task>();
                var taskScheduler = new ConcurrentExclusiveSchedulerPair(TaskScheduler.Default, chunks);
                var taskFactory = new TaskFactory(taskScheduler.ConcurrentScheduler);

                int chunkSize = prof.ComputeSampleChunkLength(chunks);
                Trace.WriteLine($"Using chunk {chunkSize}");

                var lockObject = new object();
                int index = 0;

                var callTree = new ProfileCallTree();

                for (int k = 0; k < chunks; k++) {
                    int start = Math.Min(k * chunkSize, (int)prof.Samples.Count);
                    int end = Math.Min((k + 1) * chunkSize, (int)prof.Samples.Count);

                    if (start == end)
                        continue;

                    tasks.Add(taskFactory.StartNew(() => {
                        //{
                        var stackFuncts = new HashSet<IRTextFunction>();
                        var stackModules = new HashSet<int>();

                        foreach(var sample in prof.Samples.Enumerate(start, end)) {
                            index++;

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
                            bool hask = false;

                            var resolvedStack = stack.GetOptionalData() as ResolvedProfileStack;

                            if (resolvedStack != null) {
                                foreach (var resolvedFrame in resolvedStack.StackFrames) {
                                    if (resolvedFrame.IsUnknown) {
                                        // Can at least increment the module weight.
                                        if (isTopFrame) {
                                            var frameImage = prof.FindImageForIP(resolvedFrame.FrameIP, context);

                                            if (frameImage != null && stackModules.Add(frameImage.Id)) {

                                                //? TODO: Use debugInfo from FindModuleInfo
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

                                    //Trace.WriteLine($"Resolved image {resolvedFrame.Image.ModuleName}m {resolvedFrame.DebugInfo.Name}, prof func {resolvedFrame.Profile.FunctionDebugInfo.Name}, rva {resolvedFrame.FrameRVA}, ip {resolvedFrame.FrameIP}");
                                    
                                    var frameRva = resolvedFrame.FrameRVA;
                                    var funcInfo = resolvedFrame.DebugInfo;
                                    var funcRva = funcInfo.RVA;
                                    var textFunction = resolvedFrame.Function;
                                    var profile = resolvedFrame.Profile;

                                    lock (profile) {
                                        //profile.FunctionDebugInfo = funcInfo; //? REMOVE
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
                                int pointerSize = prof.TraceInfo.PointerSize;

                                bool trace = false;

                                //? TODO: Stacks with >256 frames are truncated, inclusive time computation is not right then
                                //? for ex it never gets to main. Easy example is a quicksort impl
                                for(; frameIndex < stackFrames.Length; frameIndex++) {
                                    var frameIp = stackFrames[frameIndex];

                                    if (trace) {
                                        Trace.WriteLine($"  at frame {frameIndex}: {frameIp}");
                                    }

                                    ProfileImage frameImage = null;

                                    if (ETWEventProcessor.IsKernelAddress((ulong)frameIp, pointerSize)) {
                                        //Utils.WaitForDebugger(true);
                                        hask = true;
                                        frameImage = prof.FindImageForIP(frameIp, 0);
                                    }
                                    else {
                                        frameImage = prof.FindImageForIP(frameIp, context.ProcessId);
                                    }

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

                                            resolvedStack.AddFrame(ResolvedProfileStackFrame.Unknown, frameIndex, stack);
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
                                    FunctionDebugInfo funcInfo = null;
                                    IRTextFunction textFunction = null;

                                    //var modSw = Stopwatch.StartNew();
                                    module = FindModuleInfo(prof, frameImage, context.ProcessId, symbolOptions);
                                    //modSw.Stop();
                                    //if (modSw.ElapsedMilliseconds > 500) {
                                    //    Trace.WriteLine($"=> Slow load {modSw.Elapsed}: {frameImage.Name}");
                                    //}

                                    if (module != null) {
                                        if (managedBaseAddress != 0) {
                                            frameRva = frameIp;
                                            // Trace.WriteLine($"Check managed image {frameImage.ModuleName}");
                                            // Trace.WriteLine($"   for IP {frameIp}");
                                            //trace = true;

                                            if (module.HasDebugInfo) {
                                                funcInfo = module.FindFunctionDebugInfo(frameRva);
                                            }
                                            else {
                                                //? TODO: merge with below to add +HEX func names
                                            }
                                        }
                                        else {
                                            frameRva = frameIp - frameImage.BaseAddress;

                                            if (module.HasDebugInfo) {
                                                funcInfo = module.FindFunctionDebugInfo(frameRva);
                                            }
                                            
                                            if(funcInfo == null) {
                                                textFunction = module.FindFunction(frameRva, out _);

                                                if (textFunction == null) {
                                                    var placeholderName = $"{frameIp:X}";
                                                    textFunction = module.AddPlaceholderFunction(placeholderName, frameIp);
                                                    if (trace) {
                                                        Trace.WriteLine($"New placehodler {placeholderName} in {frameImage.ModuleName} ");
                                                    }
                                                }

                                                funcInfo = new FunctionDebugInfo(textFunction.Name, frameIp, 0);
                                            }
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
                                            funcInfo = new FunctionDebugInfo(placeholderName, frameIp, 0);
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
                                        if (textFunction == null) {
                                            textFunction = module.FindFunction(funcRva, out bool isExternalFunc);
                                        }
                                    }

                                    if (textFunction == null) {
                                        resolvedStack.AddFrame(ResolvedProfileStackFrame.Unknown, frameIndex, stack);
                                        isTopFrame = false;
                                        continue;
                                    }

                                    FunctionProfileData profile = null;

                                    //? TODO: Use RW lock
                                    lock (profileData_) {
                                        profile = profileData_.GetOrCreateFunctionProfile(textFunction, null);
                                    }

                                    lock (profile) {
                                        profile.FunctionDebugInfo = funcInfo;
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

                                        var resolvedFrame = new ResolvedProfileStackFrame(frameIp, frameRva, funcInfo, 
                                                                                          textFunction, frameImage, module, profile);
                                        resolvedStack.AddFrame(resolvedFrame, frameIndex, stack);
                                    }
                                    //}

                                    isTopFrame = false;
                                }

                                stack.SetOptionalData(resolvedStack);
                            }

                            UpdateCallTree(resolvedStack, callTree, sample);
                        }
                    }));
                }

                await Task.WhenAll(tasks.ToArray());
                profileData_.CallTree = callTree;
                Trace.WriteLine($"Done processing samples in {sw.Elapsed}");

                if (options_.IncludePerformanceCounters) {
                    // Process performance counters.
                    ProcessPerformanceCounters(prof, mainProcessId, symbolOptions, progressCallback, cancelableTask);

                }

                Trace.WriteLine($"Done in {totalSw.Elapsed}");
                //Trace.Flush();
                return true;
            });

            if (cancelableTask != null && cancelableTask.IsCanceled) {
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

    private void UpdateCallTree(ResolvedProfileStack resolvedStack, ProfileCallTree callTree,
                                ProfileSample sample) {
        // Build call tree. Note that the call tree methods themselves are thread-safe.
        bool isRootFrame = true;
        ProfileCallTreeNode prevNode = null;
        ResolvedProfileStackFrame prevFrame = null;

        for (int k = resolvedStack.FrameCount - 1; k >= 0; k--) {
            var resolvedFrame = resolvedStack.StackFrames[k];

            if (resolvedFrame.IsUnknown) {
                continue;
            }

            ProfileCallTreeNode node = null;

            if (isRootFrame) {
                node = callTree.AddRootNode(resolvedFrame.DebugInfo, resolvedFrame.Function);
                isRootFrame = false;
            }
            else {
                node = callTree.AddChildNode(prevNode, resolvedFrame.DebugInfo, resolvedFrame.Function);
                prevNode.AddCallSite(node, prevFrame.FrameRVA, sample.Weight);
            }

            node.AccumulateWeight(sample.Weight);

            // Set the user/kernel-mode context of the function.
            if (node.Kind == ProfileCallTreeNodeKind.Unset) {
                if (resolvedFrame.IsKernelCode) {
                    node.Kind = ProfileCallTreeNodeKind.NativeKernel;
                }
                else if(resolvedFrame.Module is { IsManaged: true }) {
                    node.Kind = ProfileCallTreeNodeKind.Managed;
                }
                else {
                    node.Kind = ProfileCallTreeNodeKind.NativeUser;
                }
            }

            //node.RecordSample(sample, resolvedFrame); //? Remove
            prevNode = node;
            prevFrame = resolvedFrame;
        }

        if (prevNode != null) {
            prevNode.AccumulateExclusiveWeight(sample.Weight);
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
            if (cancelableTask != null && cancelableTask.IsCanceled) {
                return;
            }

            Debug.Assert(binTaskList[i] != null);
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
            if (cancelableTask != null && cancelableTask.IsCanceled) {
                return;
            }

            var binaryFile = await binTaskList[i].ConfigureAwait(false);

            if (binaryFile != null && binaryFile.Found) {
                pdbTaskList[i] = session_.CompilerInfo.FindDebugInfoFile(binaryFile.FilePath);
            }
        }

        // Wait for the PDBs to be loaded.
        for (int i = 0; i < imageLimit; i++) {
            if (cancelableTask != null && cancelableTask.IsCanceled) {
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

    private void ProcessPerformanceCounters(RawProfileData prof, int mainProcessId,
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
                        profile = profileData_.GetOrCreateFunctionProfile(textFunction, null);
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

    sealed class ResolvedProfileStackFrame {
        public long FrameIP { get; set; }
        public long FrameRVA { get; set; }
        public FunctionDebugInfo DebugInfo { get; set; }
        public IRTextFunction Function { get; set; }
        public ProfileImage Image { get; set; }
        public ModuleInfo Module { get; set; }
        public FunctionProfileData Profile { get; set; }
        public bool IsKernelCode { get; set; }
        public bool IsUnknown => Image == null;

        public ResolvedProfileStackFrame() { }

        public ResolvedProfileStackFrame(long frameIP, long frameRVA, FunctionDebugInfo debugInfo, IRTextFunction function,
            ProfileImage image, ModuleInfo module, FunctionProfileData profile = null) {
            FrameIP = frameIP;
            FrameRVA = frameRVA;
            DebugInfo = debugInfo;
            Function = function;
            Image = image;
            Module = module;
            Profile = profile;
        }

        public static ResolvedProfileStackFrame Unknown => new ResolvedProfileStackFrame();
    }

    sealed class ResolvedProfileStack {
        public List<ResolvedProfileStackFrame> StackFrames { get; set; }
        public ProfileContext Context { get; set; }
        public int FrameCount => StackFrames.Count;

        private static ConcurrentDictionary<long, ResolvedProfileStackFrame> frameInstances_ = new();
        private static ConcurrentDictionary<long, ResolvedProfileStackFrame> kernelFrameInstances_ = new();

        public void AddFrame(ResolvedProfileStackFrame frame, int frameIndex, ProfileStack stack) {
            // A stack frame IP can be called from both user and kernel mode code.
            frame.IsKernelCode = frameIndex < stack.UserModeTransitionIndex;
            var existingFrame = frame.IsKernelCode ?
                kernelFrameInstances_.GetOrAdd(frame.FrameIP, frame) :
                frameInstances_.GetOrAdd(frame.FrameIP, frame);
            StackFrames.Add(existingFrame);
        }

        public ResolvedProfileStack(int frameCount, ProfileContext context) {
            StackFrames = new List<ResolvedProfileStackFrame>(frameCount);
            Context = context;
        }
    }

}
