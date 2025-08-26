// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ProfileExplorerCore.Binary;
using ProfileExplorerCore.Compilers.Architecture;
using ProfileExplorerCore.Compilers.ASM;
using ProfileExplorerCore.Profile.Data;
using ProfileExplorerCore.Profile.Processing;
using ProfileExplorerCore.Providers;
using ProfileExplorerCore.Session;
using ProfileExplorerCore.Settings;
using ProfileExplorerCore.Utilities;

namespace ProfileExplorerCore.Profile.ETW;

public sealed class ETWProfileDataProvider : IProfileDataProvider, IDisposable {
  private const int IMAGE_LOCK_COUNT = 64;
  private const int PROGRESS_UPDATE_INTERVAL = 32768; // Progress UI update after pow2 N samples.
#if DEBUG
  // For collecting statistics on stack frame resolution.
  private volatile static int UnresolvedStackCount;
  private volatile static int ResolvedStackCount;
  private Dictionary<ProfileImage, Dictionary<long, (FunctionDebugInfo Info, int SampleCount)>>
    perModuleSampleStatsMap_;
#endif

  // Per-thread caching of the previously handled image
  // and module builder, with hotspots many samples have the same.
  [ThreadStatic]
  private static ProfileImage prevImage_;
  [ThreadStatic]
  private static ProfileModuleBuilder prevProfileModuleBuilder_;
  private ProfileDataProviderOptions options_;
  private ProfileDataReport report_;
  private ISession session_;
  private ProfileData profileData_;
  private object lockObject_;
  private object[] imageLocks_;
  private ConcurrentDictionary<int, ProfileModuleBuilder> imageModuleMap_;
  private HashSet<ProfileImage> rejectedDebugModules_;
  private int currentSampleIndex_;

