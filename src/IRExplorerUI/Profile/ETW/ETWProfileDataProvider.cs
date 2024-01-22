// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using IRExplorerCore;
using IRExplorerUI.Compilers;
using IRExplorerUI.Compilers.ASM;

namespace IRExplorerUI.Profile;

public sealed class ETWProfileDataProvider : IProfileDataProvider, IDisposable {
  private const int IMAGE_LOCK_COUNT = 64;
  private const int PROGRESS_UPDATE_INTERVAL = 32768; // Progress UI update after pow2 N samples.
  [ThreadStatic]
  private static ProfileImage prevImage_;
  [ThreadStatic]
  private static ModuleInfo prevModule_;
  private ProfileDataProviderOptions options_;
  private ProfileDataReport report_;
  private ISession session_;
  private ICompilerInfoProvider compilerInfo_;
  private ProfileData profileData_;
  private object lockObject_;
  private object[] imageLocks_;
  private ConcurrentDictionary<int, ModuleInfo> imageModuleMap_;
  private ConcurrentDictionary<int, Task<ModuleInfo>> imageModuleLoadTasks_;
  private int currentSampleIndex_;

  public ETWProfileDataProvider(ISession session) {
    session_ = session;
    profileData_ = new ProfileData();

    // Data structs used for module loading.
    lockObject_ = new object();
    imageModuleMap_ = new ConcurrentDictionary<int, ModuleInfo>();
    imageModuleLoadTasks_ = new ConcurrentDictionary<int, Task<ModuleInfo>>();
    imageLocks_ = new object[IMAGE_LOCK_COUNT];

    for (int i = 0; i < imageLocks_.Length; i++) {
      imageLocks_[i] = new object();
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

  //private void ProcessInlineeSample(TimeSpan sampleWeight, long sampleOffset,
  //    IRTextFunction textFunction, ModuleInfo module) {
  //    return; //? TODO: Reimplement, this needs to have the inlined func still in the binary

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

  public async Task<ProfileData> LoadTraceAsync(string tracePath, List<int> processIds,
                                                ProfileDataProviderOptions options,
                                                SymbolFileSourceSettings symbolSettings,
                                                ProfileDataReport report,
                                                ProfileLoadProgressHandler progressCallback,
                                                CancelableTask cancelableTask) {
    UpdateProgress(progressCallback, ProfileLoadStage.TraceReading, 0, 0);
    var sw = Stopwatch.StartNew();

    var rawProfile = await Task.Run(() => {
      int acceptedProcessId = processIds.Count == 1 ? processIds[0] : 0;
      symbolSettings.InsertSymbolPath(tracePath); // Include the trace path in the symbol search path.

      using var eventProcessor = new ETWEventProcessor(tracePath, options, acceptedProcessId);
      return eventProcessor.ProcessEvents(progressCallback, cancelableTask);
    });

    if (rawProfile.FindProcess(processIds[0]) == null) {
      Trace.WriteLine($"Failed to find main process id {processIds[0]} in trace.");
      return null;
    }

    var result = await LoadTraceAsync(rawProfile, processIds, options, symbolSettings,
                                      report, progressCallback, cancelableTask);
    rawProfile.Dispose();
    return result;
  }

  public async Task<ProfileData> LoadTraceAsync(RawProfileData rawProfile, List<int> processIds,
                                                ProfileDataProviderOptions options,
                                                SymbolFileSourceSettings symbolSettings,
                                                ProfileDataReport report,
                                                ProfileLoadProgressHandler progressCallback,
                                                CancelableTask cancelableTask) {
    var mainProcess = rawProfile.FindProcess(processIds[0]);
    report_ = report;
    report_.Process = mainProcess;
    report.TraceInfo = rawProfile.TraceInfo;
    int mainProcessId = mainProcess.ProcessId;

    // Save process and thread info.
    profileData_.Process = mainProcess;

    foreach (int procId in processIds) {
      var proc = rawProfile.FindProcess(procId);

      if (proc != null) {
        profileData_.AddThreads(proc.Threads(rawProfile));
      }
    }

    // Save all modules to include the ones loaded in the kernel only,
    // which would show up in stack traces if kernel samples are enabled.
    profileData_.AddModules(rawProfile.Images);

    try {
      options_ = options;
      string imageName = Utils.TryGetFileNameWithoutExtension(mainProcess.ImageFileName);

      if (options.HasBinarySearchPaths) {
        symbolSettings.InsertSymbolPaths(options.BinarySearchPaths);
      }

      // The entire ETW processing must be done on the same thread.
      bool result = await Task.Run(async () => {
        Trace.WriteLine($"Init at {DateTime.Now}");
        var totalSw = Stopwatch.StartNew();

        // Start getting the function address data while the trace is loading.
        UpdateProgress(progressCallback, ProfileLoadStage.TraceReading, 0, 0);
        ProfileImage prevImage = null;
        ModuleInfo prevModule = null;

        // Start getting the function address data while the trace is loading.
        Trace.WriteLine($"Start load at {DateTime.Now}");

        if (cancelableTask is {IsCanceled: true}) {
          return false;
        }

        rawProfile.PrintProcess(mainProcessId);
        //profile.PrintSamples(mainProcessId);

        await LoadBinaryAndDebugFiles(rawProfile, mainProcess, imageName,
                                      symbolSettings, progressCallback, cancelableTask);

        if (cancelableTask is {IsCanceled: true}) {
          return false;
        }

        // Start early load of all modules that are used by the binary
        // to reduce the wait time when resolving the stack frame functions.
        UpdateProgress(progressCallback, ProfileLoadStage.TraceProcessing, 0, 0);
        StartEarlyModuleLoad(rawProfile, mainProcess, symbolSettings);

        var sw = Stopwatch.StartNew();

        // Split sample processing in multiple chunk, each done by another thread.
        int chunks = Math.Min(24, Environment.ProcessorCount * 3 / 4);
#if DEBUG
        chunks = 1;
#endif
        int chunkSize = rawProfile.ComputeSampleChunkLength(chunks);

        Trace.WriteLine($"Using {chunks} threads");
        var tasks = new List<Task<List<(ProfileSample Sample, ResolvedProfileStack Stack)>>>();
        var taskScheduler = new ConcurrentExclusiveSchedulerPair(TaskScheduler.Default, chunks);
        var taskFactory = new TaskFactory(taskScheduler.ConcurrentScheduler);

        // Process the raw samples and stacks by resolving stack frame symbols
        // and creating the function profiles.
        var callTree = new ProfileCallTree();

        for (int k = 0; k < chunks; k++) {
          int start = Math.Min(k * chunkSize, rawProfile.Samples.Count);
          int end = Math.Min((k + 1) * chunkSize, rawProfile.Samples.Count);

          if (start == end) {
            continue;
          }

          tasks.Add(taskFactory.StartNew(() => {
            var chunkSamples = ProcessSamplesChunk(rawProfile, start, end,
                                                   processIds, options.IncludeKernelEvents, callTree,
                                                   symbolSettings, progressCallback, cancelableTask, chunks);
            return chunkSamples;
          }));
        }

        await Task.WhenAll(tasks.ToArray());

        // Collect samples from tasks.
        var samples = new List<(ProfileSample, ResolvedProfileStack)>[tasks.Count];

        for (int k = 0; k < tasks.Count; k++) {
          samples[k] = tasks[k].Result;
        }

        // Merge the samples from all chunks and sort them by time.
        int totalSamples = 0;

        foreach (var chunkSamples in samples) {
          totalSamples += chunkSamples.Count;
        }

        profileData_.Samples.EnsureCapacity(totalSamples);

        foreach (var chunkSamples in samples) {
          profileData_.Samples.AddRange(chunkSamples);
        }

        if (profileData_.Samples != null) {
          profileData_.Samples.Sort((a, b) => a.Sample.Time.CompareTo(b.Sample.Time));
        }
        else {
          // Make an empty list to keep other parts happy.
          profileData_.Samples = new List<(ProfileSample Sample, ResolvedProfileStack Stack)>();
        }

        var sw2 = Stopwatch.StartNew();
        profileData_.ComputeThreadSampleRanges();
        profileData_.FilterFunctionProfile(new ProfileSampleFilter());

        Trace.WriteLine($"Done compute func profile/call tree in {sw2.Elapsed}");
        Trace.WriteLine($"Done processing samples in {sw.Elapsed}");

        if (rawProfile.PerformanceCountersEvents.Count > 0) {
          // Process performance counters.
          ProcessPerformanceCounters(rawProfile, processIds, symbolSettings, progressCallback, cancelableTask);
        }

        Trace.WriteLine($"Done in {totalSw.Elapsed}");
        return true;
      });

      if (cancelableTask is {IsCanceled: true}) {
        return null;
      }

      // Setup session documents.
      if (result) {
        var exeDocument = FindSessionDocuments(imageName, out var otherDocuments);

        if (exeDocument == null) {
          Trace.WriteLine($"Failed to find main EXE document");
          exeDocument = new LoadedDocument(string.Empty, string.Empty, Guid.Empty);
          exeDocument.Summary = new IRTextSummary(string.Empty);
        }
        else {
          Trace.WriteLine($"Using exe document {exeDocument.ModuleName}");
        }

        session_.SessionState.MainDocument = exeDocument;
        await session_.SetupNewSession(exeDocument, otherDocuments, profileData_).ConfigureAwait(false);
      }

      if (cancelableTask is {IsCanceled: true}) {
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

  private List<(ProfileSample Sample, ResolvedProfileStack Stack)>
    ProcessSamplesChunk(RawProfileData rawProfile, int start, int end, List<int> processIds,
                        bool includeKernelEvents, ProfileCallTree callTree,
                        SymbolFileSourceSettings symbolSettings,
                        ProfileLoadProgressHandler progressCallback,
                        CancelableTask cancelableTask, int chunks) {
    int index = 0;
    var stackFuncts = new HashSet<IRTextFunction>();
    var stackModules = new HashSet<int>();
    var totalWeight = TimeSpan.Zero;
    var profileWeight = TimeSpan.Zero;
    var samples = new List<(ProfileSample Sample, ResolvedProfileStack Stack)>(end - start + 1);
    var sampleRefs = CollectionsMarshal.AsSpan(rawProfile.Samples).Slice(start, end - start);

    foreach (var sample in sampleRefs) {
      // Update progress every pow2 N samples.
      if ((++index & PROGRESS_UPDATE_INTERVAL - 1) == 0) {
        if (cancelableTask is {IsCanceled: true}) {
          return samples;
        }

        int position = Interlocked.Add(ref currentSampleIndex_, PROGRESS_UPDATE_INTERVAL);
        UpdateProgress(progressCallback, ProfileLoadStage.TraceProcessing,
                       rawProfile.Samples.Count, position);
      }

      if (!includeKernelEvents && sample.IsKernelCode) {
        continue;
      }

      // Ignore other processes.
      var context = sample.GetContext(rawProfile);

      if (!processIds.Contains(context.ProcessId)) {
        continue;
      }

      // Count time for each sample.
      var sampleWeight = sample.Weight;
      var stack = sample.GetStack(rawProfile);
      ResolvedProfileStack resolvedStack = null;

      // Count time in the profile image.
      totalWeight += sample.Weight;
      profileWeight += sample.Weight;

      // If no stack is associated, use a dummy stack that has
      // a single frame with the sample IP, which is sufficient
      // to count the sample in the proper function as exclusive time.
      if (stack.IsUnknown) {
        stack = new ProfileStack {
          ContextId = sample.ContextId,
          //? TODO: Avoid allocating a new array for each sample.
          FramePointers = new long[1] {sample.IP}
        };
      }

      // Process each stack frame to map it to a module:function
      // using the debug info. A stack is resolved only once, future
      // occurrences use the cached version.
      stackFuncts.Clear();
      stackModules.Clear();

      resolvedStack = stack.GetOptionalData() as ResolvedProfileStack;

      if (resolvedStack != null) {
        //ProcessResolvedStack(resolvedStack, sampleWeight, stackModules, stackFuncts);
      }
      else {
        resolvedStack = ProcessUnresolvedStack(stack, sampleWeight, context, rawProfile,
                                               stackModules, stackFuncts, symbolSettings);
        stack.SetOptionalData(resolvedStack); // Cache resolved stack.
      }

      samples.Add((sample, resolvedStack));
    }

    lock (lockObject_) {
      profileData_.TotalWeight += totalWeight;
      profileData_.ProfileWeight += profileWeight;
    }

    return samples;
  }

  private ResolvedProfileStack ProcessUnresolvedStack(ProfileStack stack, TimeSpan sampleWeight,
                                                      ProfileContext context, RawProfileData rawProfile,
                                                      HashSet<int> stackModules, HashSet<IRTextFunction> stackFuncts,
                                                      SymbolFileSourceSettings symbolSettings) {
    bool isTopFrame = true;
    var resolvedStack = new ResolvedProfileStack(stack.FrameCount, context);
    long[] stackFrames = stack.FramePointers;
    bool isManagedCode = false;
    int frameIndex = 0;
    int pointerSize = rawProfile.TraceInfo.PointerSize;

    //? TODO: Stacks with >256 frames are truncated, inclusive time computation is not right then
    //? for ex it never gets to main. Easy example is a quicksort impl
    for (; frameIndex < stackFrames.Length; frameIndex++) {
      long frameIp = stackFrames[frameIndex];
      ProfileImage frameImage = null;
      isManagedCode = false;
      bool isKernel = false;

      if (ETWEventProcessor.IsKernelAddress((ulong)frameIp, pointerSize)) {
        frameImage = rawProfile.FindImageForIP(frameIp, ETWEventProcessor.KernelProcessId);
        isKernel = true;
      }
      else {
        frameImage = rawProfile.FindImageForIP(frameIp, context.ProcessId);
      }

      if (frameImage == null) {
        // Check if it's a .NET method, the JITted code may not mapped to any module.
        if (rawProfile.HasManagedMethods(context.ProcessId)) {
          var managedFunc = rawProfile.FindManagedMethodForIP(frameIp, context.ProcessId);

          if (managedFunc != null) {
            frameImage = managedFunc.Image;
            isManagedCode = true;
          }
        }

        if (frameImage == null) {
          resolvedStack.AddFrame(frameIp, 0, ResolvedProfileStackFrameDetails.Unknown, frameIndex, stack);
          isTopFrame = false;
          continue;
        }
      }

      // Try to resolve the frame using the lists of processes/images and debug info.
      long frameRva = 0;
      long funcRva = 0;
      ModuleInfo module = null;
      FunctionDebugInfo funcDebugInfo = null;
      IRTextFunction textFunction = null;

      module = FindModuleInfo(rawProfile, frameImage, context.ProcessId, symbolSettings);

      if (module == null) {
        resolvedStack.AddFrame(frameIp, 0, ResolvedProfileStackFrameDetails.Unknown, frameIndex, stack);
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
          string placeholderName = $"{frameRva:X}";
          textFunction = module.AddPlaceholderFunction(placeholderName, frameRva);
        }

        funcDebugInfo = new FunctionDebugInfo(textFunction.Name, frameRva, 0);
      }

      // Find the corresponding text function in the module, which may
      // be set already above for placeholders.
      if (textFunction == null) {
        textFunction = module.FindFunction(funcDebugInfo.RVA, out bool isExternalFunc);

        if (textFunction == null) {
          resolvedStack.AddFrame(frameIp, 0, ResolvedProfileStackFrameDetails.Unknown, frameIndex, stack);
          isTopFrame = false;
          continue;
        }
      }

      // Create the function profile data, with the merged weight of all instances
      // of the func. across all call stacks.
      var resolvedFrame = new ResolvedProfileStackFrameDetails(funcDebugInfo, textFunction,
                                                               frameImage, module.IsManaged);
      resolvedStack.AddFrame(frameIp, frameRva, resolvedFrame, frameIndex, stack);
      isTopFrame = false;
    }

    return resolvedStack;
  }

  private LoadedDocument FindSessionDocuments(string imageName, out List<LoadedDocument> otherDocuments) {
    LoadedDocument exeDocument = null;
    otherDocuments = new List<LoadedDocument>();

    foreach (var module in imageModuleMap_.Values) {
      if (module.ModuleDocument == null) {
        continue;
      }

      if (Utils.IsExecutableFile(module.ModuleDocument.BinaryFile?.FilePath)) {
        if (exeDocument == null) {
          exeDocument = module.ModuleDocument;
        }
        else if (module.ModuleDocument.ModuleName.Contains(imageName, StringComparison.OrdinalIgnoreCase)) {
          // Pick the better match EXE.
          otherDocuments.Add(exeDocument);
          exeDocument = module.ModuleDocument;
          continue;
        }
      }

      otherDocuments.Add(module.ModuleDocument);

      if (module.DebugInfo != null) {
        // Used after profiling completes to unload debug info and free memory.
        profileData_.RegisterModuleDebugInfo(module.ModuleDocument.ModuleName, module.DebugInfo);
      }
    }

    return exeDocument;
  }

  private void UpdateProgress(ProfileLoadProgressHandler callback, ProfileLoadStage stage,
                              int total, int current, string optional = null) {
    if (callback != null) {
      ThreadPool.QueueUserWorkItem(state => {
        callback(new ProfileLoadProgress(stage) {
          Total = total, Current = current,
          Optional = optional
        });
      });
    }
  }

  private async Task LoadBinaryAndDebugFiles(RawProfileData rawProfile, ProfileProcess mainProcess,
                                             string mainImageName,
                                             SymbolFileSourceSettings symbolSettings,
                                             ProfileLoadProgressHandler progressCallback,
                                             CancelableTask cancelableTask) {
    var imageList = mainProcess.Images(rawProfile).ToList();
    int imageLimit = imageList.Count;

    // Locate the referenced binary files in parallel. This will download them
    // from the symbol server if not yet on local machine and enabled.
    UpdateProgress(progressCallback, ProfileLoadStage.BinaryLoading, imageLimit, 0);
    var binTaskList = new Task<BinaryFileSearchResult>[imageLimit];
    var pdbTaskList = new Task<DebugFileSearchResult>[imageLimit];

    for (int i = 0; i < imageLimit; i++) {
      var binaryFile = FromProfileImage(imageList[i]);
      binTaskList[i] = PEBinaryInfoProvider.LocateBinaryFile(binaryFile, symbolSettings);
      //? TODO: Immediately after bin download PDB can be too binTaskList[i].ContinueWith()
    }

    // Determine the compiler target for the new session.
    var irMode = IRMode.Default;

    for (int i = 0; i < imageLimit; i++) {
      if (cancelableTask is {IsCanceled: true}) {
        return;
      }

      Debug.Assert(binTaskList[i] != null);
      var binaryFile = await binTaskList[i].ConfigureAwait(false);

      if (irMode == IRMode.Default && binaryFile is {Found: true}) {
        var binaryInfo = binaryFile.BinaryFile;

        if (binaryInfo != null) {
          switch (binaryInfo.Architecture) {
            case Machine.Arm:
            case Machine.Arm64: {
              irMode = IRMode.ARM64;
              break;
            }
            case Machine.I386:
            case Machine.Amd64: {
              irMode = IRMode.x86_64;
              break;
            }
          }
        }
      }

      if (binaryFile.Found) {
        UpdateProgress(progressCallback, ProfileLoadStage.BinaryLoading, imageLimit, i,
                       Utils.TryGetFileName(binaryFile.BinaryFile.ImageName));
      }
    }

    // Start a new session in the proper ASM mode.
    await session_.StartNewSession(mainImageName, SessionKind.FileSession,
                                   new ASMCompilerInfoProvider(irMode, session_)).ConfigureAwait(false);

    // Locate the needed debug files, in parallel. This will download them
    // from the symbol server if not yet on local machine and enabled.
    int pdbCount = 0;
    int downloadedPdbCount = 0;
    var taskScheduler = new ConcurrentExclusiveSchedulerPair(TaskScheduler.Default, 32);
    var taskFactory = new TaskFactory(taskScheduler.ConcurrentScheduler);

    for (int i = 0; i < imageLimit; i++) {
      if (cancelableTask is {IsCanceled: true}) {
        return;
      }

      var binaryFile = await binTaskList[i].ConfigureAwait(false);

      if (binaryFile is {Found: true}) {
        pdbCount++;

        pdbTaskList[i] = taskFactory.StartNew(() => {
          var result = session_.CompilerInfo.FindDebugInfoFile(binaryFile.FilePath).Result;
          return result;
        });
      }
    }

    UpdateProgress(progressCallback, ProfileLoadStage.SymbolLoading, pdbCount, 0);
    var sw = Stopwatch.StartNew();

    // Wait for the PDBs to be loaded.
    for (int i = 0; i < imageLimit; i++) {
      if (cancelableTask is {IsCanceled: true}) {
        return;
      }

      if (pdbTaskList[i] != null) {
        var pdbPath = await pdbTaskList[i].ConfigureAwait(false);
        downloadedPdbCount++;

        if (pdbPath.Found) {
          UpdateProgress(progressCallback, ProfileLoadStage.SymbolLoading, pdbCount, i,
                         Utils.TryGetFileName(pdbPath.SymbolFile.FileName));
        }
      }
    }

    UpdateProgress(progressCallback, ProfileLoadStage.SymbolLoading, pdbCount, 0);
    Trace.WriteLine($"PDB download time: {sw.Elapsed}");
  }

  private void StartEarlyModuleLoad(RawProfileData rawProfile, ProfileProcess mainProcess,
                                    SymbolFileSourceSettings symbolSettings) {
    var imageList = mainProcess.Images(rawProfile).ToList();

    foreach (var image in imageList) {
      imageModuleLoadTasks_[image.Id] = Task.Run(() => {
        var imageModule = LoadModuleInfo(image, rawProfile, mainProcess.ProcessId, symbolSettings);
        imageModuleMap_.TryAdd(image.Id, imageModule);
        return imageModule;
      });
    }
  }

  private ModuleInfo LoadModuleInfo(ProfileImage image, RawProfileData rawProfile, int processId,
                                    SymbolFileSourceSettings symbolSettings) {
    var imageModule = new ModuleInfo(report_, session_);
    var imageDebugInfo = rawProfile.GetDebugInfoForImage(image, processId);

    if (imageDebugInfo != null) {
      imageDebugInfo.SymbolSettings = symbolSettings;
    }

    if (imageModule.Initialize(FromProfileImage(image), symbolSettings, imageDebugInfo).ConfigureAwait(false).
      GetAwaiter().GetResult()) {
      if (!imageModule.InitializeDebugInfo().ConfigureAwait(false).GetAwaiter().GetResult()) {
        Trace.TraceWarning($"Failed to load debug debugInfo for image: {image.FilePath}");
      }
    }

    return imageModule;
  }

  private bool IsAcceptedModule(string name) {
    if (!options_.HasBinaryNameWhitelist) {
      return true;
    }

    foreach (string file in options_.BinaryNameWhitelist) {
      string fileName = Utils.TryGetFileNameWithoutExtension(file);

      if (fileName.ToLowerInvariant() == name) {
        return true;
      }
    }

    return false;
  }

  private ModuleInfo FindModuleInfo(RawProfileData rawProfile, ProfileImage queryImage, int processId,
                                    SymbolFileSourceSettings symbolSettings) {
    // prevImage_/prevModule_ are TLS variables since this is called from multiple threads.
    if (queryImage == prevImage_) {
      return prevModule_;
    }

    if (!imageModuleMap_.TryGetValue(queryImage.Id, out var imageModule)) {
      Trace.TraceWarning($"Waiting for image: {queryImage.FilePath}");

      if (imageModuleLoadTasks_.ContainsKey(queryImage.Id)) {
        // Wait for module to load, started in StartEarlyModuleLoad.
        imageModule = imageModuleLoadTasks_[queryImage.Id].ConfigureAwait(false).GetAwaiter().GetResult();
      }
      else {
        // Load module on-demand, this usually happens with kernel images.
        lock (imageLocks_[queryImage.Id % IMAGE_LOCK_COUNT]) {
          if (imageModuleMap_.TryGetValue(queryImage.Id, out imageModule)) {
            return imageModule;
          }

          imageModule = LoadModuleInfo(queryImage, rawProfile, processId, symbolSettings);
          imageModuleMap_.TryAdd(queryImage.Id, imageModule);
        }
      }
    }

    prevImage_ = queryImage;
    prevModule_ = imageModule;
    return imageModule;
  }

  private void ProcessPerformanceCounters(RawProfileData rawProfile, List<int> processIds,
                                          SymbolFileSourceSettings symbolSettings,
                                          ProfileLoadProgressHandler progressCallback,
                                          CancelableTask cancelableTask) {
    // Register the counters found in the trace.
    foreach (var counter in rawProfile.PerformanceCounters) {
      profileData_.RegisterPerformanceCounter(counter);
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
    currentSampleIndex_ = 0;
    Trace.WriteLine($"Start process PMC at {DateTime.Now}");
    var sw = Stopwatch.StartNew();

    foreach (var counter in rawProfile.PerformanceCountersEvents) {
      if ((++index & PROGRESS_UPDATE_INTERVAL - 1) == 0) { // Update progress every 128K samples.
        if (cancelableTask is {IsCanceled: true}) {
          break;
        }

        int position = Interlocked.Add(ref currentSampleIndex_, PROGRESS_UPDATE_INTERVAL);
        UpdateProgress(progressCallback, ProfileLoadStage.PerfCounterProcessing,
                       rawProfile.PerformanceCountersEvents.Count, position);
      }

      var context = counter.GetContext(rawProfile);

      if (!processIds.Contains(context.ProcessId)) {
        continue;
      }

      int managedBaseAddress = 0;
      var frameImage = rawProfile.FindImageForIP(counter.IP, context);

      if (frameImage == null) {
        if (rawProfile.HasManagedMethods(context.ProcessId)) {
          var managedFunc = rawProfile.FindManagedMethodForIP(counter.IP, context.ProcessId);

          if (managedFunc != null) {
            frameImage = managedFunc.Image;
            managedBaseAddress = 1;
          }
        }
      }

      if (frameImage != null) {
        lock (lockObject_) {
          profileData_.AddModuleCounter(frameImage.ModuleName, counter.CounterId, 1);
        }

        var module = FindModuleInfo(rawProfile, frameImage, context.ProcessId, symbolSettings);

        if (module == null) {
          continue;
        }

        if (!module.Initialized || !module.HasDebugInfo) {
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
          long offset = frameRva - funcRva;

          FunctionProfileData profile = null;

          //? TODO: Use RW lock
          lock (lockObject_) {
            profile = profileData_.GetOrCreateFunctionProfile(textFunction, funcInfo);
          }

          profile.AddCounterSample(offset, counter.CounterId, 1);
        }
      }

      profileData_.Events.Add((counter, null));
    }

    Trace.WriteLine($"Done process PMC in {sw.Elapsed}");
  }

  private BinaryFileDescriptor FromProfileImage(ProfileImage image) {
    return new BinaryFileDescriptor {
      ImageName = image.ModuleName,
      ImagePath = image.FilePath,
      Checksum = image.Checksum,
      TimeStamp = image.TimeStamp,
      ImageSize = image.Size
    };
  }
}
