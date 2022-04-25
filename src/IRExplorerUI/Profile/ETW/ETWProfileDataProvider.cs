﻿#define USE_STREAMING

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using IRExplorerCore;
using IRExplorerCore.IR;
using IRExplorerCore.IR.Tags;
using System.Linq;
using System.Threading;
using System.Text;
using System.Collections;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows;
using IRExplorerUI.Compilers;
using IRExplorerUI.Compilers.ASM;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Symbol;
using System.Windows.Markup;
using IRExplorerCore.Utilities;
using OxyPlot.Axes;
using OxyPlot;

namespace IRExplorerUI.Profile {
    public sealed class ETWProfileDataProvider : IProfileDataProvider, IDisposable {
        private ProfileDataProviderOptions options_;
        private ISession session_;
        private ICompilerInfoProvider compilerInfo_;
        private IRTextSummary mainSummary_;
        private ModuleInfo mainModule_;
        private ProfileData profileData_;
        
        static List<ProfileSample> temp_;
        
        public ETWProfileDataProvider(IRTextSummary summary, ISession session) {
            mainSummary_ = summary;
            session_ = session;
            profileData_ = new ProfileData();

            //events_ = new List<PerformanceCounterEvent>();
            events_ = new ChunkedList<PerformanceCounterEvent>();
        }

        public ProfileData LoadTrace(string tracePath, string imageName, 
              ProfileDataProviderOptions options,
              SymbolFileSourceOptions symbolOptions, 
              ProfileLoadProgressHandler progressCallback,
              CancelableTask cancelableTask) {
            return LoadTraceAsync(tracePath, imageName, options, symbolOptions,
                                  progressCallback, cancelableTask).Result;
        }
        
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
        
        //private void CollectPerformanceCounterEvents(EventContext e) {
        //    if (e.Event.Id == 47) {
        //        if (e.Event.Data.Length > 0) {
        //            events_.Add(new PerformanceCounterEvent() {
        //                Time = e.Event.Timestamp.Nanoseconds,
        //                IP = ReadInt64(e.Event.Data), 
        //                ProfilerSource = ReadInt16(e.Event.Data, 12), 
        //                ProcessId = e.Event.ProcessId ?? -1,
        //                ThreadId = e.Event.ThreadId ?? ReadInt32(e.Event.Data, 8),
        //            });

        //            //if (times_++ % 50000 == 0) {
        //            //    Trace.WriteLine($"Events {times_}, {(double)times_ / 1000000} M");
        //            //    long memory2 = GC.GetTotalMemory(true);
        //            //    Trace.WriteLine($"    Mem usage: {(double)(memory2 - memory_) / (1024 * 1024 * 1024)} GB");
        //            //    var objSize = GetSizeOfObject(new PerformanceCounterEvent());
        //            //    Trace.WriteLine($"    Events mem: {(double)((long)events_.Count * objSize) / (1024 * 1024 * 1024)} GB, event size: {objSize} b");
        //            //    Trace.Flush();
        //            //}
        //        }
        //    }
        //    else if (e.Event.Id == 50) {
        //        ; // DispatcherReadyThreadTraceData needs THREAD+PROC xperf
        //    }
        //    else if (e.Event.Id == 0x49) {
        //        if (e.Event.Data.Length > 0) {
        //            var counter = ReadPerformanceCounterInfo(e.Event.Data);
        //            profileData_.RegisterPerformanceCounter(counter);

        //            //if (times_++ % 50000 == 0) {
        //            //    Trace.WriteLine($"Events {times_}, {(double)times_ / 1000000} M");
        //            //    long memory2 = GC.GetTotalMemory(true);
        //            //    Trace.WriteLine($"    Mem usage: {(double)(memory2 - memory_) / (1024 * 1024 * 1024)} GB");
        //            //    var objSize = GetSizeOfObject(new PerformanceCounterEvent());
        //            //    Trace.WriteLine($"    Events mem: {(double)((long)events_.Count * objSize) / (1024 * 1024 * 1024)} GB, event size: {objSize} b");
        //            //    Trace.Flush();
        //            //}
        //        }
        //    }
        //}

        //private BinaryFileDescription FromETLImage(IImage image) {
        //    if (image == null) {
        //        return new BinaryFileDescription(); // Main module
        //    }

        //    var imageName = image.OriginalFileName;

        //    if (string.IsNullOrEmpty(imageName)) {
        //        imageName = image.FileName;
        //    }

        //    return new BinaryFileDescription() {
        //        ImageName = imageName,
        //        ImagePath = image.Path,
        //        Checksum = image.Checksum,
        //        TimeStamp = image.Timestamp,
        //        ImageSize = image.Size.Bytes,
        //        MajorVersion = image.FileVersionNumber?.Major ?? 0,
        //        MinorVersion = image.FileVersionNumber?.Minor ?? 0
        //    };
        //}