  public ETWProfileDataProvider(ISession session) {
    session_ = session;
    profileData_ = new ProfileData();

    // Data structs used for module loading.
    lockObject_ = new object();
    imageModuleMap_ = new ConcurrentDictionary<int, ProfileModuleBuilder>();
    rejectedDebugModules_ = new HashSet<ProfileImage>();
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

  public void Dispose() {
  }

  public async Task<ProfileData> LoadTraceAsync(string tracePath, List<int> processIds,
                                                ProfileDataProviderOptions options,
                                                SymbolFileSourceSettings symbolSettings,
                                                ProfileDataReport report,
                                                ProfileLoadProgressHandler progressCallback,
                                                CancelableTask cancelableTask) {
    Trace.WriteLine($"LoadTraceAsync(file): Starting trace loading from file: {tracePath}");
    Trace.WriteLine($"LoadTraceAsync(file): Process IDs: [{string.Join(", ", processIds)}]");
    
    try {
      UpdateProgress(progressCallback, ProfileLoadStage.TraceReading, 0, 0);

      Trace.WriteLine($"LoadTraceAsync(file): Creating ETW event processor");
      var rawProfile = await Task.Run(() => {
        int acceptedProcessId = processIds.Count == 1 ? processIds[0] : 0;
        symbolSettings.InsertSymbolPath(tracePath); // Include the trace path in the symbol search path.

        if (symbolSettings.IncludeSymbolSubdirectories) {
          symbolSettings.ExpandSymbolPathsSubdirectories([".pdb"]);
        }

        Trace.WriteLine($"LoadTraceAsync(file): Starting ETW event processing for process {acceptedProcessId}");
        using var eventProcessor = new ETWEventProcessor(tracePath, options, acceptedProcessId);
        var result = eventProcessor.ProcessEvents(progressCallback, cancelableTask);
        Trace.WriteLine($"LoadTraceAsync(file): ETW event processing completed, found {result?.Samples?.Count ?? 0} samples");
        return result;
      });

      if (rawProfile == null) {
        Trace.WriteLine($"LoadTraceAsync(file): ERROR - ETW event processing returned null");
        return null;
      }

      var mainProcess = rawProfile.FindProcess(processIds[0]);
      if (mainProcess == null) {
        Trace.WriteLine($"LoadTraceAsync(file): ERROR - Failed to find main process id {processIds[0]} in trace");
        Trace.WriteLine($"LoadTraceAsync(file): Available processes: [{string.Join(", ", rawProfile.Processes.Select(p => p.ProcessId))}]");
        return null;
      }

      Trace.WriteLine($"LoadTraceAsync(file): Found main process {processIds[0]}: {mainProcess.ImageFileName}");
      Trace.WriteLine($"LoadTraceAsync(file): Calling LoadTraceAsync with raw profile");
      
      var result = await LoadTraceAsync(rawProfile, processIds, options, symbolSettings,
                                        report, progressCallback, cancelableTask);
      
      Trace.WriteLine($"LoadTraceAsync(file): LoadTraceAsync completed, disposing raw profile");
      rawProfile.Dispose();
      
      Trace.WriteLine($"LoadTraceAsync(file): Returning {(result != null ? "success" : "failure")}");
      return result;
    }
    catch (Exception ex) {
      Trace.WriteLine($"LoadTraceAsync(file): EXCEPTION: {ex.GetType().Name}: {ex.Message}");
      Trace.WriteLine($"LoadTraceAsync(file): Stack trace: {ex.StackTrace}");
      if (ex.InnerException != null) {
        Trace.WriteLine($"LoadTraceAsync(file): Inner exception: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
      }
      return null;
    }
  }

  public async Task<ProfileData> LoadTraceAsync(RawProfileData rawProfile, List<int> processIds,
                                                ProfileDataProviderOptions options,
                                                SymbolFileSourceSettings symbolSettings,
                                                ProfileDataReport report,
                                                ProfileLoadProgressHandler progressCallback,
                                                CancelableTask cancelableTask) {
    Trace.WriteLine($"LoadTraceAsync: Starting profile loading for {processIds.Count} processes");
    
    try {
      // Fill in report details.
      var mainProcess = rawProfile.FindProcess(processIds[0]);
      if (mainProcess == null) {
        Trace.WriteLine($"LoadTraceAsync: ERROR - Main process {processIds[0]} not found in raw profile");
        return null;
      }
      
      Trace.WriteLine($"LoadTraceAsync: Found main process {processIds[0]}: {mainProcess.ImageFileName}");
      
      report_ = report;
      report_.Process = mainProcess;
      report.TraceInfo = rawProfile.TraceInfo;
      int mainProcessId = mainProcess.ProcessId;

      // Save process and thread info.
      profileData_.Process = mainProcess;
      options_ = options;

      foreach (int procId in processIds) {
        var proc = rawProfile.FindProcess(procId);

        if (proc != null) {
          Trace.WriteLine($"LoadTraceAsync: Adding threads for process {procId}: {proc.Threads(rawProfile).Count()} threads");
          profileData_.AddThreads(proc.Threads(rawProfile));
        }
        else {
          Trace.WriteLine($"LoadTraceAsync: WARNING - Process {procId} not found in raw profile");
        }
      }

      // Save all modules to include the ones loaded in the kernel only,
      // which would show up in stack traces if kernel samples are enabled.
      int moduleCount = rawProfile.Images.Count();
      Trace.WriteLine($"LoadTraceAsync: Adding {moduleCount} modules from raw profile");
      profileData_.AddModules(rawProfile.Images);

      string imageName = Utilities.Utils.TryGetFileNameWithoutExtension(mainProcess.ImageFileName);
      Trace.WriteLine($"LoadTraceAsync: Main image name: {imageName}");

      if (options.HasBinarySearchPaths) {
        Trace.WriteLine($"LoadTraceAsync: Adding {options.BinarySearchPaths.Count} binary search paths");
        symbolSettings.InsertSymbolPaths(options.BinarySearchPaths);
      }

      // The entire ETW processing must be done on the same thread.
      Trace.WriteLine($"LoadTraceAsync: Starting main processing task");
      bool result = await Task.Run(async () => {
        try {
          // Start getting the function address data while the trace is loading.
          var totalSw = Stopwatch.StartNew();
          UpdateProgress(progressCallback, ProfileLoadStage.TraceReading, 0, 0);
          Trace.WriteLine($"LoadTraceAsync: Task.Run started, beginning binary/debug file loading");

          // Start getting the function address data while the trace is loading.
          if (cancelableTask is {IsCanceled: true}) {
            Trace.WriteLine($"LoadTraceAsync: Cancellation requested before binary loading");
            return false;
          }

#if DEBUG
          rawProfile.PrintProcess(mainProcessId);
          //profile.PrintSamples(mainProcessId);
#endif

          // Preload binaries and debug files, downloading them concurrently if needed.
          Trace.WriteLine($"LoadTraceAsync: Starting LoadBinaryAndDebugFiles");
          await LoadBinaryAndDebugFiles(rawProfile, mainProcess, imageName,
                                        symbolSettings, progressCallback, cancelableTask);
          Trace.WriteLine($"LoadTraceAsync: Completed LoadBinaryAndDebugFiles");

          if (cancelableTask is {IsCanceled: true}) {
            Trace.WriteLine($"LoadTraceAsync: Cancellation requested after binary loading");
            return false;
          }

          // Start main processing part, resolving stack frames,
          // mapping IPs/RVAs to functions using the debug info.
          UpdateProgress(progressCallback, ProfileLoadStage.TraceProcessing, rawProfile.Samples.Count, 0);
          var processingSw = Stopwatch.StartNew();
          Trace.WriteLine($"LoadTraceAsync: Starting sample processing for {rawProfile.Samples.Count} samples");

          // Split sample processing in multiple chunks, each done by another thread.
          int chunks = CoreSettingsProvider.GeneralSettings.CurrentCpuCoreLimit;
#if DEBUG
          chunks = 1;
#endif
          int chunkSize = rawProfile.ComputeSampleChunkLength(chunks);
          int sampleCount = rawProfile.Samples.Count;

          Trace.WriteLine($"LoadTraceAsync: Using {chunks} threads, chunk size: {chunkSize}");
          var tasks = new List<Task<List<(ProfileSample Sample, ResolvedProfileStack Stack)>>>();
          var taskScheduler = new ConcurrentExclusiveSchedulerPair(TaskScheduler.Default, chunks);
          var taskFactory = new TaskFactory(taskScheduler.ConcurrentScheduler);

          // Process the raw samples and stacks by resolving stack frame symbols
          // and creating the function profiles.
          for (int k = 0; k < chunks; k++) {
            int start = Math.Min(k * chunkSize, sampleCount);
            int end = k == chunks - 1 ? sampleCount : Math.Min((k + 1) * chunkSize, sampleCount);

            Trace.WriteLine($"LoadTraceAsync: Creating task {k} for samples {start}-{end}");
            tasks.Add(taskFactory.StartNew(async () => {
              var chunkSamples = await ProcessSamplesChunk(rawProfile, start, end,
                                                     processIds, options.IncludeKernelEvents,
                                                     symbolSettings, progressCallback, cancelableTask, chunks).ConfigureAwait(false);
              return chunkSamples;
            }).Unwrap());
          }

          Trace.WriteLine($"LoadTraceAsync: Waiting for {tasks.Count} sample processing tasks");
          await Task.WhenAll(tasks.ToArray());
          Trace.WriteLine($"LoadTraceAsync: Done processing samples in {processingSw.Elapsed}");

          if (cancelableTask is {IsCanceled: true}) {
            Trace.WriteLine($"LoadTraceAsync: Cancellation requested after sample processing");
            return false;
          }

          // Collect samples from tasks.
          Trace.WriteLine($"LoadTraceAsync: Collecting chunk samples");
          CollectChunkSamples(tasks);

          // Create the per-function profile and call tree.
          UpdateProgress(progressCallback, ProfileLoadStage.ComputeCallTree, 0, rawProfile.Samples.Count);
          var callTreeSw = Stopwatch.StartNew();
          Trace.WriteLine($"LoadTraceAsync: Computing thread sample ranges and function profile");
          
          profileData_.ComputeThreadSampleRanges();
          profileData_.FilterFunctionProfile(new ProfileSampleFilter());

          Trace.WriteLine(
            $"LoadTraceAsync: Done compute func profile/call tree in {callTreeSw.Elapsed}, {callTreeSw.ElapsedMilliseconds} ms");
          Trace.WriteLine(
            $"LoadTraceAsync: Done processing trace in {processingSw.Elapsed}, {processingSw.ElapsedMilliseconds} ms");

          // Process performance counters.
          if (rawProfile.HasPerformanceCountersEvents) {
            Trace.WriteLine($"LoadTraceAsync: Processing {rawProfile.PerformanceCountersEvents.Count} performance counter events");
            ProcessPerformanceCounters(rawProfile, processIds, symbolSettings, progressCallback, cancelableTask);
          }
          else {
            Trace.WriteLine($"LoadTraceAsync: No performance counter events to process");
          }

#if DEBUG
          // PrintSampleStatistics();
#endif
          Trace.WriteLine($"LoadTraceAsync: Done loading profile in {totalSw.Elapsed}");
          return true;
        }
        catch (Exception ex) {
          Trace.WriteLine($"LoadTraceAsync: EXCEPTION in Task.Run: {ex.GetType().Name}: {ex.Message}");
          Trace.WriteLine($"LoadTraceAsync: Stack trace: {ex.StackTrace}");
          throw; // Re-throw to be caught by outer try-catch
        }
      });

      if (cancelableTask is {IsCanceled: true}) {
        Trace.WriteLine($"LoadTraceAsync: Cancellation requested after main processing task");
        return null;
      }

      // Setup session documents.
      if (result) {
        Trace.WriteLine($"LoadTraceAsync: Main processing succeeded, setting up session documents");
        var exeDocument = FindSessionDocuments(imageName, out var otherDocuments);

        if (exeDocument == null) {
          Trace.WriteLine($"LoadTraceAsync: WARNING - Failed to find main EXE document for {imageName}");
          exeDocument = new LoadedDocument(string.Empty, string.Empty, Guid.Empty);
          exeDocument.Summary = new IRTextSummary(string.Empty);
        }
        else {
          Trace.WriteLine($"LoadTraceAsync: Using exe document {exeDocument.ModuleName} with {otherDocuments.Count} other documents");
        }

        Trace.WriteLine($"LoadTraceAsync: Calling session_.SetupNewSession");
        await session_.SetupNewSession(exeDocument, otherDocuments, profileData_).ConfigureAwait(false);
        Trace.WriteLine($"LoadTraceAsync: Completed session_.SetupNewSession");
      }
      else {
        Trace.WriteLine($"LoadTraceAsync: ERROR - Main processing task returned false (failed)");
      }

      if (cancelableTask is {IsCanceled: true}) {
        Trace.WriteLine($"LoadTraceAsync: Cancellation requested after session setup");
        return null;
      }

      Trace.WriteLine($"LoadTraceAsync: Returning {(result ? "success" : "failure")}");
      return result ? profileData_ : null;
    }
    catch (Exception ex) {
      Trace.TraceError($"LoadTraceAsync: TOP-LEVEL EXCEPTION: {ex.GetType().Name}: {ex.Message}");
      Trace.WriteLine($"LoadTraceAsync: Exception details - Type: {ex.GetType().FullName}");
      Trace.WriteLine($"LoadTraceAsync: Exception source: {ex.Source}");
      Trace.WriteLine($"LoadTraceAsync: Stack trace: {ex.StackTrace}");
      
      if (ex.InnerException != null) {
        Trace.WriteLine($"LoadTraceAsync: Inner exception: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
        Trace.WriteLine($"LoadTraceAsync: Inner exception stack: {ex.InnerException.StackTrace}");
      }
      
      Trace.Flush();
      return null;
    }
  }

  private void CollectChunkSamples(List<Task<List<(ProfileSample Sample, ResolvedProfileStack Stack)>>> tasks) {
    var samples = new List<(ProfileSample, ResolvedProfileStack)>[tasks.Count];

    for (int k = 0; k < tasks.Count; k++) {
      samples[k] = tasks[k].Result;
    }

    // Preallocate merged samples list.
    int totalSamples = 0;

    foreach (var chunkSamples in samples) {
      totalSamples += chunkSamples.Count;
    }

    profileData_.Samples.EnsureCapacity(totalSamples);

    // Merge the samples from all chunks and sort them by time.
    foreach (var chunkSamples in samples) {
      profileData_.Samples.AddRange(chunkSamples);
    }

    if (profileData_.Samples != null) {
      profileData_.Samples.Sort((a, b) => a.Sample.Time.CompareTo(b.Sample.Time));
    }
    else {
      // Make an empty list to keep other parts happy.
      profileData_.Samples = [];
    }
  }

  private async Task<List<(ProfileSample Sample, ResolvedProfileStack Stack)>>
    ProcessSamplesChunk(RawProfileData rawProfile, int start, int end, List<int> processIds,
                        bool includeKernelEvents,
                        SymbolFileSourceSettings symbolSettings,
                        ProfileLoadProgressHandler progressCallback,
                        CancelableTask cancelableTask, int chunks) {
    var totalWeight = TimeSpan.Zero;
    var profileWeight = TimeSpan.Zero;
    var samples = new List<(ProfileSample Sample, ResolvedProfileStack Stack)>(end - start + 1);
    int sampleIndex = 0;
    var chunkSw = Stopwatch.StartNew();
    int stackResolutionCount = 0;
    int kernelSamplesSkipped = 0;
    int otherProcessSamplesSkipped = 0;

    Trace.WriteLine($"ProcessSamplesChunk: Processing chunk {start}-{end} ({end - start} samples) on thread {Thread.CurrentThread.ManagedThreadId}");

    for (int i = start; i < end; i++) {
      var sample = rawProfile.Samples[i];
      
      // Update progress every pow2 N samples.
      if ((++sampleIndex & PROGRESS_UPDATE_INTERVAL - 1) == 0) {
        if (cancelableTask is {IsCanceled: true}) {
          Trace.WriteLine($"ProcessSamplesChunk: Cancellation requested at sample {sampleIndex}");
          return samples;
        }

        // Calculate global progress more accurately - each chunk processes start+sampleIndex samples
        int globalProgress = start + sampleIndex;
        var elapsed = chunkSw.Elapsed;
        var samplesPerSecond = sampleIndex / Math.Max(elapsed.TotalSeconds, 0.001);
        var progressInfo = $"Thread {Thread.CurrentThread.ManagedThreadId}: {samplesPerSecond:F0} samples/sec, {stackResolutionCount} stacks resolved";
        
        UpdateProgress(progressCallback, ProfileLoadStage.TraceProcessing,
                       rawProfile.Samples.Count, globalProgress, progressInfo);
      }

      if (!includeKernelEvents && sample.IsKernelCode) {
        kernelSamplesSkipped++;
        continue;
      }

      // Ignore other processes.
      var context = sample.GetContext(rawProfile);

      if (!processIds.Contains(context.ProcessId)) {
        otherProcessSamplesSkipped++;
        continue;
      }

      // Count time for each sample.
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
      resolvedStack = stack.GetOptionalData() as ResolvedProfileStack;

      if (resolvedStack == null) {
#if DEBUG
        Interlocked.Increment(ref UnresolvedStackCount);
#endif
        stackResolutionCount++;
        resolvedStack = await ProcessUnresolvedStackAsync(stack, context, rawProfile, symbolSettings).ConfigureAwait(false);
        stack.SetOptionalData(resolvedStack); // Cache resolved stack.
      }
      else {
#if DEBUG
        Interlocked.Increment(ref ResolvedStackCount);
#endif
      }

#if DEBUG
      RecordSampleStatistics(resolvedStack);
#endif

      samples.Add((sample, resolvedStack));
    }

    var finalElapsed = chunkSw.Elapsed;
    Trace.WriteLine($"ProcessSamplesChunk: Completed chunk {start}-{end} in {finalElapsed.TotalSeconds:F2}s, " +
                   $"processed {samples.Count} samples, resolved {stackResolutionCount} stacks, " +
                   $"skipped {kernelSamplesSkipped} kernel + {otherProcessSamplesSkipped} other process samples");

    lock (lockObject_) {
      profileData_.TotalWeight += totalWeight;
      profileData_.ProfileWeight += profileWeight;
    }

    return samples;
  }

  private async Task<ResolvedProfileStack> ProcessUnresolvedStackAsync(ProfileStack stack,
                                                      ProfileContext context, RawProfileData rawProfile,
                                                      SymbolFileSourceSettings symbolSettings) {
    var sw = Stopwatch.StartNew();
    var resolvedStack = new ResolvedProfileStack(stack.FrameCount, context);
    long[] stackFrames = stack.FramePointers;
    bool isManagedCode = false;
    int frameIndex = 0;
    int pointerSize = rawProfile.TraceInfo.PointerSize;
    int kernelFrames = 0;
    int managedFrames = 0;
    int unknownFrames = 0;
    int resolvedFrames = 0;

    //? TODO: Stacks with >256 frames are truncated, inclusive time computation is not right then
    //? for ex it never gets to main. Easy example is a quicksort impl
    for (; frameIndex < stackFrames.Length; frameIndex++) {
      long frameIp = stackFrames[frameIndex];
      ProfileImage frameImage = null;
      isManagedCode = false;

      if (ETWEventProcessor.IsKernelAddress((ulong)frameIp, pointerSize)) {
        kernelFrames++;
        frameImage = rawProfile.FindImageForIP(frameIp, ETWEventProcessor.KernelProcessId);
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
            managedFrames++;
          }
        }

        if (frameImage == null) {
          unknownFrames++;
          resolvedStack.AddFrame(null, frameIp, 0, frameIndex, ResolvedProfileStackFrameKey.Unknown, stack,
                                 pointerSize);
          continue;
        }
      }

      // Try to resolve the frame using the lists of processes/images and debug info.
      long frameRva = 0;
      ProfileModuleBuilder profileModuleBuilder = null;
      var moduleStartTime = sw.Elapsed;
      profileModuleBuilder = await GetModuleBuilderAsync(rawProfile, frameImage, context.ProcessId, symbolSettings).ConfigureAwait(false);
      var moduleEndTime = sw.Elapsed;

      if (profileModuleBuilder == null) {
        unknownFrames++;
        resolvedStack.AddFrame(null, frameIp, 0, frameIndex, ResolvedProfileStackFrameKey.Unknown, stack, pointerSize);
        continue;
      }

      // Track significant module builder delays
      var moduleTime = (moduleEndTime - moduleStartTime).TotalMilliseconds;
      if (moduleTime > 10) { // Log delays > 10ms
        Trace.WriteLine($"Slow module builder for {frameImage.ModuleName}: {moduleTime:F1}ms");
      }

      if (isManagedCode) {
        frameRva = frameIp;
      }
      else {
        frameRva = frameIp - frameImage.BaseAddress;
      }

      // Find the function the sample belongs to.
      var funcStartTime = sw.Elapsed;
      var funcPair = profileModuleBuilder.GetOrCreateFunction(frameRva);
      var funcEndTime = sw.Elapsed;

      // Track significant function lookup delays
      var funcTime = (funcEndTime - funcStartTime).TotalMilliseconds;
      if (funcTime > 10) { // Log delays > 10ms
        Trace.WriteLine($"Slow function lookup in {frameImage.ModuleName} at RVA 0x{frameRva:X}: {funcTime:F1}ms");
      }

      // Create the function profile data, with the merged weight of all instances
      // of the func. across all call stacks.
      var resolvedFrame = new ResolvedProfileStackFrameKey(funcPair.DebugInfo, frameImage,
                                                           profileModuleBuilder.IsManaged);
      resolvedStack.AddFrame(funcPair.Function, frameIp, frameRva, frameIndex,
                             resolvedFrame, stack, pointerSize);
      resolvedFrames++;
    }

    var totalTime = sw.Elapsed;
    
    // Log slow stack resolutions for debugging
    if (totalTime.TotalMilliseconds > 50) { // Log stacks taking > 50ms
      Trace.WriteLine($"Slow stack resolution: {totalTime.TotalMilliseconds:F1}ms for {stackFrames.Length} frames " +
                     $"(resolved: {resolvedFrames}, unknown: {unknownFrames}, managed: {managedFrames}, kernel: {kernelFrames})");
    }

    return resolvedStack;
  }

  private ILoadedDocument FindSessionDocuments(string imageName, out List<ILoadedDocument> otherDocuments) {
    ILoadedDocument exeDocument = null;
    otherDocuments = new List<ILoadedDocument>();

    foreach (var module in imageModuleMap_.Values) {
      var moduleDoc = module.ModuleDocument;

      if (moduleDoc == null) {
        continue;
      }

      if (Utilities.Utils.IsExecutableFile(moduleDoc.BinaryFile?.FilePath)) {
        if (exeDocument == null) {
          exeDocument = module.ModuleDocument;
        }
        else if (moduleDoc.ModuleName.Contains(imageName, StringComparison.OrdinalIgnoreCase)) {
          // Pick the better match EXE.
          otherDocuments.Add(exeDocument);
          exeDocument = module.ModuleDocument;
          continue;
        }
      }

      otherDocuments.Add(moduleDoc);

      if (module.DebugInfo != null) {
        // Used after profiling completes to unload debug info and free memory.
        profileData_.RegisterModuleDebugInfo(moduleDoc.ModuleName, module.DebugInfo);
      }
    }

    return exeDocument;
  }

  private void UpdateProgress(ProfileLoadProgressHandler callback, ProfileLoadStage stage,
                              int total, int current, string optional = null) {
    if (callback != null) {
      callback(new ProfileLoadProgress(stage) {
        Total = total, Current = current,
        Optional = optional
      });
    }
  }

  private List<(ProfileImage Image, int SampleCount)>
    CollectTopModules(RawProfileData rawProfile, ProfileProcess mainProcess) {
    var moduleMap = new Dictionary<ProfileImage, int>();
    int pointerSize = rawProfile.TraceInfo.PointerSize;
    var sampleRefs = CollectionsMarshal.AsSpan(rawProfile.Samples);
    var timer = Stopwatch.StartNew();
    int index = 0;
    int totalSamplesProcessed = 0;
    int mainProcessSamples = 0;
    int samplesWithStacks = 0;
    int samplesWithoutStacks = 0;

    Trace.WriteLine($"TOP_MODULES_DEBUG: Starting top modules collection for process {mainProcess.ProcessId} ({mainProcess.ImageFileName})");
    Trace.WriteLine($"TOP_MODULES_DEBUG: Total samples in trace: {rawProfile.Samples.Count}");

    foreach (ref var sample in sampleRefs) {
      totalSamplesProcessed++;
      var context = sample.GetContext(rawProfile);

      if (context.ProcessId != mainProcess.ProcessId) {
        continue;
      }

      mainProcessSamples++;
      var stack = sample.GetStack(rawProfile);

      if (stack.IsUnknown) {
        // Even if no stack is available, count the sample IP itself for module identification
        // This allows us to load debug files for function name resolution even without stacks
        ProfileImage frameImage = null;

        if (ETWEventProcessor.IsKernelAddress((ulong)sample.IP, pointerSize)) {
          frameImage = rawProfile.FindImageForIP(sample.IP, ETWEventProcessor.KernelProcessId);
        }
        else {
          frameImage = rawProfile.FindImageForIP(sample.IP, context.ProcessId);
        }

        if (frameImage != null) {
          moduleMap.AccumulateValue(frameImage, 1);
        }
        
        samplesWithoutStacks++;
        continue;
      }

      samplesWithStacks++;
      
      foreach (long frame in stack.FramePointers) {
        ProfileImage frameImage = null;

        if (ETWEventProcessor.IsKernelAddress((ulong)frame, pointerSize)) {
          frameImage = rawProfile.FindImageForIP(frame, ETWEventProcessor.KernelProcessId);
        }
        else {
          frameImage = rawProfile.FindImageForIP(frame, context.ProcessId);
        }

        if (frameImage != null) {
          moduleMap.AccumulateValue(frameImage, 1);
        }
      }

      // Stop collecting after a couple seconds, it's good enough
      // for an approximated set of used modules.
      if ((++index & PROGRESS_UPDATE_INTERVAL - 1) == 0 &&
          timer.ElapsedMilliseconds > 1000) {
        Trace.WriteLine($"TOP_MODULES_DEBUG: Early termination after {timer.ElapsedMilliseconds}ms at sample {totalSamplesProcessed}");
        break;
      }
    }

    var moduleList = moduleMap.ToList();
    moduleList.Sort((a, b) => b.Item2.CompareTo(a.Item2));

    Trace.WriteLine($"TOP_MODULES_DEBUG: Collection completed in {timer.Elapsed}");
    Trace.WriteLine($"TOP_MODULES_DEBUG: Processed {totalSamplesProcessed} total samples");
    Trace.WriteLine($"TOP_MODULES_DEBUG: Found {mainProcessSamples} samples for main process {mainProcess.ProcessId}");
    Trace.WriteLine($"TOP_MODULES_DEBUG: Found {samplesWithStacks} samples with valid stacks");
    Trace.WriteLine($"TOP_MODULES_DEBUG: Found {samplesWithoutStacks} samples without stacks (using sample IP for module identification)");
    Trace.WriteLine($"TOP_MODULES_DEBUG: Collected {moduleMap.Count} unique modules");
    Trace.WriteLine($"TOP_MODULES_DEBUG: Top 20 modules by sample count:");

    for (int i = 0; i < Math.Min(20, moduleList.Count); i++) {
      var module = moduleList[i];
      Trace.WriteLine($"TOP_MODULES_DEBUG:   {i + 1}. {module.Item1.ModuleName}: {module.Item2} samples (path: {module.Item1.FilePath})");
    }

    Trace.WriteLine("TOP_MODULES_DEBUG: =====================================");

#if DEBUG
    Trace.WriteLine($"Collected top modules: {timer.Elapsed}, modules: {moduleMap.Count}");

    foreach (var pair in moduleList) {
      Trace.WriteLine($"  - {pair.Item1.ModuleName}: {pair.Item2}");
    }

    Trace.WriteLine("-------------------------------------");
#endif
    return moduleList;
  }

  private async Task LoadBinaryAndDebugFiles(RawProfileData rawProfile, ProfileProcess mainProcess,
                                             string mainImageName,
                                             SymbolFileSourceSettings symbolSettings,
                                             ProfileLoadProgressHandler progressCallback,
                                             CancelableTask cancelableTask) {
    var imageList = mainProcess.Images(rawProfile).ToList();
    var kernelProc = rawProfile.FindProcess(ETWEventProcessor.KernelProcessId);

    if (kernelProc != null) {
      imageList.AddRange(kernelProc.Images(rawProfile));
    }

    int imageLimit = imageList.Count;

    // Find the modules with samples, sorted by sample count.
    // Used to skip loading of insignificant modules with few samples.
    var topModules = CollectTopModules(rawProfile, mainProcess);
    int moduleSampleCutOff = 0;

    if (symbolSettings.SkipLowSampleModules) {
      moduleSampleCutOff = (int)(symbolSettings.LowSampleModuleCutoff * rawProfile.Samples.Count);
    }

    Trace.WriteLine($"BINARY_FILTER_DEBUG: Sample cutoff calculation: {symbolSettings.LowSampleModuleCutoff} * {rawProfile.Samples.Count} = {moduleSampleCutOff}");
    Trace.WriteLine($"BINARY_FILTER_DEBUG: Skip low sample modules: {symbolSettings.SkipLowSampleModules}");
    Trace.WriteLine($"BINARY_FILTER_DEBUG: Starting binary filtering for {imageLimit} total modules");

    // Locate the referenced binary files in parallel. This will download them
    // from the symbol server if not yet on local machine and enabled.
    UpdateProgress(progressCallback, ProfileLoadStage.BinaryLoading, imageLimit, 0);
    var binTaskList = new Task<BinaryFileSearchResult>[imageLimit];
    var pdbTaskList = new Task<DebugFileSearchResult>[imageLimit];

    // Start downloading binaries
    var binTaskSemaphore = new SemaphoreSlim(8);

    for (int i = 0; i < imageLimit; i++) {
      Trace.WriteLine($"BINARY_FILTER_DEBUG: Processing module {i + 1}/{imageLimit}: {imageList[i].ModuleName}");
      
      if (!IsAcceptedModule(imageList[i])) {
        Trace.WriteLine($"BINARY_FILTER_DEBUG: Module FINAL RESULT: REJECTED at binary name allowlist stage: {imageList[i].ModuleName}");
        continue;
      }

      // Accept only images that have any samples from
      // all the images ones loaded in the process.
      int moduleIndex = topModules.FindIndex(pair => pair.Item1 == imageList[i]);
      bool acceptModule = moduleIndex >= 0;
      
      if (moduleIndex >= 0) {
        int sampleCount = topModules[moduleIndex].Item2;
        Trace.WriteLine($"BINARY_FILTER_DEBUG: Module found in top modules at position {moduleIndex + 1}: {imageList[i].ModuleName} with {sampleCount} samples");
      } else {
        Trace.WriteLine($"BINARY_FILTER_DEBUG: Module NOT found in top modules list: {imageList[i].ModuleName}");
        // Check if there's a similar module name that might be a match
        var similarModules = topModules.Where(tm => 
          tm.Item1.ModuleName.Contains(imageList[i].ModuleName, StringComparison.OrdinalIgnoreCase) ||
          imageList[i].ModuleName.Contains(tm.Item1.ModuleName, StringComparison.OrdinalIgnoreCase)
        ).ToList();
        
        if (similarModules.Any()) {
          Trace.WriteLine($"BINARY_FILTER_DEBUG: Found {similarModules.Count} modules with similar names:");
          foreach (var sim in similarModules) {
            int simIndex = topModules.IndexOf(sim);
            Trace.WriteLine($"BINARY_FILTER_DEBUG:   - {sim.Item1.ModuleName}: {sim.Item2} samples at position {simIndex + 1}");
          }
        }
      }
      
      if (!acceptModule) {
        Trace.WriteLine($"BINARY_FILTER_DEBUG: Module FINAL RESULT: REJECTED at top modules stage: {imageList[i].ModuleName} (not in top modules list)");
        continue;
      }

      var binaryFile = FromProfileImage(imageList[i]);

      if (symbolSettings.IsRejectedBinaryFile(binaryFile)) {
        Trace.WriteLine($"BINARY_FILTER_DEBUG: Module FINAL RESULT: REJECTED at previously failed stage: {imageList[i].ModuleName}");
        rejectedDebugModules_.Add(imageList[i]);
        continue;
      }

      Trace.WriteLine($"BINARY_FILTER_DEBUG: Module FINAL RESULT: ACCEPTED for binary download: {imageList[i].ModuleName}");

      binTaskList[i] = Task.Run(async () => {
        await binTaskSemaphore.WaitAsync();
        BinaryFileSearchResult result;

        try {
          result = await PEBinaryInfoProvider.LocateBinaryFileAsync(binaryFile, symbolSettings);
        }
        finally {
          binTaskSemaphore.Release();
        }

        return result;
      });
    }

    // Determine the compiler target for the new session.
    var irMode = IRMode.Default;

#if DEBUG
    var binSw = Stopwatch.StartNew();
#endif

    for (int i = 0; i < imageLimit; i++) {
      if (cancelableTask is {IsCanceled: true}) {
        return;
      }

      if (binTaskList[i] == null) {
        continue;
      }

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
        Trace.WriteLine($"Downloaded binary: {binaryFile.FilePath}");
        UpdateProgress(progressCallback, ProfileLoadStage.BinaryLoading, imageLimit, i,
                       Utilities.Utils.TryGetFileName(binaryFile.BinaryFile.ImageName));
      }
    }

#if DEBUG
    Trace.WriteLine($"Binary download time: {binSw.Elapsed}");
#endif

    // Locate the needed debug files, in parallel. This will download them
    // from the symbol server if not yet on local machine and enabled.
    int pdbCount = 0;
    var pdbTaskSemaphore = new SemaphoreSlim(12);

    Trace.WriteLine("=== DEBUG FILE SEARCH LOGGING TEST - This message should ALWAYS appear ===");
    Trace.WriteLine($"DEBUG_FILTER_DEBUG: Starting debug file search for {imageLimit} modules. Low sample cutoff: {moduleSampleCutOff}");

    for (int i = 0; i < imageLimit; i++) {
      if (cancelableTask is {IsCanceled: true}) {
        return;
      }

      if (binTaskList[i] == null) {
        Trace.WriteLine($"DEBUG_FILTER_DEBUG: Debug file search SKIPPED - Module rejected during binary filtering: {imageList[i].ModuleName} (path: {imageList[i].FilePath})");
        rejectedDebugModules_.Add(imageList[i]);
        continue; // Rejected module.
      }

      var binaryFile = await binTaskList[i].ConfigureAwait(false);

      int moduleIndex = topModules.FindIndex(pair => pair.Item1 == imageList[i]);
      bool acceptModule = moduleIndex >= 0 &&
                          topModules[moduleIndex].SampleCount > moduleSampleCutOff;

      if (!acceptModule) {
        if (moduleIndex < 0) {
          Trace.WriteLine($"DEBUG_FILTER_DEBUG: Debug file search SKIPPED - Module not in top modules list: {imageList[i].ModuleName} (path: {imageList[i].FilePath})");
        } else {
          long sampleCount = topModules[moduleIndex].SampleCount;
          Trace.WriteLine($"DEBUG_FILTER_DEBUG: Debug file search SKIPPED - Module sample count too low: {imageList[i].ModuleName} (samples: {sampleCount}, cutoff: {moduleSampleCutOff})");
        }
        rejectedDebugModules_.Add(imageList[i]);
        continue;
      }

      // Try to use ETL info if binary not available.
      var symbolFile = rawProfile.GetDebugFileForImage(imageList[i], mainProcess.ProcessId);

      if (symbolFile != null) {
        if (symbolSettings.IsRejectedSymbolFile(symbolFile)) {
          Trace.WriteLine($"DEBUG_FILTER_DEBUG: Debug file search SKIPPED - Symbol file previously rejected: {imageList[i].ModuleName} (symbol: {symbolFile})");
          rejectedDebugModules_.Add(imageList[i]);
          continue;
        }

        Trace.WriteLine($"DEBUG_FILTER_DEBUG: Debug file search STARTED - Using ETL symbol file descriptor: {imageList[i].ModuleName} (symbol: {symbolFile})");
        pdbTaskList[i] = Task.Run(async () => {
          await pdbTaskSemaphore.WaitAsync();
          DebugFileSearchResult result;

          try {
            result = await session_.CompilerInfo.FindDebugInfoFileAsync(symbolFile, symbolSettings);
          }
          finally {
            pdbTaskSemaphore.Release();
          }

          return result;
        });
      }
      else if (binaryFile is {Found: true}) {
        pdbCount++;

        Trace.WriteLine($"DEBUG_FILTER_DEBUG: Debug file search STARTED - Using binary file path: {imageList[i].ModuleName} (binary: {binaryFile.FilePath})");
        pdbTaskList[i] = Task.Run(async () => {
          await pdbTaskSemaphore.WaitAsync();
          DebugFileSearchResult result;

          try {
            result = session_.CompilerInfo.FindDebugInfoFileAsync(binaryFile.FilePath, symbolSettings).Result;
          }
          finally {
            pdbTaskSemaphore.Release();
          }

          return result;
        });
      }
      else {
        // Neither symbol file from ETL nor binary file available
        if (symbolFile == null) {
          Trace.WriteLine($"DEBUG_FILTER_DEBUG: Debug file search SKIPPED - No symbol file descriptor in ETL: {imageList[i].ModuleName} (path: {imageList[i].FilePath})");
        }
        if (binaryFile is {Found: false}) {
          Trace.WriteLine($"DEBUG_FILTER_DEBUG: Debug file search SKIPPED - Binary file not found: {imageList[i].ModuleName} (details: {binaryFile?.Details})");
        }
      }
    }

    UpdateProgress(progressCallback, ProfileLoadStage.SymbolLoading, pdbCount, 0);
#if DEBUG
    var sw = Stopwatch.StartNew();
#endif

    // Wait for the PDBs to be loaded.
    for (int i = 0; i < imageLimit; i++) {
      if (cancelableTask is {IsCanceled: true}) {
        return;
      }

      if (pdbTaskList[i] != null) {
        var pdbPath = await pdbTaskList[i].ConfigureAwait(false);

        if (pdbPath.Found) {
          UpdateProgress(progressCallback, ProfileLoadStage.SymbolLoading, pdbCount, i,
                         Utilities.Utils.TryGetFileName(pdbPath.SymbolFile.FileName));
        }
      }
    }

    UpdateProgress(progressCallback, ProfileLoadStage.SymbolLoading, pdbCount, 0);
#if DEBUG
    Trace.WriteLine($"PDB download time: {sw.Elapsed}");
#endif
  }

