using System;
using System.Collections.Generic;
using IRExplorerCore;
using Microsoft.Diagnostics.Tracing;
using static IRExplorerUI.Profile.ETWProfileDataProvider;
using Microsoft.Diagnostics.Tracing.Parsers.Symbol;
using Microsoft.Diagnostics.Tracing.Parsers;
using System.Diagnostics;
using System.IO;
using IRExplorerUI.Compilers;
using Microsoft.Diagnostics.Runtime;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Windows.Documents;
using System.Text;
using System.Windows;
using System.Windows.Controls.Ribbon.Primitives;
using CSScriptLib;
using System.Net;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.AutomatedAnalysis;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.StartPanel;

namespace IRExplorerUI.Profile.ETW;

public sealed class ETWEventProcessor : IDisposable {
    const double SamplingErrorMargin = 1.1; // 10% deviation from sampling interval allowed.
    const int SampleReportingInterval = 10000;
    const int EventReportingInterval = 10000;
    const int MaxCoreCount = 4096;

    private ETWTraceEventSource source_;
    private bool isRealTime_;
    private bool handleDotNetEvents_;
    private int acceptedProcessId_;
    private bool handleChildProcesses_;
    private string managedAsmDir_;
    private List<int> childAcceptedProcessIds_;
    private ProfileDataProviderOptions providerOptions_;

    bool samplingIntervalSet_;
    int samplingInterval100NS_;
    double samplingIntervalMS_;
    double samplingIntervalLimitMS_;

    public ETWEventProcessor(ETWTraceEventSource source, ProfileDataProviderOptions providerOptions,
                             bool isRealTime = true, 
                             int acceptedProcessId = 0, bool handleChildProcesses = false,
                             bool handleDotNetEvents = false,
                             string managedAsmDir = null) {
        Trace.WriteLine($"New ETWEventProcessor: ProcId {acceptedProcessId}, handleDotNet: {handleDotNetEvents}");
        source_ = source;
        providerOptions_ = providerOptions;
        isRealTime_ = isRealTime;
        acceptedProcessId_ = acceptedProcessId;
        handleDotNetEvents_ = handleDotNetEvents;
        handleChildProcesses_ = handleChildProcesses;
        managedAsmDir_ = managedAsmDir;
        childAcceptedProcessIds_ = new List<int>();
    }

    public ETWEventProcessor(string tracePath, ProfileDataProviderOptions providerOptions) {
        Debug.Assert(File.Exists(tracePath));
        childAcceptedProcessIds_ = new List<int>();
        source_ = new ETWTraceEventSource(tracePath);
        providerOptions_ = providerOptions;
    }

    public static bool IsKernelAddress(ulong ip, int pointerSize) {
        if (pointerSize == 4) {
            return ip >= 0x80000000;
        }

        return ip >= 0xFFFF000000000000;
    }

