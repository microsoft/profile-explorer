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
using System.Windows.Controls.Ribbon.Primitives;

namespace IRExplorerUI.Profile.ETW;

public class ETWEventProcessor : IDisposable {
    class DisassemblerArgs {
        public DisassemblerArgs(byte[] methodCode, DebugFunctionInfo funcInfo, ClrRuntime runtime) {
            MethodCode = methodCode;
            FuncInfo = funcInfo;
            Runtime = runtime;
        }

        public byte[] MethodCode { get; }
        public DebugFunctionInfo FuncInfo { get; }
        public ClrRuntime Runtime { get; }
    }
    
    private ETWTraceEventSource source_;
    private bool isRealTime_;
    private bool handleDotNetEvents_;
    private int acceptedProcessId_;
    private bool handleChildProcesses_;
    private string managedAsmDir_;
    private List<int> childAcceptedProcessIds_;
    private ProfileDataProviderOptions providerOptions_;

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
        managedAsmDir_ = managedAsmDir;
        childAcceptedProcessIds_ = new List<int>();
    }

    public ETWEventProcessor(string tracePath, ProfileDataProviderOptions providerOptions) {
        Debug.Assert(File.Exists(tracePath));
        childAcceptedProcessIds_ = new List<int>();
        source_ = new ETWTraceEventSource(tracePath);
        providerOptions_ = providerOptions;
    }

    public List<TraceProcessSummary> BuildProcessSummary(CancelableTask cancelableTask) {
        // Default 1ms sampling interval 1ms.
        UpdateSamplingInterval(10000);
        
        source_.Kernel.PerfInfoCollectionStart += data => {
            if (data.SampleSource == 0) {
                UpdateSamplingInterval(data.NewInterval);
                samplingIntervalSet = true;
            }
        };

        source_.Kernel.PerfInfoSetInterval += data => {
            if (data.SampleSource == 0 && !samplingIntervalSet) {
                UpdateSamplingInterval(data.OldInterval);
                samplingIntervalSet = true;
            }
        };

        RawProfileData profile = new();
        
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

            //? TODO: Use a pooled sample to avoid alloc (profile.Rent*)
            var sample = new ProfileSample((long)data.InstructionPointer,
                TimeSpan.FromMilliseconds(data.TimeStampRelativeMSec),
                TimeSpan.FromMilliseconds(samplingIntervalMS),
                false, contextId);
            int sampleId = profile.AddSample(sample);
            profile.ReturnContext(contextId);
        };
        
        source_.Process();
        return profile.BuildProcessSummary();
    }

    const double SamplingErrorMargin = 1.1; // 10% deviation from sampling interval allowed.

    bool samplingIntervalSet = false;
    int samplingInterval100NS;
    double samplingIntervalMS;
    double samplingIntervalLimitMS;

    void UpdateSamplingInterval(int value) {
        samplingInterval100NS = value;
        samplingIntervalMS = (double)samplingInterval100NS / 10000;
        samplingIntervalLimitMS = samplingIntervalMS * SamplingErrorMargin;
    }

    public RawProfileData ProcessEvents(ProfileLoadProgressHandler progressCallback, CancelableTask cancelableTask) {
        // Default 1ms sampling interval 1ms.
        UpdateSamplingInterval(10000);
        ImageIDTraceData lastImageIdData = null;
        ProfileImage lastProfileImage = null;
        long lastProfileImageTime = 0;

        double[] perCoreLastTime = new double[4096];
        int[] perCoreLastSample = new int[4096];
        var perContextLastSample = new Dictionary<int, int>();
        int lastReportedSampleCount = 0;
        const int sampleReportInterval = 500;

        var symbolParser = new SymbolTraceEventParser(source_);

        symbolParser.ImageID += data => {
            // The image timestamp often is part of this event when reading an ETL file.
            // A correct timestamp is needed to locate and download the image.

            //Trace.WriteLine($"ImageID: orig {data.OriginalFileName}, QPC {data.TimeStampQPC}");
            //Trace.WriteLine($"ImageID: has lastProfileImage {lastProfileImage != null}");
            //Trace.WriteLine($"    matching {lastProfileImage != null && lastProfileImageTime == data.TimeStampQPC}");
            //
            //if (lastProfileImage != null) {
            //    Trace.WriteLine($"    last image: {lastProfileImage.FilePath}");
            //    Trace.WriteLine($"    last orign: {lastProfileImage.OriginalFileName}");
            //    Trace.WriteLine($"    qpc {lastProfileImageTime} vs current {data.TimeStampQPC}");
            //}

            if (lastProfileImage != null &&
                lastProfileImageTime == data.TimeStampQPC) {
                lastProfileImage.OriginalFileName = data.OriginalFileName;

                if (lastProfileImage.TimeStamp == 0) { lastProfileImage.TimeStamp = data.TimeDateStamp;
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

            //Trace.WriteLine($"ImageGroup: name {data.FileName}, QPC {data.TimeStampQPC}");
            //Trace.WriteLine($"   has last {lastImageIdData != null}");
            //Trace.WriteLine($"   matching {lastImageIdData != null && lastImageIdData.TimeStampQPC == data.TimeStampQPC}");
            //
            //if (lastImageIdData != null) {
            //    Trace.WriteLine($"    last orign: {lastImageIdData.OriginalFileName}");
            //    Trace.WriteLine($"    qpc {lastImageIdData.TimeDateStamp} vs current {data.TimeStampQPC}");
            //}
            
            if (lastImageIdData != null && lastImageIdData.TimeStampQPC == data.TimeStampQPC) {
                // The ImageID event showed up earlier in the stream.
                sawImageId = true;
                originalName = lastImageIdData.OriginalFileName;
                timeStamp = lastImageIdData.TimeDateStamp;
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
            // data.PointerSize;
        };

        source_.Kernel.StackWalkStack += data => {
            if (!IsAcceptedProcess(data.ProcessID)) {
                return; // Ignore events from other processes.
            }

            //queue.Add((StackWalkStackTraceData)data.Clone());
            var context = profile.RentTempContext(data.ProcessID, data.ThreadID, data.ProcessorNumber);
            int contextId = profile.AddContext(context);

            int frameCount = data.FrameCount;
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

            profile.ReturnStack(stackId);
            profile.ReturnContext(contextId);
        };

        source_.Kernel.PerfInfoCollectionStart += data => {
            if (data.SampleSource == 0) {
                UpdateSamplingInterval(data.NewInterval);
                samplingIntervalSet = true;
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
            if (data.SampleSource == 0 && !samplingIntervalSet) {
                UpdateSamplingInterval(data.OldInterval);
                samplingIntervalSet = true;
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

            if (weight > samplingIntervalLimitMS) {
                weight = samplingIntervalMS;
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
            if (sampleId - lastReportedSampleCount >= sampleReportInterval) {
                progressCallback?.Invoke(new ProfileLoadProgress(ProfileLoadStage.TraceLoading) { Total = sampleId, Current = sampleId });
                lastReportedSampleCount = sampleId;
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

#if false
            var key = new Tuple<long, int, int>((long)data.InstructionPointer, contextId, data.ProfileSource);

            if (!pmcDict.TryGetValue(key, out var list)) {
                list = new List<PerformanceCounterEvent>();
                pmcDict.Add(key, list);
            }

            if (list.Count > 0) {
                var last = list[^1];
                if ((counterEvent.Time - last.Time).TotalMilliseconds < 100) {
                    small++;
                    
                }
                else {
                    large++;
                    list.Add(counterEvent);

                }
            }
            else {
                list.Add(counterEvent);

            }

#endif

                //Trace.WriteLine($"PMC {data.ProfileSource}: {data.InstructionPointer}");

            };
        }

        if (handleDotNetEvents_) {
            ProcessDotNetEvents(profile);
        }

        // Go over all ETW events, which will call the registered handlers.
        try {
            Trace.WriteLine("Start processing ETW events");
            var sw = Stopwatch.StartNew();
            
            source_.Process();
            
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

        methodMapping.DebugInfoProvider.AddMethodILToNativeMap(methodMapping.DebugInfo, ilOffsets);
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
        var funcInfo = new DebugFunctionInfo(funcName, (long)funcRva, data.MethodSize,
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