  private async Task<ProfileModuleBuilder> CreateModuleBuilderAsync(ProfileImage image, RawProfileData rawProfile, int processId,
                                                   SymbolFileSourceSettings symbolSettings) {
    var totalSw = Stopwatch.StartNew();
    var imageModule = new ProfileModuleBuilder(report_, session_);
    IDebugInfoProvider imageDebugInfo = null;

    Trace.WriteLine($"CreateModuleBuilderAsync: Starting for module {image.ModuleName}");

    // Time spent on initial setup
    var initSw = Stopwatch.StartNew();
    
    if (IsAcceptedModule(image)) {
      imageDebugInfo = rawProfile.GetDebugInfoForManagedImage(image, processId);

      if (imageDebugInfo != null) {
        imageDebugInfo.SymbolSettings = symbolSettings;
        Trace.WriteLine($"CreateModuleBuilderAsync: Found managed debug info for {image.ModuleName}");
      }
    }
    
    var initTime = initSw.Elapsed;

    // Time spent on module initialization
    var moduleInitSw = Stopwatch.StartNew();
    Trace.WriteLine($"CreateModuleBuilderAsync: Calling Initialize for {image.ModuleName}");
    
    try {
      bool moduleInitialized = await imageModule.Initialize(FromProfileImage(image), symbolSettings, imageDebugInfo).ConfigureAwait(false);
      var moduleInitTime = moduleInitSw.Elapsed;
      
      Trace.WriteLine($"CreateModuleBuilderAsync: Initialize completed for {image.ModuleName}, result: {moduleInitialized}");

      if (moduleInitialized) {
        // If binary couldn't be found, try to initialize using
        // the PDB signature from the trace file.

        if (rejectedDebugModules_.Contains(image)) {
#if DEBUG
          Trace.WriteLine($"CreateModuleBuilderAsync: Skipped rejected module {image.ModuleName}");
#endif
          return imageModule;
        }

        // Time spent on debug info file lookup
        var debugFileSw = Stopwatch.StartNew();
        var debugInfoFile = GetDebugInfoFile(imageModule.ModuleDocument.BinaryFile,
                                             image, rawProfile, processId, symbolSettings);
        var debugFileTime = debugFileSw.Elapsed;

        // Time spent on debug info initialization
        var debugInitSw = Stopwatch.StartNew();
        bool debugInitialized = false;
        if (debugInfoFile != null) {
          Trace.WriteLine($"CreateModuleBuilderAsync: Initializing debug info for {image.ModuleName}");
          debugInitialized = await imageModule.InitializeDebugInfo(debugInfoFile).ConfigureAwait(false);
          Trace.WriteLine($"CreateModuleBuilderAsync: Debug info initialization for {image.ModuleName}, result: {debugInitialized}");
        }
        else {
          Trace.WriteLine($"CreateModuleBuilderAsync: No debug info file found for {image.ModuleName}");
        }
        var debugInitTime = debugInitSw.Elapsed;

        if (debugInfoFile != null && !debugInitialized) {
          Trace.TraceWarning($"CreateModuleBuilderAsync: Failed to load debug debugInfo for image: {image.FilePath}");
        }

        var totalTime = totalSw.Elapsed;
        
        // Log modules that take significant time to create
        if (totalTime.TotalMilliseconds > 100) { // Log modules taking > 100ms
          Trace.WriteLine($"CreateModuleBuilderAsync: Slow module creation for {image.ModuleName}: {totalTime.TotalMilliseconds:F1}ms total " +
                         $"(init: {initTime.TotalMilliseconds:F1}ms, module: {moduleInitTime.TotalMilliseconds:F1}ms, " +
                         $"debugFile: {debugFileTime.TotalMilliseconds:F1}ms, debugInit: {debugInitTime.TotalMilliseconds:F1}ms)");
        }
      }
      else {
        var totalTime = totalSw.Elapsed;
        Trace.WriteLine($"CreateModuleBuilderAsync: Module initialization failed for {image.ModuleName} after {totalTime.TotalMilliseconds:F1}ms");
      }

      Trace.WriteLine($"CreateModuleBuilderAsync: Completed for module {image.ModuleName}");
      return imageModule;
    }
    catch (Exception ex) {
      Trace.WriteLine($"CreateModuleBuilderAsync: EXCEPTION for module {image.ModuleName}: {ex.GetType().Name}: {ex.Message}");
      Trace.WriteLine($"CreateModuleBuilderAsync: Stack trace: {ex.StackTrace}");
      throw;
    }
  }