    public List<ProcessSummary> BuildProcessSummary(ProcessListProgressHandler progressCallback,
                                                    CancelableTask cancelableTask) {
        // Default 1ms sampling interval 1ms.
        UpdateSamplingInterval(SampleReportingInterval);
        
        source_.Kernel.PerfInfoCollectionStart += data => {
            if (data.SampleSource == 0) {
                UpdateSamplingInterval(data.NewInterval);
                samplingIntervalSet_ = true;
            }
        };

        source_.Kernel.PerfInfoSetInterval += data => {
            if (data.SampleSource == 0 && !samplingIntervalSet_) {
                UpdateSamplingInterval(data.OldInterval);
                samplingIntervalSet_ = true;
            }
        };

        RawProfileData profile = new();
        int lastReportedSample = 0;
        int lastProcessListSample = 0;
        int nextProcessListSample = SampleReportingInterval * 10;

        if (isRealTime_) {
            profile.TraceInfo.ProfileStartTime = DateTime.Now;
        }

        source_.Kernel.ProcessStartGroup += data => {
            var proc = new ProfileProcess(data.ProcessID, data.ParentID,
                data.ProcessName, data.ImageFileName,
                data.CommandLine);
            profile.AddProcess(proc);
        };

        source_.Kernel.PerfInfoSample += data => {
            if (cancelableTask.IsCanceled) {
                source_.StopProcessing();
            }

            if (data.ProcessID < 0) {
                return;
            }

            var context = profile.RentTempContext(data.ProcessID, data.ThreadID, data.ProcessorNumber);
            int contextId = profile.AddContext(context);
            
            var sample = new ProfileSample((long)data.InstructionPointer,
                TimeSpan.FromMilliseconds(data.TimeStampRelativeMSec),
                TimeSpan.FromMilliseconds(samplingIntervalMS_),
                false, contextId);
            int sampleId = profile.AddSample(sample);
            profile.ReturnContext(contextId);

            
            if (sampleId - lastReportedSample >= SampleReportingInterval) {
                List<ProcessSummary> processList = null;


                if (sampleId - lastProcessListSample >= nextProcessListSample) {
                    var sw = Stopwatch.StartNew();

                    processList = profile.BuildProcessSummary();
                    lastProcessListSample = sampleId;
                    nextProcessListSample *= 2;

                    Trace.WriteLine($"Took {sw.ElapsedMilliseconds} for {profile.Samples.Count / 10000} K");

                }

                progressCallback?.Invoke(new ProcessListProgress() {
                    Total = (int)source_.SessionDuration.TotalMilliseconds,
                    Current = (int)data.TimeStampRelativeMSec,
                    Processes = processList
                });

                lastReportedSample = sampleId;
            }

            Trace.Flush();
        };
        
        source_.Process();
        return profile.BuildProcessSummary();
    }

    void UpdateSamplingInterval(int value) {
        samplingInterval100NS_ = value;
        samplingIntervalMS_ = (double)samplingInterval100NS_ / 10000;
        samplingIntervalLimitMS_ = samplingIntervalMS_ * SamplingErrorMargin;
    }

