using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using IRExplorerCore;
using System.Windows.Documents;
using Microsoft.Diagnostics.Tracing;
using static IRExplorerUI.Profile.ETWProfileDataProvider;
using Microsoft.Diagnostics.Tracing.Parsers.Symbol;
using Microsoft.Diagnostics.Tracing.Parsers;
using System.Diagnostics;
using System.IO;
using IRExplorerUI.Compilers;
using Microsoft.Diagnostics.Runtime;
using static System.Collections.Specialized.BitVector32;
using System.Windows.Markup;
using CSScriptLib;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using System.Collections;
using System.Collections.Concurrent;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using IRExplorerUI.Compilers.ASM;
using Microsoft.Diagnostics.Runtime.DacInterface;
using System.Net;
using System.Runtime.InteropServices;
using Architecture = Microsoft.Diagnostics.Runtime.Architecture;
using System.IO.Compression;
using System.Threading;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using System.Windows.Media.Media3D;

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
    private List<int> childAcceptedProcessIds_;
    private Dictionary<int, ClrRuntime> runtimeMap_;
    BlockingCollection<DisassemblerArgs> disasmTaskQueue_;

    public ETWEventProcessor(ETWTraceEventSource source, bool isRealTime = true, 
                             int acceptedProcessId = 0, bool handleDotNetEvents = false) {
        source_ = source;
        isRealTime_ = isRealTime;
        acceptedProcessId_ = acceptedProcessId;
        handleDotNetEvents_ = handleDotNetEvents;
        childAcceptedProcessIds_ = new List<int>();
    }

    public ETWEventProcessor(string tracePath) {
        Debug.Assert(File.Exists(tracePath));
        source_ = new ETWTraceEventSource(tracePath);
        childAcceptedProcessIds_ = new List<int>();
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

            // Save sample.
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

    const double samplingErrorMargin = 1.1; // 10% deviation from sampling interval allowed.

    bool samplingIntervalSet = false;
    int samplingInterval100NS;
    double samplingIntervalMS;
    double samplingIntervalLimitMS;

    void UpdateSamplingInterval(int value) {
        samplingInterval100NS = value;
        samplingIntervalMS = (double)samplingInterval100NS / 10000;
        samplingIntervalLimitMS = samplingIntervalMS * samplingErrorMargin;
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
        const int sampleReportInterval = 1000;

        var symbolParser = new SymbolTraceEventParser(source_);

        symbolParser.ImageID += data => {
            // The image timestamp often is part of this event.
            // A correct timestamp is needed to locate and download the image.
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
            
            // If parent is one of the accepted processes, accept the child too.
            //? TOOD: Option
            if (IsAcceptedProcess(data.ParentID)) {
                Trace.WriteLine($"=> Accept child {data.ProcessID} of {data.ParentID}");
                childAcceptedProcessIds_.Add(data.ProcessID);
            }
        };

        source_.Kernel.ImageGroup += data => {
            string originalName = null;
            int timeStamp = data.TimeDateStamp;
            bool sawImageId = false;

            if (timeStamp == 0) {
                if (lastImageIdData != null && lastImageIdData.TimeStampQPC == data.TimeStampQPC) {
                    // The ImageID event showed up earlier in the stream.
                    sawImageId = true;
                    originalName = lastImageIdData.OriginalFileName;
                    timeStamp = lastImageIdData.TimeDateStamp;
                }
                else if (isRealTime_) {
                    // In a capture session, the image is on the local machine,
                    // so just the the info out of the binary.
                    var imageInfo = PEBinaryInfoProvider.GetBinaryFileInfo(data.FileName);

                    if (imageInfo != null) {
                        timeStamp = imageInfo.TimeStamp;
                    }
                }
            }

            var image = new ProfileImage(data.FileName, originalName, (long)data.ImageBase,
                (long)data.DefaultBase, data.ImageSize,
                timeStamp, data.ImageChecksum);

            //Trace.WriteLine($"ImageGroup: {image}, Proc: {data.ProcessID}");
            int imageId = profile.AddImageToProcess(data.ProcessID, image);

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
                Trace.WriteLine("CANCELED");
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

            //? TODO: Use a pooled sample to avoid alloc (profile.Rent*)
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
            int sampleCount = profile.Samples.Count;

            if (sampleCount - lastReportedSampleCount >= sampleReportInterval) {
                progressCallback?.Invoke(new ProfileLoadProgress(ProfileLoadStage.TraceLoading) { Total = sampleCount, Current = sampleCount });
                lastReportedSampleCount = sampleCount;
            }
        };

        Task disasmTask = null;
        
        if (handleDotNetEvents_) {
            ProcessDotNetEvents(profile);

            // Use separate thread to disassemble the native code,
            // since it would slow down the general event processing.
            disasmTaskQueue_ = new BlockingCollection<DisassemblerArgs>();
            disasmTask = Task.Run(() => {
                try {
                    foreach (var data in disasmTaskQueue_.GetConsumingEnumerable(cancelableTask.Token)) {
                        Trace.WriteLine($"=> Process {data.FuncInfo.Name}");
                        DisassembleManagedMethod(data);
                    }
                }
                catch (OperationCanceledException) {
                    // Canceled by client, ignore.
                }
            });
        }

        // Go over all ETW events, which will call the registered handlers.
        source_.Process();

        if (handleDotNetEvents_) {
            disasmTaskQueue_.CompleteAdding();
            disasmTask.Wait();
        }

        Trace.WriteLine($"Done processing ETW events");
        //Trace.Flush();
        
        profile.LoadingCompleted();
        //StateSerializer.Serialize(@"C:\test\out.dat", profile);
        return profile;
    }
    
    private void ProcessDotNetEvents(RawProfileData profile) {
        runtimeMap_ = new Dictionary<int, ClrRuntime>();
        var rundownParser = new ClrRundownTraceEventParser(source_);
        
        source_.Clr.MethodILToNativeMap += data => {
            ProcessDotNetILToNativeMap(data, profile);
        };
        
        rundownParser.MethodILToNativeMapDCStart += data => {
            ProcessDotNetILToNativeMap(data, profile);
        };
        
        rundownParser.MethodILToNativeMapDCStop += data => {
            ProcessDotNetILToNativeMap(data, profile);
        };

        source_.Clr.MethodLoadVerbose += data => {
            
            ProcessDotNetMethodLoad(data, profile);
        };

        rundownParser.MethodDCStartVerbose += data => {
            ProcessDotNetMethodLoad(data, profile);
        };

        rundownParser.MethodDCStopVerbose += data => {        
            ProcessDotNetMethodLoad(data, profile);
        };

        // MethodUnloadVerbose
    }
    
    private void ProcessDotNetILToNativeMap(MethodILToNativeMapTraceData data, RawProfileData profile) {
        if (!IsAcceptedProcess(data.ProcessID)) {
            return; // Ignore events from other processes.
        }

#if DEBUG
        Trace.WriteLine($"=> ILMap token: {data.MethodID}, entries: {data.CountOfMapEntries}, ProcessID: {data.ProcessID}, name: {data.ProcessName}");
#endif
        var runtime = GetRuntime(data.ProcessID);

        if (runtime == null) {
            return;
        }

        var method = runtime.GetMethodByHandle((ulong)data.MethodID);
        var module = method?.Type?.Module;
        
        if (module == null) {
            return;
        }

        PdbInfo pdbInfo = module.Pdb;

        if (pdbInfo == null) {
            return;
        }

        var methodMapping = profile.FindManagedMethod(data.MethodID, data.ProcessID);

        if (methodMapping.IsUnknown) {
            return;
        }
        
        var moduleDebugInfo = profile.GetDebugInfoForImage(methodMapping.Image, data.ProcessID) as DotNetDebugInfoProvider;
        if (moduleDebugInfo == null) {
            return; //? Can it fail?
        }

        moduleDebugInfo.ManagedSymbolFile = FromRuntimePdbInfo(pdbInfo);
        var ilOffsets = new List<(int ILOffset, int NativeOffset)>(data.CountOfMapEntries);
        
        for (int i = 0; i < data.CountOfMapEntries; i++) {
            ilOffsets.Add((data.ILOffset(i), data.NativeOffset(i)));
        }

        moduleDebugInfo.AddMethodILToNativeMap(methodMapping.DebugInfo, ilOffsets);
    }

    private void ProcessDotNetMethodLoad(MethodLoadUnloadVerboseTraceData data, RawProfileData profile) {
        if (!IsAcceptedProcess(data.ProcessID)) {
            return; // Ignore events from other processes.
        }

#if DEBUG
        Trace.WriteLine($"=> Load at {data.MethodStartAddress:X}: {data.MethodName} {data.MethodSignature},ProcessID: {data.ProcessID}, name: {data.ProcessName}");
        Trace.WriteLine($"     id/token: {data.MethodID}/{data.MethodToken}, opts: {data.OptimizationTier}, size: {data.MethodSize}");
#endif
        var runtime = GetRuntime(data.ProcessID);

        if (runtime == null) {
            return;
        }
        
        
        // runtime.FlushCachedData();
        
        var method = runtime.GetMethodByHandle((ulong)data.MethodID);
        var module = method?.Type?.Module;
        
        if (module == null) {
            return;
        }
        
        var runtimeArch = FromRuntimeArchitecture(runtime);
        var moduleName = Utils.TryGetFileName(module.Name);
        var (moduleDebugInfo, moduleImage) = profile.GetOrAddModuleDebugInfo(data.ProcessID, moduleName,
                                                                            (long)module.ImageBase, runtimeArch);
        if (moduleDebugInfo == null) {
            return;
        }

        var funcRva = data.MethodStartAddress;
        var funcName = method.Signature;
        var funcInfo = new DebugFunctionInfo(funcName, (long)funcRva, data.MethodSize, data.MethodToken);
        moduleDebugInfo.AddFunctionInfo(funcInfo);
        profile.AddManagedMethodMapping(data.MethodID, funcInfo, moduleImage, 
                                        (long)data.MethodStartAddress, data.MethodSize, data.ProcessID);

        // Save method code by copying it from the running process.
        var methodCode = CopyDotNetMethod(data.MethodStartAddress, data.MethodSize, runtime);
        funcInfo.Data = methodCode;

        // Use separate thread to disassemble the native code,
        // since it would slow down the general event processing.
        disasmTaskQueue_.Add(new DisassemblerArgs(methodCode, funcInfo, runtime));
    }

    private void DisassembleManagedMethod(DisassemblerArgs disassemblerArgs) {
        try {
            bool isValid = true;

            for (int i = 0; i < Math.Min(10, disassemblerArgs.MethodCode.Length); i++) {
                if (disassemblerArgs.MethodCode[i] != 0) {
                    break;
                }
                else if (i >= 8) {
                    isValid = false;
                    break;
                }
            }
            
            var runtime = disassemblerArgs.Runtime;
            var runtimeArch = FromRuntimeArchitecture(runtime);
            using var disassembler = Disassembler.CreateForMachine(runtimeArch, address => {
                if (address == 0) {
                    return null;
                }

                // Assuming the address is a managed entry point,
                // this will return the method that gets called.
                var targetMethod = runtime.GetMethodByInstructionPointer((ulong)address);

                if (targetMethod != null) {
                    return targetMethod.Signature;
                }
                
                // Check if it's one of the JIT helpers.
                return runtime.GetJitHelperFunctionName((ulong)address);
            });

            var asmText = disassembler.DisassembleToText(disassemblerArgs.MethodCode,
                                                         disassemblerArgs.FuncInfo.RVA);
            var cs = new CompressedString(asmText);
            disassemblerArgs.FuncInfo.Data = cs;

            //Trace.WriteLine($"Length: {asmText.Length}, Size {asmText.Length * 2}, Compressed: {cs.Size}");
            //Trace.WriteLine(asmText);
            //Trace.WriteLine("===========================");
        }
        catch (Exception ex) {
            Trace.WriteLine($"Failed to disasm managed method at {disassemblerArgs.FuncInfo.RVA}: {ex.ToString()}");
        }
    }

    private Machine FromRuntimeArchitecture(ClrRuntime runtime) {
        var arch = runtime.ClrInfo.DacInfo.TargetArchitecture;
        return arch switch {
            Architecture.Amd64 => Machine.Amd64,
            Architecture.X86 => Machine.I386,
            Architecture.Arm64 => Machine.Arm64,
            Architecture.Arm => Machine.Arm,
            _ => throw new InvalidOperationException($"Unknown .NET architecture: {arch}")
        };
    }

    private SymbolFileDescriptor FromRuntimePdbInfo(PdbInfo info) {
        return new SymbolFileDescriptor(info.Path, info.Guid, info.Revision);
    }

    private ClrRuntime GetRuntime(int processId) {
        if (!runtimeMap_.TryGetValue(processId, out var runtime)) {
            runtime = TryConnectToDotNetProcess(processId);
            runtimeMap_[processId] = runtime;
        }

        return runtime;
    }

    private ClrRuntime TryConnectToDotNetProcess(int processId) {
        DataTarget dataTarget = null;
        
        try {
            dataTarget = DataTarget.AttachToProcess(processId, false);

            foreach (var v in dataTarget.ClrVersions) {
                var dac = v.DacInfo;
                Trace.WriteLine($"DAC {dac.LocalDacPath}");
                Trace.WriteLine($"DAC target: {dac.TargetArchitecture}");
                Trace.WriteLine($"DAC version: {dac.Version}");
            }

            return dataTarget.ClrVersions[0].CreateRuntime();
        }
        catch (Exception ex) {
            Trace.WriteLine($"Failed to connect to .NET process {processId}: {ex.Message}\n{ex.StackTrace}");
            dataTarget?.Dispose();
            return null;
        }
    }

    private byte[] CopyDotNetMethod(ulong address, int size, ClrRuntime runtime) {
        try {
            var buffer = new byte[size];
            runtime.DataTarget.DataReader.Read(address, buffer.AsSpan());
            return buffer;
        }
        catch (Exception ex) {
            Trace.WriteLine($"Failed to read .NET method {address:X}, size {size}: {ex.Message}\n{ex.StackTrace}");
            return null;
        }
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
    
    public void Dispose() {
        source_?.Dispose();
    }

    //runtime.DacLibrary.SOSDacInterface.GetMethodDescData(method.MethodDesc, data.MethodStartAddress, out var x);
    //runtime.DacLibrary.SOSDacInterface.GetMethodTableData(data.MethodStartAddress, out var mt);
    //var slot = runtime.DacLibrary.SOSDacInterface.GetMethodTableSlot(mt.Token, x.SlotNumber);
    //var slotPtr = x.AddressOfNativeCodeSlot.Value;
    //var buffer = new byte[8];
    //runtime.DataTarget.DataReader.Read((ulong)slotPtr, buffer.AsSpan());
    //var slotAddr = MemoryMarshal.Read<long>(buffer);
    //Trace.WriteLine($"Found slot {slotAddr:X} for ptr {slotPtr:X}");

    //runtime.DacLibrary.SOSDacInterface.GetCodeHeaderData(data.MethodStartAddress, out var codeHeader);
    //runtime.DacLibrary.SOSDacInterface.meth
    //var back = runtime.DacLibrary.SOSDacInterface.GetMethodDescPtrFromIP(data.MethodStartAddress);
}