  private DebugFileSearchResult GetDebugInfoFile(BinaryFileSearchResult binaryFile,
                                                 ProfileImage image, RawProfileData rawProfile, int processId,
                                                 SymbolFileSourceSettings symbolSettings) {
    if (binaryFile is {Found: true}) {
      return session_.CompilerInfo.FindDebugInfoFile(binaryFile.FilePath, symbolSettings);
    }
    else {
      // Try to use ETL info if binary not available.
      var symbolFile = rawProfile.GetDebugFileForImage(image, processId);

      if (symbolFile != null) {
        return session_.CompilerInfo.FindDebugInfoFile(symbolFile, symbolSettings);
      }
    }

    return null;
  }

  private bool IsAcceptedModule(ProfileImage image) {
    if (!options_.HasBinaryNameAllowedList) {
      Trace.WriteLine($"BINARY_FILTER_DEBUG: Module PASSED binary name allowlist check - No allowlist defined: {image.ModuleName} (path: {image.FilePath})");
      return true;
    }

    Trace.WriteLine($"BINARY_FILTER_DEBUG: Checking binary name allowlist for module: {image.ModuleName} (path: {image.FilePath})");
    Trace.WriteLine($"BINARY_FILTER_DEBUG: Binary name allowlist contents: [{string.Join(", ", options_.BinaryNameAllowedList)}]");

    foreach (string file in options_.BinaryNameAllowedList) {
      string fileName = Utilities.Utils.TryGetFileNameWithoutExtension(file);
      Trace.WriteLine($"BINARY_FILTER_DEBUG: Comparing '{image.ModuleName}' against allowlist entry '{fileName}' (original: '{file}')");

      if (fileName.Equals(image.ModuleName, StringComparison.OrdinalIgnoreCase)) {
        Trace.WriteLine($"BINARY_FILTER_DEBUG: Module PASSED binary name allowlist check - Matched '{fileName}': {image.ModuleName}");
        return true;
      }
    }

    Trace.WriteLine($"BINARY_FILTER_DEBUG: Module FAILED binary name allowlist check - Not found in allowlist: {image.ModuleName} (path: {image.FilePath})");
    return false;
  }