        private BinaryFileDescription FromProfileImage(ProfileImage image) {
            return new BinaryFileDescription() {
                ImageName = image.ModuleName,
                ImagePath = image.FilePath,
                Checksum = image.Checksum,
                TimeStamp = image.TimeStamp,
                ImageSize = image.Size,
            };
        }

        private BinaryFileDescription FromSummary(IRTextSummary summary) {
            return new BinaryFileDescription();
        }

        public async Task<ProfileData> LoadTraceAsync(string tracePath, string imageName,
              ProfileDataProviderOptions options,
              SymbolFileSourceOptions symbolOptions,
              ProfileLoadProgressHandler progressCallback,
              CancelableTask cancelableTask) {
            try {
                // Extract just the file name.
                options_ = options;
                times_ = 0;
                var initialImageName = imageName;
                imageName = Utils.TryGetFileNameWithoutExtension(imageName);

                //var imageModuleMap = new Dictionary<int, ModuleInfo>();
                var imageModuleMap = new ConcurrentDictionary<int, ModuleInfo>();

                // The entire ETW processing must be done on the same thread.
                bool result = await Task.Run(async () => {
                    Trace.WriteLine($"Init at {DateTime.Now}");

                    // GC.Collect();
                    // GC.WaitForPendingFinalizers();
                    // long memoryUse = GC.GetTotalMemory(true);
                    
                    progressCallback?.Invoke(new ProfileLoadProgress(ProfileLoadStage.TraceLoading) {
                        Total = 0,
                        Current = 0
                    });

                    var totalSw = Stopwatch.StartNew();
                    RawProfileData prof = new();

                    //? Maybe faster to process twice - once to get proc list, then get samples/stacks just for one proc?
                    //?   OR multiple threads, each handling diff processes
                    using (var source = new ETWTraceEventSource(tracePath)) {
                        double lastTime = 0;

                        double[] perCoreLastTime = new double[4096];
                        int[] perCoreLastSample = new int[4096];
                        var perContextLastSample = new Dictionary<int, int>();

                        bool samplingIntervalSet = false;
                        int samplingInterval100NS; 
                        const double samplingErrorMargin = 1.1; // 10% deviation from sampling interval allowed.
                        double samplingIntervalMS;
                        double samplingIntervalLimitMS;
                        var shaBuffer = new byte[20].AsMemory();

                        void UpdateSamplingInterval(int value) {
                            samplingInterval100NS = value;
                            samplingIntervalMS = (double)samplingInterval100NS / 10000;
                            samplingIntervalLimitMS = samplingIntervalMS * samplingErrorMargin;
                            Trace.WriteLine($"=> UPDATE {value} to {samplingIntervalMS} ms");
                        }

                        // Default 1ms sampling interval, 1M ns.
                        UpdateSamplingInterval(10000);

                        ImageIDTraceData lastImageIdData = null;
                        ProfileImage lastProfileImage = null;
                        long lastProfileImageTime = 0;

                        var psw = Stopwatch.StartNew();


                        //BlockingCollection<StackWalkStackTraceData> queue = new BlockingCollection<StackWalkStackTraceData>();

                        var symbolParser = new SymbolTraceEventParser(source);

                        symbolParser.ImageID += data => {
                            if (lastProfileImage != null && lastProfileImageTime == data.TimeStampQPC) {
                                lastProfileImage.OriginalFileName = data.OriginalFileName;

                                if (lastProfileImage.TimeStamp == 0) {
                                    lastProfileImage.TimeStamp = data.TimeDateStamp;
                                }
                            }
                            else {
                                // The ImageGroup event should show up later in the stream.
                                lastImageIdData = (ImageIDTraceData)data.Clone();
                            }
                        };

                        //? PDB info - could allow downloading PDBs before EXEs
                        //symbolParser.ImageIDDbgID_RSDS

                        source.Kernel.ProcessStartGroup += data => {
                            var proc = new ProfileProcess(data.ProcessID, data.ParentID,
                                                          data.ProcessName, data.ImageFileName,
                                                          data.CommandLine);
                            //Trace.WriteLine($"proc: {proc}");
                            prof.AddProcess(proc);
                        };
                        
                        source.Kernel.ImageGroup += data => {
                            string originalName = null;
                            int timeStamp = data.TimeDateStamp;
                            bool sawImageId = false;

                            if (lastImageIdData != null && lastImageIdData.TimeStampQPC == data.TimeStampQPC) {
                                // The ImageID event showed up earlier in the stream.
                                sawImageId = true;
                                originalName = lastImageIdData.OriginalFileName;

                                if (timeStamp == 0) {
                                    timeStamp = lastImageIdData.TimeDateStamp;
                                }
                            }

                            var image = new ProfileImage(data.FileName, originalName, (long)data.ImageBase,
                                                         (long)data.DefaultBase, data.ImageSize,
                                                         timeStamp, data.ImageChecksum);

                            //Trace.WriteLine($"ImageGroup: {image}, Proc: {data.ProcessID}");
                            int imageId = prof.AddImageToProcess(data.ProcessID, image);

                            if (!sawImageId) {
                                // The ImageID event may show up later in the stream.
                                lastProfileImage = prof.FindImage(imageId);
                                lastProfileImageTime = data.TimeStampQPC;
                            }
                            else {
                                lastProfileImage = null;
                            }
                        };
                        
                        source.Kernel.ThreadStartGroup += data => {
                            var thread = new ProfileThread(data.ThreadID, data.ProcessID, data.ThreadName);
                            prof.AddThreadToProcess(data.ProcessID, thread);
                            //Trace.WriteLine($"thread: {thread}");
                        };

                        source.Kernel.EventTraceHeader += data => {
                            // data.PointerSize;
                        };

                        source.Kernel.StackWalkStack += data => {
                            //if (data.ProcessID != mainProcessId) {
                            //    return;
                            //}
                            
                            //queue.Add((StackWalkStackTraceData)data.Clone());
                            var context = prof.RentTempContext(data.ProcessID, data.ThreadID, data.ProcessorNumber);
                            int contextId = prof.AddContext(context);

                            int frameCount = data.FrameCount;
                            var stack = prof.RentTemporaryStack(frameCount, contextId);
                            
                            // Copy data from event to the temp. stack pointer array.
                            unsafe {
                                void* ptr = (void*)((IntPtr)(void*)data.DataStart + 16);
                                int bytes = data.PointerSize * frameCount;
                                var span = new Span<byte>(ptr, bytes);

                                fixed (long* destPtr = stack.FramePointers) {
                                    var destSpan = new Span<byte>(destPtr, bytes);
                                    span.CopyTo(destSpan);
                                }
                            }

                            //for (int i = 0; i < data.FrameCount; i++) {
                            //    stack.FramePointers[i] = (long)data.InstructionPointer(i);
                            //}
                            
                            int stackId = prof.AddStack(stack, context);

                            // Try to associate with a previous sample from the same context.
                            int sampleId = perCoreLastSample[data.ProcessorNumber];

                            if (!prof.TrySetSampleStack(sampleId, stackId, contextId)) {
                                if(perContextLastSample.TryGetValue(contextId, out sampleId)) {
                                    prof.SetSampleStack(sampleId, stackId, contextId);
                                }
                            }
                            
                            prof.ReturnStack(stackId);
                            prof.ReturnContext(contextId);

                            //Trace.WriteLine("-------------------------------");

                            //for (int i = 0; i < data.FrameCount; i++) {
                            //  var frameIP = data.InstructionPointer(i);
                            //  Trace.WriteLine($"Stack {frameIP:X}, {data.ProcessID}, t {data.ThreadID}, p {data.ProcessorNumber}");
                            //}
                            //    break;
                            //}
                            //    PrintIPInfo(frameIP, data.ThreadID, out var _, out var _, out var _);
                            //    Trace.WriteLine("");
                            //}
                        };

                        source.Kernel.PerfInfoCollectionStart += data => {
                            if (data.SampleSource == 0) {
                                Trace.WriteLine($"=> PerfInfoCollectionStart: new {data.NewInterval}, old {data.OldInterval}");
                                UpdateSamplingInterval(data.NewInterval);
                                samplingIntervalSet = true;
                            }
                        };

                        source.Kernel.PerfInfoSetInterval += data => {
                            if (data.SampleSource == 0 && !samplingIntervalSet) {
                                Trace.WriteLine($"=> PerfInfoCollectionStart: new {data.NewInterval}, old {data.OldInterval}");
                                UpdateSamplingInterval(data.OldInterval);
                                samplingIntervalSet = true;
                            }
                        };

                        
                        source.Kernel.PerfInfoSample += data => {
                            // if (data.ProcessID != mainProcessId) {
                            //     return;
                            // }
                            int cpu = data.ProcessorNumber;
                            double timestamp = data.TimeStampRelativeMSec;

                            bool isKernelCode = data.ExecutingDPC || data.ExecutingISR;
                            double weight = timestamp - perCoreLastTime[cpu];

                            if (weight > samplingIntervalLimitMS) {
                                weight = samplingIntervalMS;
                            }

                            perCoreLastTime[cpu] = timestamp;
                            
                            // Skip unknown process.
                            if (data.ProcessID < 0) {
                                return;
                            }

                            // Skip idle thread on non-kernel code.
                            if (data.ThreadID == 0 && !isKernelCode) {
                                return;
                            }
                            
                            var context = prof.RentTempContext(data.ProcessID, data.ThreadID, cpu);
                            int contextId = prof.AddContext(context);
                            var sample = new ProfileSample((long)data.InstructionPointer,
                                                            TimeSpan.FromMilliseconds(timestamp),
                                                            TimeSpan.FromMilliseconds(weight),
                                                            isKernelCode, contextId);
                            int sampleId = prof.AddSample(sample);
                            prof.ReturnContext(contextId);

                            perCoreLastSample[cpu] = sampleId;
                            perContextLastSample[contextId] = sampleId;

                            //Trace.WriteLine("-------------------------------");
                            //Trace.WriteLine($"Sample {data.InstructionPointer:X}, {data.ProcessID}, t {data.ThreadID}, p {data.ProcessorNumber}");
                            //Trace.WriteLine($"      Kernel: {data.ExecutingDPC || data.ExecutingISR}");
                            //bool found = PrintIPInfo(data.InstructionPointer, data.ThreadID, out var funcInfo, out var offset, out var module);
                            //Trace.WriteLine("");

                            //if (found) {
                            //    var textFunction = module.FindFunction(funcInfo.StartRVA, out bool isExternalFunc);

                            //    if (textFunction != null) {
                            //        var profile = profileData_.GetOrCreateFunctionProfile(textFunction, null);
                            //        profile.DebugInfo = funcInfo;
                            //        var sampleWeight = TimeSpan.FromMilliseconds(weight);
                            //        profile.AddInstructionSample(offset, sampleWeight);
                            //        profile.Weight += sampleWeight;
                            //        profile.ExclusiveWeight += sampleWeight;
                            //    }
                            //}
                        };

                        //void ReceivingThread() {
                        //    try {
                        //        foreach (var data in queue.GetConsumingEnumerable(cancelableTask.Token)) {
                        //            var context = new ProfileContext(data.ProcessID, data.ThreadID,
                        //                                             data.ProcessorNumber);
                        //            var contextId = prof.AddContext(context);
                        //            var stack = new ProfileStack(contextId, data.FrameCount);

                        //            for (int i = 0; i < data.FrameCount; i++) {
                        //                stack.FramePointers[i] = (long)data.InstructionPointer(i);
                        //            }

                        //            int stackId = prof.AddStack(stack, context);

                        //            //? TODO: maybe set for each not having a context?
                        //            if (!prof.SetLastSampleStack(stackId, contextId)) {
                        //                //Trace.WriteLine("---------------------");
                        //                //Trace.WriteLine($" search ctx {context}");
                        //                //bool r = prof.SetLastSampleStack(stackId, contextId, 10);
                        //                //Trace.WriteLine($"Failed to set sample stack, with maxStep {r}");
                        //            }
                        //        }
                        //    }
                        //    catch (OperationCanceledException) {

                        //    }
                        //}

                        //var thread = new Thread(ReceivingThread);
                        //thread.Start();
                        //queue.CompleteAdding();

                        source.Process();
                        psw.Stop();

                        // GC.Collect();
                        // GC.WaitForPendingFinalizers();
                        // long memoryUseAfter = GC.GetTotalMemory(true);
                        //Trace.WriteLine($"    Mem usage: {(double)(memoryUseAfter - memoryUse) / (1024 * 1024)} MB");

                        Trace.WriteLine($"Read time: {psw.Elapsed}");
                        Trace.WriteLine($"    stacks: {prof.stacks_.Count}");
                        Trace.WriteLine($"    samples: {prof.samples_.Count}");
                        Trace.WriteLine($"    ctxs: {prof.contexts_.Count}");
                        Trace.WriteLine($"    procs: {prof.processes_.Count}");
                        Trace.WriteLine($"    imgs: {prof.imagesMap_.Count}");
                        Trace.WriteLine($"    threads: {prof.threadsMap_.Count}");
                        
                        // Start getting the function address data while the trace is loading.
                        progressCallback?.Invoke(new ProfileLoadProgress(ProfileLoadStage.TraceLoading));
                    }

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

                    var binSearchOptions = (SymbolFileSourceOptions)symbolOptions.Clone();
                    binSearchOptions.InsertSymbolPath(initialImageName);
                    binSearchOptions.InsertSymbolPath(tracePath);

                    ModuleInfo FindModuleInfo(ProfileImage queryImage) {
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
                                        imageModule = new ModuleInfo(options_, session_);

                                        //? Needs some delay-load, can't disasm every dll for no reason
                                        //? - now Initialize uses a whitelist
                                        var sw2 = Stopwatch.StartNew();
                                        Trace.WriteLine($"Start loading image {queryImage.FilePath}");

                                        if (!IsAcceptedModule(queryImage.FilePath)) {
                                            imageModuleMap.TryAdd(queryImage.Id, imageModule);

                                            Trace.TraceInformation($"Ignore not whitelisted image {queryImage.FilePath}");
                                            return null;
                                        }

                                        //? TODO: New doc not registerd properly with session, reload crashes
                                        if (imageModule.Initialize(FromProfileImage(queryImage), binSearchOptions).
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

                    var mainProcess = prof.FindProcess(imageName, true);

                    if (mainProcess == null) {
                        return false;
                    }

                    var mainProcessId = mainProcess.ProcessId;
                    //prof.PrintSamples(mainProcessId);

                    var imageList = mainProcess.Images(prof).ToList();

                    if (imageList == null || imageList.Count == 0) {
                        var imageSet = new HashSet<ProfileImage>();

                        foreach (var sample in prof.samples_) {
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
                    var binTaskList = new Task<string>[imageLimit];
                    var pdbTaskList = new Task<string>[imageLimit];
                    

                    for (int i = 0; i < imageLimit; i++) {
                        var binaryFile = FromProfileImage(imageList[i]);
                        binTaskList[i] = PEBinaryInfoProvider.LocateBinaryFile(binaryFile, binSearchOptions);
                        //? TODO: Immediately after bin download PDB can be too binTaskList[i].ContinueWith()
                    }

                    // Determine the compiler target for the new session.
                    IRMode irMode = IRMode.Default;

                    for (int i = 0; i < imageLimit; i++) {
                        if (binTaskList[i] == null) continue;
                        var binaryFilePath = await binTaskList[i].ConfigureAwait(false);

                        if (irMode == IRMode.Default && File.Exists(binaryFilePath)) {
                            var binaryInfo = PEBinaryInfoProvider.GetBinaryFileInfo(binaryFilePath);

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
                    await session_.StartNewSession(tracePath, SessionKind.FileSession, new ASMCompilerInfoProvider(irMode, session_)).ConfigureAwait(false);

                    for (int i = 0; i < imageLimit; i++) {
                        var binaryFilePath = await binTaskList[i].ConfigureAwait(false);

                        if (File.Exists(binaryFilePath)) {
                            Trace.WriteLine($"Loaded BIN: {binaryFilePath}");
                            var name = Utils.TryGetFileNameWithoutExtension(imageList[i].FilePath);
                            acceptedImages.Add(name.ToLowerInvariant());

                            pdbTaskList[i] = session_.CompilerInfo.FindDebugInfoFile(binaryFilePath);
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
                    chunks = 1;
                    Trace.WriteLine($"Using {chunks} threads");

                    var tasks = new List<Task>();
                    var taskScheduler = new ConcurrentExclusiveSchedulerPair(TaskScheduler.Default, chunks);
                    var taskFactory = new TaskFactory(taskScheduler.ConcurrentScheduler);

                    int chunkSize = (int)(prof.samples_.Count / chunks);
                    var lockObject = new object();

                    //MessageBox.Show("Start tasks");

                    //var resolvedStacks = new ConcurrentDictionary<int, ResolvedProfileStack>();

                    var callTree = new ProfileCallTree();

                    for (int k = 0; k < chunks; k++) {
                        int start = k * chunkSize;
                        int end = Math.Min((k + 1) * chunkSize, (int)prof.samples_.Count);
                        Trace.WriteLine($"Chunk {k}: {start} - {end}");

                        tasks.Add(taskFactory.StartNew(() => {
                        //{
                            var stackFuncts = new HashSet<IRTextFunction>();
                            var stackModules = new HashSet<int>();


                            for (int i = start; i < end; i++) {
                                index++;

                                var sample = prof.samples_[i]; //? Avoid copy, use ref

                                if (!options.IncludeKernelEvents &&
                                    sample.IsKernelCode) {
                                    continue; //? TODO: Is this all kernel?
                                }

                                var context = sample.GetContext(prof);

                                if (context.ProcessId != mainProcessId) {
                                    continue;
                                }

                                if (index % 20000 == 0) {
                                    if (cancelableTask != null && cancelableTask.IsCanceled) {
                                        return;
                                    }

                                    progressCallback?.Invoke(new ProfileLoadProgress(ProfileLoadStage.TraceProcessing) {Total = (int)prof.samples_.Count, Current = index});
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

                                IRTextFunction prevStackFunc = null;
                                FunctionProfileData prevStackProfile = null;
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

                                        var funcInfo = resolvedFrame.FunctionInfo;
                                        var funcName = funcInfo.Name;
                                        var funcRva = funcInfo.RVA;
                                        var textFunction = resolvedFrame.Function;
                                        var profile = resolvedFrame.Profile;

                                        lock (profile) {
                                            profile.DebugInfo = funcInfo;
                                            var offset = resolvedFrame.FrameIP - funcRva;

                                            // Don't count the inclusive time for recursive functions multiple times.
                                            if (stackFuncts.Add(textFunction)) {
                                                profile.AddInstructionSample(offset, sampleWeight);
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

                                            //? TODO: Expensive, do as post-processing
                                            // Try to map the sample to all the inlined functions.
                                            if (options_.MarkInlinedFunctions && resolvedFrame.Module.HasDebugInfo &&
                                                textFunction.Sections.Count > 0) {
                                                ProcessInlineeSample(sampleWeight, offset, textFunction, resolvedFrame.Module);
                                            }
                                        }

                                        prevStackProfile = profile;
                                        isTopFrame = false;
                                        prevStackFunc = textFunction;
                                    }
                                }
                                else {
                                    resolvedStack = new ResolvedProfileStack(stack.FrameCount, context);
                                    var stackFrames = stack.FramePointers;

                                    //? TODO: Stacks with >256 frames are truncated, inclusive time computation is not right then
                                    //? for ex it never gets to main. Easy example is a quicksort impl
                                    foreach (var frameIp in stackFrames) {
                                        //? Use Frame -> Resolved Frame cache
                                        ProfileImage frameImage = prof.FindImageForIP(frameIp, context);

                                        if (frameImage == null) {
                                            resolvedStack.AddFrame(ResolvedProfileStackFrame.Unknown);
                                            prevStackFunc = null;
                                            prevStackProfile = null;
                                            isTopFrame = false;
                                            continue;
                                        }

                                        // Count exclusive time for each module in the executable. 
                                        if (isTopFrame && stackModules.Add(frameImage.Id)) {

                                            //? TODO: Use info from FindModuleInfo
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
                                        module = FindModuleInfo(frameImage);
                                        //modSw.Stop();
                                        //if (modSw.ElapsedMilliseconds > 500) {
                                        //    Trace.WriteLine($"=> Slow load {modSw.Elapsed}: {frameImage.Name}");
                                        //}

                                        if (module != null && module.HasDebugInfo) {
                                            //(funcName, funcRva) = module.FindFunctionByRVA(frameRva);
                                            frameRva = frameIp - frameImage.BaseAddress;

                                            //lock (lockObject) {
                                            funcInfo = module.FindDebugFunctionInfo(frameRva);
                                            //}

                                            funcName = funcInfo.Name;
                                            funcRva = funcInfo.RVA;
                                        }

                                        if (funcName == null) {
                                            resolvedStack.AddFrame(ResolvedProfileStackFrame.Unknown);
                                            prevStackFunc = null;
                                            prevStackProfile = null;
                                            isTopFrame = false;
                                            continue;
                                        }

                                        var textFunction = module.FindFunction(funcRva, out bool isExternalFunc);

                                        if (textFunction == null) {
                                            //if (missing.Add(funcName)) {
                                            //    Trace.WriteLine($"  - Skip missing frame {funcName} in {frame.Image.FileName}");
                                            //}
                                            resolvedStack.AddFrame(ResolvedProfileStackFrame.Unknown);
                                            prevStackFunc = null;
                                            prevStackProfile = null;
                                            isTopFrame = false;

                                            //prevFrames.Add($"<MISSING> {funcName} " + (frame.Symbol != null ? "SYM" : "NOSYM") );
                                            continue;
                                        }

                                        var profile = profileData_.GetOrCreateFunctionProfile(textFunction, null);


                                        lock (profile) {
                                            profile.DebugInfo = funcInfo;
                                            var offset = frameRva - funcRva;

                                            // Don't count the inclusive time for recursive functions multiple times.
                                            if (stackFuncts.Add(textFunction)) {
                                                profile.AddInstructionSample(offset, sampleWeight);
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

                                            //? TODO: Expensive, do as post-processing
                                            // Try to map the sample to all the inlined functions.
                                            if (options_.MarkInlinedFunctions && module.HasDebugInfo &&
                                                textFunction.Sections.Count > 0) {
                                                ProcessInlineeSample(sampleWeight, offset, textFunction, module);
                                            }

                                            resolvedStack.AddFrame(new ResolvedProfileStackFrame(frameIp, funcInfo, textFunction, frameImage, module, profile));
                                        }
                                        //}

                                        prevStackProfile = profile;
                                        isTopFrame = false;
                                        prevStackFunc = textFunction;
                                    }

                                    stack.SetOptionalData(resolvedStack);
                                }

                                // Build call tree.
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

                    //profileData_.Complete();

                    //Trace.WriteLine(callTree.PrintSamples());
                    //prof.PrintSamples(0);

                    Trace.WriteLine($"Done process samples in {sw.Elapsed}");
                    //MessageBox.Show($"Done in {sw.Elapsed}");


                    //Trace.WriteLine(callTree.Print());
                    //Trace.Flush();
                    // END SAMPLES PROC

#if false
                    Trace.WriteLine($"Start process PMC at {DateTime.Now}");
                    Trace.Flush();

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
                            var funcInfo = module.FindDebugFunctionInfo(counterRVA);

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

                    Trace.WriteLine($"Done process PMC in {sw.Elapsed}");
                    Trace.WriteLine($"Done loading profile in {swTotal.Elapsed}");
#endif


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

                    Trace.WriteLine($"Done in {totalSw.Elapsed}");
                    //Trace.Flush();
                    return true;
                });

                // Add 
                if (result) {
                    //ShowGraph(traceProfile);
                    LoadedDocument exeDocument = null;
                    var otherDocuments = new List<LoadedDocument>();

                    foreach (var pair in imageModuleMap) {
                        if (pair.Value.ModuleDocument == null) {
                            continue;
                        }

                        if (Utils.IsExecutableFile(pair.Value.ModuleDocument.BinaryFilePath)) {
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

                    Trace.WriteLine($"Using exe document {exeDocument?.ModuleName}");
                    session_.SessionState.MainDocument = exeDocument;
                    await session_.SetupNewSession(exeDocument, otherDocuments);
                }

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

        private void ShowGraph(RawProfileData profile) {
            var dict = new Dictionary<int, TimeSpan>();
            var start = long.MaxValue;
            var end = long.MinValue;
            var sw = Stopwatch.StartNew();

            long maxSample = 0;

            foreach (var s in profile.samples_) {
                maxSample = Math.Max(maxSample, s.Weight.Ticks);
            }

            Trace.WriteLine($"0_MaxSample: {sw.ElapsedMilliseconds}");
            sw.Restart();

            foreach (var s in profile.samples_) {
                dict.AccumulateValue(s.GetContext(profile).ProcessId, s.Weight);
                start = Math.Min(start, s.Weight.Ticks);
                end = Math.Max(end, s.Weight.Ticks);
            }

            Trace.WriteLine($"1_PerProcTime: {sw.ElapsedMilliseconds}");

            var r = new Random();

            //for(int i = 0; i < profile.PerfCounters.Count; i++) {
            //    var c = profile.PerfCounters[i];
            //    c.ProcessId = r.Next(0, 500);
            //    profile.PerfCounters[i] = c;
            //}

            //sw.Restart();
            //var dict2 = new Dictionary<int, TimeSpan>();


            //for (int k = 0; k < 5; k++) {
            //    dict2.Clear();
            //    var sw4 = Stopwatch.StartNew();

            //    foreach (var s in profile.PerfCounters) {
            //        dict2.AccumulateValue(s.ProcessId, TimeSpan.FromMilliseconds(1));
            //    }

            //    Trace.WriteLine($"PMCiter: {sw4.ElapsedMilliseconds}, {sw4.Elapsed}");
            //}

            //Trace.WriteLine($"1_PMC: {dict2.Count}, {sw.ElapsedMilliseconds}");
            //foreach (var keyValuePair in dict2)
            //{
            //    Trace.Write($"{keyValuePair.Key}, ");
            //}


            //{
            //    using var ts = new StreamWriter(@"C:\work\pmc_counters.csv");
            //    foreach (var s in profile.PerfCounters) {
            //        ts.WriteLine($"{s.Time}, {s.IP}, {s.RVA}, {s.ProcessId}, {s.ThreadId}, {s.ProfilerSource}");
            //    }
            //}

            //{
            //    using var ts = new StreamWriter(@"C:\work\pmc_counters2.csv");
            //    for(int i = 0; i < profile.PerfCounters.Count / 8; i++) {
            //        var s = profile.PerfCounters[i];
            //        ts.WriteLine($"{s.Time}, {s.IP}, {s.RVA}, {s.ProcessId}, {s.ThreadId}, {s.ProfilerSource}");
            //    }
            //}

            //{
            //    using var ts = new StreamWriter(@"C:\work\pmc_samples.csv");
            //    foreach (var s in profile.Samples) {
            //        ts.WriteLine($"{s.RVA}, {s.Time}, {s.Weight.Ticks}, {s.ProcessId}, {s.ThreadId}, {s.ImageId}, {s.StackFrameId}, {s.ProcessorCore}");
            //    }
            //}

            var sw2 = Stopwatch.StartNew();

            //? - process timeline
            //? - per-proc thread timeline
            //? - per-proc module timeline
            //? - per-proc core timeline
            //? o time range filter
            //? o group samples by process (cache)


            // 1 tick = 100 ns
            long resolution = 100;
            var timeDiffNs = (end - start) / resolution;
            var timeDiff = TimeSpan.FromTicks(timeDiffNs);
            var timePerSlice = TimeSpan.FromMilliseconds(10);
            double width = timeDiff.Ticks / timePerSlice.Ticks;
            var sliceSeriesDict = new Dictionary<int, Dictionary<int, TimeSpan>>();
            List<Tuple<int, Dictionary<int, TimeSpan>>> sliceSeriesList = null;

            for (int k = 0; k < 1; k++) {
                var sw3 = Stopwatch.StartNew();

                sliceSeriesDict.Clear();
                foreach (var s in profile.samples_) {
                    var point = (s.Time.Ticks - start) / resolution;
                    var slice = (int)(point / timePerSlice.Ticks);

                    //? Add extension method TryGetOrAddValue
                    var sliceDict = sliceSeriesDict.GetOrAddValue(s.GetContext(profile).ProcessId);
                    sliceDict.AccumulateValue(slice, s.Weight);
                }

                var list = dict.ToList();
                list.Sort((a, b) => a.Item2.CompareTo(b.Item2));

                foreach (var x in list) {
                    var proc = profile.GetOrCreateProcess(x.Item1);
                    Trace.WriteLine($"{proc.Name}: {x.Item2}");
                }

                sliceSeriesList = sliceSeriesDict.ToList();
                sliceSeriesList.Sort((a, b) => {
                    if (!dict.TryGetValue(a.Item1, out var pa) ||
                        !dict.TryGetValue(b.Item1, out var pb)) {
                        return 0;
                    }

                    return -pa.CompareTo(pb);
                });

                Trace.WriteLine($"Iter_PerProcSliceTime: {sw3.ElapsedMilliseconds}, {sw3.Elapsed}");
            }

            Trace.WriteLine($"2_PerProcSliceTime: {sw.ElapsedMilliseconds}, {sw.Elapsed}");
            Trace.WriteLine($"2_PerProcSliceTime: {sw2.ElapsedMilliseconds}, {sw2.Elapsed}");
            Trace.Flush();

            Trace.WriteLine($"Samples: {profile.samples_.Count}");
            Trace.WriteLine($"Time: {timeDiff}, {timeDiff.TotalMinutes}");


            //foreach (var pair in list) {
            //    var p = profile.FindProcess(pair.Item1);
            //    Trace.WriteLine($"{p.Id}, {p?.Name}: {pair.Item2}");
            //}

            int count = 0;
            var model = new PlotModel();

            foreach (var pair in sliceSeriesList) {
                //if (count++ == 0)
                //    continue;
                var sliceList = pair.Item2.ToList();
                sliceList.Sort((a, b) => a.Item1.CompareTo(b.Item1));

                //var slicePlot = new OxyPlot.Series.LineSeries();
                var slicePlot = new OxyPlot.Series.LinearBarSeries();
                slicePlot.BarWidth = 10;
                slicePlot.StrokeThickness = 1;
                slicePlot.StrokeColor = OxyColor.FromArgb(255, 50, 50, 50);

                var p = profile.GetOrCreateProcess(pair.Item1);
                slicePlot.Title = p?.Name;
                model.Series.Add(slicePlot);

                foreach (var slicePair in sliceList) {
                    var pointTime = TimeSpan.FromTicks(slicePair.Item1 * timePerSlice.Ticks).TotalSeconds;
                    slicePlot.Points.Add(new DataPoint(pointTime, slicePair.Item2.TotalMilliseconds));
                }

                //if (count++ > 10)
                //    break;
                //Trace.WriteLine($"{pair.Key}: {pair.Item2}");
            }

            var w = new Window();
            w.Width = 500;
            w.Height = 300;
            var plotView = new OxyPlot.SkiaSharp.Wpf.PlotView();
            model.IsLegendVisible = true;
            model.Padding = new OxyThickness(0);

            model.Axes.Add(new LinearAxis() {
                Position = AxisPosition.Left,
                TickStyle = TickStyle.Crossing,
                MajorGridlineStyle = LineStyle.Automatic,
                MinorGridlineStyle = LineStyle.None,
                IsZoomEnabled = false,
            });
            model.Axes.Add(new LinearAxis() {
                Position = AxisPosition.Bottom,
                TickStyle = TickStyle.Crossing,
                MajorGridlineStyle = LineStyle.Automatic,
                MinorGridlineStyle = LineStyle.None,
                IsZoomEnabled = true,
            });


            plotView.Model = model;
            w.Content = plotView;
            w.Show();

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

    public class ResolvedProfileStackFrame {
        public long FrameIP { get; set; }
        public DebugFunctionInfo FunctionInfo { get; set; }
        public IRTextFunction Function { get; set; }
        public ProfileImage Image { get; set; }
        public ModuleInfo Module { get; set; }
        public FunctionProfileData Profile { get; set; }
        public bool IsUnknown => Image == null;

        public ResolvedProfileStackFrame() {}

        public ResolvedProfileStackFrame(long frameIP, DebugFunctionInfo functionInfo, IRTextFunction function,
            ProfileImage image, ModuleInfo module, FunctionProfileData profile = null) {
            FrameIP = frameIP;
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
}