    public RawProfileData ProcessEvents(ProfileLoadProgressHandler progressCallback, 
                                        CancelableTask cancelableTask) {
        UpdateSamplingInterval(SampleReportingInterval);
        ImageIDTraceData lastImageIdData = null;
        ProfileImage lastProfileImage = null;
        long lastProfileImageTime = 0;


        double[] perCoreLastTime = new double[MaxCoreCount];
        int[] perCoreLastSample = new int[MaxCoreCount];
        (int StackId, long Timestamp)[] perCoreLastKernelStack = new (int StackId, long Timestamp)[MaxCoreCount];
        int oldestCompressedSample = 0;
        var perContextLastSample = new Dictionary<int, int>();
        int lastReportedSample = 0;

        var symbolParser = new SymbolTraceEventParser(source_);

        symbolParser.ImageID += data => {
            // The image timestamp often is part of this event when reading an ETL file.
            // A correct timestamp is needed to locate and download the image.

            //Trace.WriteLine($"ImageID: orig {data.OriginalFileName}, QPC {data.TimeStampQPC}");
            //Trace.WriteLine($"ImageID: timeStamp {data.TimeDateStamp}");
            //Trace.WriteLine($"ImageID: has lastProfileImage {lastProfileImage != null}");
            //Trace.WriteLine($"    matching {lastProfileImage != null && lastProfileImageTime == data.TimeStampQPC}");

            //if (lastProfileImage != null) {
            //    Trace.WriteLine($"    last image: {lastProfileImage.FilePath}");
            //    Trace.WriteLine($"    last orign: {lastProfileImage.OriginalFileName}");
            //    Trace.WriteLine($"    qpc {lastProfileImageTime} vs current {data.TimeStampQPC}");
            //}

            if (lastProfileImage != null &&
                lastProfileImageTime == data.TimeStampQPC) {
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

        RawProfileData profile = new(handleDotNetEvents_);

        source_.Kernel.ProcessStartGroup += data => {
            var proc = new ProfileProcess(data.ProcessID, data.ParentID,
                                          data.ProcessName, data.ImageFileName,
                                          data.CommandLine);
            profile.AddProcess(proc);
#if DEBUG
            Trace.WriteLine($"ProcessStartGroup: {proc}");
#endif

            // If parent is one of the accepted processes, accept the child too.
            if (handleChildProcesses_ && IsAcceptedProcess(data.ParentID)) {
                //Trace.WriteLine($"=> Accept child {data.ProcessID} of {data.ParentID}");
                childAcceptedProcessIds_.Add(data.ProcessID);
            }
        };

        source_.Kernel.ImageGroup += data => {
            string originalName = null;
            int timeStamp = data.TimeDateStamp;
            bool sawImageId = false;

            //Trace.WriteLine($"ImageGroup: name {data.FileName}, proc {data.ProcessID}, base {data.ImageBase:X}, size {data.ImageSize:X}, procName {data.ProcessName}, TS {data.TimeStampQPC}");
            //Trace.WriteLine($"   has last {lastImageIdData != null}");
            //Trace.WriteLine($"   matching {lastImageIdData != null && lastImageIdData.TimeStampQPC == data.TimeStampQPC}");

            //if (lastImageIdData != null) {
            //    Trace.WriteLine($"    last orign: {lastImageIdData.OriginalFileName}");
            //    Trace.WriteLine($"    dateStamp {lastImageIdData.TimeDateStamp} vs current {data.TimeDateStamp}");
            //}

            if (lastImageIdData != null && lastImageIdData.TimeStampQPC == data.TimeStampQPC) {
                // The ImageID event showed up earlier in the stream.
                sawImageId = true;
                originalName = lastImageIdData.OriginalFileName;

                if (timeStamp == 0) {
                    timeStamp = lastImageIdData.TimeDateStamp;
                }
            }
            else if (isRealTime_) {
                // In a capture session, the image is on the local machine,
                // so just take the info out of the binary.
                var imageInfo = PEBinaryInfoProvider.GetBinaryFileInfo(data.FileName);

                if (imageInfo != null) {
                    timeStamp = imageInfo.TimeStamp;
                }
            }

            var image = new ProfileImage(data.FileName, originalName, (long)data.ImageBase,
                (long)data.DefaultBase, data.ImageSize,
                timeStamp, data.ImageChecksum);
            int imageId = profile.AddImageToProcess(data.ProcessID, image);

#if DEBUG
            //Trace.WriteLine($"ImageGroup: {image}, Proc: {data.ProcessID}");
#endif
            if (!sawImageId) {
                // The ImageID event may show up later in the stream.
                lastProfileImage = profile.FindImage(imageId);
                lastProfileImageTime = data.TimeStampQPC;
            }
            else {
                lastProfileImage = null;
            }
        };
        
        source_.Kernel.ThreadStartGroup += data => {
            if (cancelableTask != null && cancelableTask.IsCanceled) {
                source_.StopProcessing();
            }
            
            var thread = new ProfileThread(data.ThreadID, data.ProcessID, data.ThreadName);
            profile.AddThreadToProcess(data.ProcessID, thread);
        };

        source_.Kernel.EventTraceHeader += data => {
            profile.TraceInfo.CpuSpeed = data.CPUSpeed;
            profile.TraceInfo.ProfileStartTime = data.StartTime;
            profile.TraceInfo.ProfileEndTime = data.EndTime;
        };

        source_.Kernel.SystemConfigCPU += data => {
            profile.TraceInfo.PointerSize = data.PointerSize;
            profile.TraceInfo.CpuCount = data.NumberOfProcessors;
            profile.TraceInfo.ComputerName = data.ComputerName;
            profile.TraceInfo.DomainName = data.DomainName;
            profile.TraceInfo.MemorySize = data.MemSize;
        };

        source_.Kernel.StackWalkStack += data => {
            bool isKernelStack = false;

            if (data.FrameCount > 0 &&
                IsKernelAddress(data.InstructionPointer(0), data.PointerSize)) {
                isKernelStack = true;
                
                //Trace.WriteLine($"Kernel stack {data.InstructionPointer(0):X}, proc {data.ProcessID}, name {data.ProcessName}, TS {data.EventTimeStampQPC}");
                //if (data.FrameCount > 1 && !IsKernelAddress(data.InstructionPointer(data.FrameCount - 1), 8)) {
                //  //  Trace.WriteLine("     ends in user");
                //}
            }
            else {
                //Trace.WriteLine($"User stack {data.InstructionPointer(0):X}, proc {data.ProcessID}, name {data.ProcessName}, TS {data.EventTimeStampQPC}");
            }

            if (!IsAcceptedProcess(data.ProcessID)) {
                return; // Ignore events from other processes.
            }

            //queue.Add((StackWalkStackTraceData)data.Clone());
            var context = profile.RentTempContext(data.ProcessID, data.ThreadID, data.ProcessorNumber);
            int contextId = profile.AddContext(context);
            int frameCount = data.FrameCount;

            ProfileStack kstack = null;

            if (!isKernelStack) {
                var lastKernelStack = perCoreLastKernelStack[data.ProcessorNumber];

                if (lastKernelStack.StackId != 0 &&
                    lastKernelStack.Timestamp == data.EventTimeStampQPC) {
                    //Trace.WriteLine($"Found kstack {lastKernelStack.StackId} at {lastKernelStack.Timestamp}");


                    // Append at the end of the kernel stack, marking a user -> kernel mode transition.
                    kstack = profile.FindStack(lastKernelStack.StackId);
                    int kstackFrameCount = kstack.FrameCount;
                    long[] frames = new long[kstack.FrameCount + data.FrameCount];
                    kstack.FramePointers.CopyTo(frames, 0);

                    for (int i = 0; i < frameCount; i++) {
                        frames[kstackFrameCount + i] = (long)data.InstructionPointer(i);
                    }

                    kstack.FramePointers = frames;
                    kstack.UserModeTransitionIndex = kstackFrameCount; // Frames after index are user mode.
                }
            }

            if (kstack == null) {
                var stack = profile.RentTemporaryStack(frameCount, contextId);

                // Copy data from event to the temp. stack pointer array.
                // Slightly faster to copy the entire array as a whole.
                unsafe {
                    void* ptr = (void*)((IntPtr)(void*)data.DataStart + 16);
                    int bytes = data.PointerSize * frameCount;
                    var span = new Span<byte>(ptr, bytes);

                    fixed (long* destPtr = stack.FramePointers) {
                        var destSpan = new Span<byte>(destPtr, bytes);
                        span.CopyTo(destSpan);
                    }
                }

                int stackId = profile.AddStack(stack, context);

                // Try to associate with a previous sample from the same context.
                int sampleId = perCoreLastSample[data.ProcessorNumber];

                if (!profile.TrySetSampleStack(sampleId, stackId, contextId)) {
                    if (perContextLastSample.TryGetValue(contextId, out sampleId)) {
                        profile.SetSampleStack(sampleId, stackId, contextId);
                    }
                }

                if (isKernelStack) {
                    perCoreLastKernelStack[data.ProcessorNumber] = (stackId, data.EventTimeStampQPC);
                }

                profile.ReturnStack(stackId);
            }

            profile.ReturnContext(contextId);
        };

        source_.Kernel.PerfInfoCollectionStart += data => {
            if (data.SampleSource == 0) {
                UpdateSamplingInterval(data.NewInterval);
                samplingIntervalSet_ = true;
            }
            else {
                // The description of a PMC event.
                var dataSpan = data.EventData().AsSpan();
                string name = ReadWideString(dataSpan, 12);
                var counterInfo = new PerformanceCounter(data.SampleSource, name, data.NewInterval);
                profile.AddPerformanceCounter(counterInfo);
            }
        };

        source_.Kernel.PerfInfoSetInterval += data => {
            if (data.SampleSource == 0 && !samplingIntervalSet_) {
                UpdateSamplingInterval(data.OldInterval);
                samplingIntervalSet_ = true;
            }
        };

        source_.Kernel.PerfInfoSample += data => {
            if (!IsAcceptedProcess(data.ProcessID)) {
                return; // Ignore events from other processes.
            }

            if (cancelableTask != null && cancelableTask.IsCanceled) {
                source_.StopProcessing();
            }

            // If the time since the last sample is greater than the sampling interval + some error margin,
            // it likely means that some samples were lost, use the sampling interval as the weight.
            int cpu = data.ProcessorNumber;
            double timestamp = data.TimeStampRelativeMSec;
            double weight = timestamp - perCoreLastTime[cpu];

            if (weight > samplingIntervalLimitMS_) {
                weight = samplingIntervalMS_;
            }

            perCoreLastTime[cpu] = timestamp;

            // Skip unknown process.
            if (data.ProcessID < 0) {
                return;
            }

            // Skip idle thread on non-kernel code.
            bool isKernelCode = data.ExecutingDPC || data.ExecutingISR;

            if (data.ThreadID == 0 && !isKernelCode) {
                return;
            }

            // Save sample.
            var context = profile.RentTempContext(data.ProcessID, data.ThreadID, cpu);
            int contextId = profile.AddContext(context);

            var sample = new ProfileSample((long)data.InstructionPointer,
                                           TimeSpan.FromMilliseconds(timestamp),
                                           TimeSpan.FromMilliseconds(weight),
                                           isKernelCode, contextId);
            int sampleId = profile.AddSample(sample);
            profile.ReturnContext(contextId);

            // Remember the sample, to be matched later with a call stack.
            perCoreLastSample[cpu] = sampleId;
            perContextLastSample[contextId] = sampleId;

            // Report progress.
            if (sampleId - lastReportedSample >= SampleReportingInterval) {
                progressCallback?.Invoke(new ProfileLoadProgress(ProfileLoadStage.TraceLoading) { Total = sampleId, Current = sampleId });
                lastReportedSample = sampleId;

                // Updating the stack associated with a sample often ends up
                // decompressing a segment and it remains in that state until the end, wasting memory.
                // Since sample IDs are increasing, compress all segments that may have been
                // accessed since the last time and cannot be anymore.
                if (profile.TraceInfo.CpuCount > 0) {
                    int earliestSample = int.MaxValue;

                    for (int i = 0; i < profile.TraceInfo.CpuCount; i++) {
                        int value = perCoreLastSample[i];
                        earliestSample = Math.Min(value, earliestSample);
                    }

                    //Trace.WriteLine($"Compress {earliestSample}");
                    if (earliestSample > oldestCompressedSample) {
                        profile.Samples.CompressRange(oldestCompressedSample, earliestSample);
                        oldestCompressedSample = earliestSample;
                    }
                }
            }
        };

#if false
        GC.Collect();
        GC.WaitForPendingFinalizers();
        long memory = GC.GetTotalMemory(true);

        var pmcDict = new Dictionary<Tuple<long, int, int>, List<PerformanceCounterEvent>>();
        int small = 0;
        int large = 0;
#endif

        if (providerOptions_.IncludePerformanceCounters) {
            Trace.WriteLine("Collecting PMC events");

            source_.Kernel.PerfInfoPMCSample += data => {
                if (!IsAcceptedProcess(data.ProcessID)) {
                    return; // Ignore events from other processes.
                }

                //if (cancelableTask != null && cancelableTask.IsCanceled) {
                //    source_.StopProcessing();
                //}

                // Skip unknown process.
                if (data.ProcessID < 0) {
                    return;
                }

                var context = profile.RentTempContext(data.ProcessID, data.ThreadID, data.ProcessorNumber);
                int contextId = profile.AddContext(context);
                double timestamp = data.TimeStampRelativeMSec;

                var counterEvent = new PerformanceCounterEvent((long)data.InstructionPointer,
                    TimeSpan.FromMilliseconds(timestamp),
                    contextId, (short)data.ProfileSource);
                profile.AddPerformanceCounterEvent(counterEvent);
                profile.ReturnContext(contextId);
            };
        }

        if (handleDotNetEvents_) {
            ProcessDotNetEvents(profile);
        }

        // Go over all ETW events, which will call the registered handlers.
        try {
            Trace.WriteLine("Start processing ETW events");
            var sw = Stopwatch.StartNew();

            if (isRealTime_) {
                profile.TraceInfo.ProfileStartTime = DateTime.Now;
            }

            source_.Process();

            if (isRealTime_) {
                profile.TraceInfo.ProfileEndTime = DateTime.Now;
            }

            sw.Stop();
            Trace.WriteLine($"Took: {sw.ElapsedMilliseconds} ms");
        }
        catch (Exception ex) {
            Trace.TraceError($"Failed to process ETW events: {ex.Message}");
        }
        
        Trace.WriteLine($"Done processing ETW events");
        Trace.WriteLine($"  samples: {profile.Samples.Count}");
        Trace.WriteLine($"  events: {profile.PerformanceCountersEvents.Count}");
        //Trace.Flush();

        profile.LoadingCompleted();

        if (handleDotNetEvents_) {
            profile.ManagedLoadingCompleted(managedAsmDir_);
        }

#if DEBUG
        profile.PrintAllProcesses();
#endif

#if false
        GC.Collect();
        GC.WaitForPendingFinalizers();
        long memory2 = GC.GetTotalMemory(true);
        Trace.WriteLine($"Memory diff: {(memory2 - memory) / (1024 * 1024):F2}, MB");
        Trace.Flush();

        Trace.WriteLine($"PMC dict keys: {pmcDict.Count}");
        Trace.WriteLine($"   small diff: {small}");
        Trace.WriteLine($"   large diff: {large}");
        Trace.WriteLine($"   small perc: {((double)small / (small + large)) * 100:F4}");
        Trace.Flush();
#endif
        return profile;
    }
    
    private void ProcessDotNetEvents(RawProfileData profile) {
        //var rundownParser = new ClrRundownTraceEventParser(source_);

        source_.Clr.LoaderModuleLoad += data => {
            ProcessLoaderModuleLoad(data, profile);
        };

        source_.Clr.MethodLoadVerbose += data => {
            ProcessDotNetMethodLoad(data, profile);
        };

        source_.Clr.MethodILToNativeMap += data => {
            ProcessDotNetILToNativeMap(data, profile);
        };

        //rundownParser.MethodILToNativeMapDCStart += data => {
        //    ProcessDotNetILToNativeMap(data, profile);
        //};
        
        //rundownParser.MethodILToNativeMapDCStop += data => {
        //    ProcessDotNetILToNativeMap(data, profile);
        //};

        //rundownParser.MethodDCStartVerbose += data => {
        //    ProcessDotNetMethodLoad(data, profile);
        //};

        //rundownParser.MethodDCStopVerbose += data => {        
        //    ProcessDotNetMethodLoad(data, profile);
        //};
    }
    
    private void ProcessLoaderModuleLoad(ModuleLoadUnloadTraceData data, RawProfileData profile) {
        Trace.WriteLine($"=> Managed module {data.ModuleID}, {data.ModuleILFileName} in proc {data.ProcessID}");
        var runtimeArch = Machine.Amd64;
        var moduleName = data.ModuleILFileName;
        var moduleDebugInfo = profile.GetOrAddModuleDebugInfo(data.ProcessID, moduleName, data.ModuleID, runtimeArch);
        
        if (moduleDebugInfo != null) {
            moduleDebugInfo.ManagedSymbolFile = FromModuleLoad(data);
            Trace.WriteLine($"Set managed symbol {moduleDebugInfo.ManagedSymbolFile}");
        }
    }

    private void ProcessDotNetILToNativeMap(MethodILToNativeMapTraceData data, RawProfileData profile) {
        if (!IsAcceptedProcess(data.ProcessID)) {
            return; // Ignore events from other processes.
        }

//#if DEBUG
        //Trace.WriteLine($"=> ILMap token: {data.MethodID}, entries: {data.CountOfMapEntries}, ProcessID: {data.ProcessID}, name: {data.ProcessName}");
//#endif
        var methodMapping = profile.FindManagedMethod(data.MethodID, data.ProcessID);

        if (methodMapping == null) {
            return;
        }
        
        var ilOffsets = new List<(int ILOffset, int NativeOffset)>(data.CountOfMapEntries);
        
        for (int i = 0; i < data.CountOfMapEntries; i++) {
            ilOffsets.Add((data.ILOffset(i), data.NativeOffset(i)));
        }

        var (debugInfo, _)= profile.GetModuleDebugInfo(data.ProcessID, methodMapping.ModuleId);

        if (debugInfo != null) {
            debugInfo.AddMethodILToNativeMap(methodMapping.FunctionDebugInfo, ilOffsets);
        }
    }

    private void ProcessDotNetMethodLoad(MethodLoadUnloadVerboseTraceData data, RawProfileData profile) {
        if (!IsAcceptedProcess(data.ProcessID)) {
            return; // Ignore events from other processes.
        }
        
#if DEBUG
        Trace.WriteLine($"=> Load at {data.MethodStartAddress}: {data.MethodNamespace}.{data.MethodName}, {data.MethodSignature},ProcessID: {data.ProcessID}, name: {data.ProcessName}");
        Trace.WriteLine($"     id/token: {data.MethodID}/{data.MethodToken}, opts: {data.OptimizationTier}, size: {data.MethodSize}");
#endif
        
        var funcRva = data.MethodStartAddress;
        //var funcName = data.MethodSignature;
        var funcName = $"{data.MethodNamespace}.{data.MethodName}";
        var funcInfo = new FunctionDebugInfo(funcName, (long)funcRva, data.MethodSize,
                                             ToOptimizationLevel(data.OptimizationTier), data.MethodToken);
        profile.AddManagedMethodMapping(data.ModuleID, data.MethodID, funcInfo,
                                        (long)data.MethodStartAddress, data.MethodSize, data.ProcessID);
    }

    private string ToOptimizationLevel(OptimizationTier tier) {
        return tier switch {
            OptimizationTier.MinOptJitted => "MinOptJitted",
            OptimizationTier.Optimized => "Optimized",
            OptimizationTier.OptimizedTier1 => "OptimizedTier1",
            OptimizationTier.PreJIT => "PreJIT",
            OptimizationTier.QuickJitted => "QuickJitted",
            OptimizationTier.ReadyToRun => "ReadyToRun",
            _ => null
        };
    }
    
    private SymbolFileDescriptor FromModuleLoad(ModuleLoadUnloadTraceData data) {
        return new SymbolFileDescriptor(data.ManagedPdbBuildPath, data.ManagedPdbSignature, data.ManagedPdbAge);
    }
    
    private bool IsAcceptedProcess(int processID) {
        if (acceptedProcessId_ == 0) {
            return true; // No filtering.
        }

        if (processID == acceptedProcessId_) {
            return true;
        }

        return childAcceptedProcessIds_.Contains(processID);
    }

    private unsafe static string ReadWideString(ReadOnlySpan<byte> data, int offset = 0) {
        fixed (byte* dataPtr = data) {
            var sb = new StringBuilder();

            while (offset < data.Length - 1) {
                byte first = dataPtr[offset];
                byte second = dataPtr[offset + 1];

                if (first == 0 && second == 0) {
                    break; // Found string null terminator.
                }

                sb.Append((char)((short)first | ((short)second << 8)));
                offset += 2;
            }

            return sb.ToString();
        }
    }

    public void Dispose() {
        source_?.Dispose();
    }
}