  private async Task<ProfileModuleBuilder> GetModuleBuilderAsync(RawProfileData rawProfile, ProfileImage queryImage, int processId,
                                                SymbolFileSourceSettings symbolSettings) {
    // prevImage_/prevModule_ are TLS variables since this is called from multiple threads.
    if (queryImage == prevImage_) {
      return prevProfileModuleBuilder_;
    }

    if (!imageModuleMap_.TryGetValue(queryImage.Id, out var imageModule)) {
      // TODO: Why not lock on queryImage?
      lock (imageLocks_[queryImage.Id % IMAGE_LOCK_COUNT]) {
        if (imageModuleMap_.TryGetValue(queryImage.Id, out imageModule)) {
          prevImage_ = queryImage;
          prevProfileModuleBuilder_ = imageModule;
          return imageModule;
        }
      }

      // Create the module builder outside the lock to avoid blocking other threads
      imageModule = await CreateModuleBuilderAsync(queryImage, rawProfile, processId, symbolSettings).ConfigureAwait(false);
      
      // Add to the cache
      imageModuleMap_.TryAdd(queryImage.Id, imageModule);
    }

    prevImage_ = queryImage;
    prevProfileModuleBuilder_ = imageModule;
    return imageModule;
  }

  private ProfileModuleBuilder GetModuleBuilder(RawProfileData rawProfile, ProfileImage queryImage, int processId,
                                                SymbolFileSourceSettings symbolSettings) {
    // prevImage_/prevModule_ are TLS variables since this is called from multiple threads.
    if (queryImage == prevImage_) {
      return prevProfileModuleBuilder_;
    }

    if (!imageModuleMap_.TryGetValue(queryImage.Id, out var imageModule)) {
      // TODO: Why not lock on queryImage?
      lock (imageLocks_[queryImage.Id % IMAGE_LOCK_COUNT]) {
        if (imageModuleMap_.TryGetValue(queryImage.Id, out imageModule)) {
          return imageModule;
        }

        // Fall back to sync version with deadlock risk - this is for compatibility
        imageModule = CreateModuleBuilderAsync(queryImage, rawProfile, processId, symbolSettings).ConfigureAwait(false).GetAwaiter().GetResult();
        imageModuleMap_.TryAdd(queryImage.Id, imageModule);
      }
    }

    prevImage_ = queryImage;
    prevProfileModuleBuilder_ = imageModule;
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
    //? TODO: Use ref foreach
    currentSampleIndex_ = 0;
    Trace.WriteLine($"Start process PMC at {DateTime.Now}");
    var sw = Stopwatch.StartNew();

    foreach (var counter in rawProfile.PerformanceCountersEvents) {
      if ((++index & PROGRESS_UPDATE_INTERVAL - 1) == 0) { // Update progress every 128K samples.
        if (cancelableTask is {IsCanceled: true}) {
          break;
        }

        UpdateProgress(progressCallback, ProfileLoadStage.PerfCounterProcessing,
                       rawProfile.PerformanceCountersEvents.Count, index);
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
        profileData_.AddModuleCounter(frameImage.ModuleName, counter.CounterId, 1);
        var profileModuleBuilder = GetModuleBuilder(rawProfile, frameImage, context.ProcessId, symbolSettings);

        if (profileModuleBuilder == null) {
          continue;
        }

        if (!profileModuleBuilder.Initialized || !profileModuleBuilder.HasDebugInfo) {
          //Trace.WriteLine($"Uninitialized module {image.FileName}");
          continue;
        }

        long frameRva = managedBaseAddress != 0 ?
          counter.IP : counter.IP - frameImage.BaseAddress;

        var funcPair = profileModuleBuilder.GetOrCreateFunction(frameRva);
        long funcRva = funcPair.DebugInfo.RVA;
        long offset = frameRva - funcRva;

        var profile = profileData_.GetOrCreateFunctionProfile(funcPair.Function, funcPair.DebugInfo);
        profile.AddCounterSample(offset, counter.CounterId, 1);
      }

      // profileData_.Events.Add((counter, null));
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

#if DEBUG
  private void RecordSampleStatistics(ResolvedProfileStack resolvedStack) {
    // Record statistics about the most common RVAs with exclusive samples.
    if (resolvedStack.FrameCount <= 0) return;

    lock (lockObject_) {
      perModuleSampleStatsMap_ ??= new();
      var topFrame = resolvedStack.StackFrames[0];

      if (topFrame.FrameDetails.Image == null) {
        return;
      }

      var moduleStats = perModuleSampleStatsMap_.GetOrAddValue(topFrame.FrameDetails.Image);

      if (!moduleStats.TryGetValue(topFrame.FrameRVA, out var rvaStats)) {
        rvaStats = (topFrame.FrameDetails.DebugInfo, 1);
        moduleStats[topFrame.FrameRVA] = rvaStats;
      }
      else {
        rvaStats.SampleCount++;
        moduleStats[topFrame.FrameRVA] = rvaStats;
      }
    }
  }

  private void PrintSampleStatistics() {
    lock (lockObject_) {
      if (perModuleSampleStatsMap_ == null) {
        return;
      }

      Trace.WriteLine("Per-module RVA sample stats");

      foreach (var moduleStats in perModuleSampleStatsMap_) {
        Trace.WriteLine($"--------------\n{moduleStats.Key.ModuleName}:");
        var rvaStats = moduleStats.Value.ToList();
        rvaStats.Sort((a, b) => b.Item2.SampleCount.CompareTo(a.Item2.SampleCount));

        foreach (var (rva, sampleStats) in rvaStats) {
          Trace.WriteLine($" - RVA {rva:X}: {sampleStats.SampleCount} samples, func: {sampleStats.Info.Name}");
        }
      }
    }
  }
#endif
}