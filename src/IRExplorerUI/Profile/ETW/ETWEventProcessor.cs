using System;
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
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace IRExplorerUI.Profile.ETW;

public class ETWEventProcessor : IDisposable {
    private ETWTraceEventSource source_;
    private bool isRealTime_;
    private bool handleDotNetEvents_;

    public ETWEventProcessor(ETWTraceEventSource source, bool isRealTime = true, bool handleDotNetEvents = false) {
        source_ = source;
        isRealTime_ = isRealTime;
        handleDotNetEvents_ = handleDotNetEvents;
    }

    public ETWEventProcessor(string tracePath) {
        Debug.Assert(File.Exists(tracePath));
        source_ = new ETWTraceEventSource(tracePath);
    }

    public List<TraceProcessSummary> BuildProcessSummary(CancelableTask cancelableTask) {
        RawProfileData profile = new();
        var list = new List<TraceProcessSummary>();
        var processSamples = new Dictionary<ProfileProcess, int>();
        int sampleCount = 0;

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

            var process = profile.GetOrCreateProcess(data.ProcessID);
            processSamples.AccumulateValue(process, data.Count);
            sampleCount++;
        };

        source_.Process();

        foreach (var pair in processSamples) {
            list.Add(new TraceProcessSummary(pair.Key, pair.Value) {
                WeightPercentage = 100 * (double)pair.Value / (double)sampleCount
            });
        }

        return list;
    }

    public RawProfileData ProcessEvents(ProfileLoadProgressHandler progressCallback, CancelableTask cancelableTask) {
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

        //? BlockingCollection<StackWalkStackTraceData> queue = new BlockingCollection<StackWalkStackTraceData>();
        var symbolParser = new SymbolTraceEventParser(source_);

        symbolParser.ImageID += data => {
            // The image timestamp often is part of this event.
            // A correct timestamp is needed to locate and download the image.
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

        RawProfileData profile = new();
        
        source_.Kernel.ProcessStartGroup += data => {
            var proc = new ProfileProcess(data.ProcessID, data.ParentID,
                                          data.ProcessName, data.ImageFileName,
                                          data.CommandLine);
            profile.AddProcess(proc);
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
            //if (data.ProcessID != mainProcessId) {
            //    return;
            //}

            //if (string.IsNullOrEmpty(data.ProcessName) ||
            //    !data.ProcessName.Contains(targetProc)) {
            //    return;
            //}

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
            // if (data.ProcessID != mainProcessId) {
            //     return;
            // }

            //if (string.IsNullOrEmpty(data.ProcessName) ||
            //    !data.ProcessName.Contains(targetProc)) {
            //    return;
            //}

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
            int sampleCount = profile.Samples.Count;

            if (sampleCount - lastReportedSampleCount >= sampleReportInterval) {
                progressCallback?.Invoke(new ProfileLoadProgress(ProfileLoadStage.TraceLoading) { Total = sampleCount, Current = sampleCount });
                lastReportedSampleCount = sampleCount;
            }
        };
        
        if (handleDotNetEvents_) {
            ProcessDotNetEvents(profile);
        }

        source_.Process();

        Trace.WriteLine($"Done processing ETW events");
        profile.LoadingCompleted();
        return profile;
    }

    private Dictionary<int, ClrRuntime> runtimeMap_;
    private Dictionary<long, DebugFunctionInfo> funcMap_;

    private void ProcessDotNetEvents(RawProfileData profile) {
        runtimeMap_ = new Dictionary<int, ClrRuntime>();
        funcMap_ = new Dictionary<long, DebugFunctionInfo>();
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

    private string targetProc = "ConsoleTester";

    private void ProcessDotNetILToNativeMap(MethodILToNativeMapTraceData data, RawProfileData profile) {
        if (string.IsNullOrEmpty(data.ProcessName) ||
            !data.ProcessName.Contains(targetProc)) {
            return;
        }

        //Trace.WriteLine($"=> ILMap token: {data.MethodID}, entries: {data.CountOfMapEntries}, ProcessID: {data.ProcessID}, name: {data.ProcessName}");
        var debugInfo = profile.FindManagedMethod(data.MethodID);

        if (debugInfo == null) {
            return;
        }

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

        if (File.Exists(pdbInfo.Path)) {
            using var stream = File.OpenRead(pdbInfo.Path);
            var mdp = MetadataReaderProvider.FromPortablePdbStream(stream);
            var md = mdp.GetMetadataReader();
            var debugHandle = ((MethodDefinitionHandle)MetadataTokens.Handle((int)debugInfo.Id)).ToDebugInformationHandle();
            var managedDebugInfo = md.GetMethodDebugInformation(debugHandle);
            var sequencePoints = managedDebugInfo.GetSequencePoints();

            for (int i = 0; i < data.CountOfMapEntries; i++) {
                var ilOffset = data.ILOffset(i);
                var nativeOffset = data.NativeOffset(i);

                //if (ilOffset == (int)SpecialILOffset.NoMapping ||
                //    ilOffset == (int)SpecialILOffset.Epilog ||
                //    ilOffset == (int)SpecialILOffset.Prolog) {
                //    continue;
                //}

                bool found = false;
                int closestDist = int.MaxValue;
                SequencePoint? closestPoint = null;

                // Search for exact or closes IL offset based on
                // https://github.com/dotnet/BenchmarkDotNet/blob/0321a3176b710110af5be04e54702e19a5bee151/src/BenchmarkDotNet.Disassembler.x64/SourceCodeProvider.cs#L131
                foreach (var point in sequencePoints) { //? TODO: Slow, use some map since most ILoffsets are found exactly
                    if (point.Offset == ilOffset) {
                        closestPoint = point;
                        closestDist = 0;
                        break;
                    }

                    int dist = Math.Abs(point.Offset - (int)ilOffset);

                    if (dist < closestDist) {
                        closestDist = dist;
                        closestPoint = point;
                    }
                }

                if (closestPoint.HasValue) {
                    var doc = md.GetDocument(closestPoint.Value.Document);
                    var docName = md.GetString(doc.Name);
                    var lineInfo = new DebugSourceLineInfo(nativeOffset, closestPoint.Value.StartLine, 
                        closestPoint.Value.StartColumn, docName);
                    debugInfo.SourceLines.Add(lineInfo);
                    debugInfo.StartDebugSourceLine = lineInfo;
                }
            }
        }

    }

    private Machine FromArchitecture(Architecture arch) {
        return arch switch {
            Architecture.Amd64 => Machine.Amd64,
            Architecture.X86 => Machine.I386,
            Architecture.Arm64 => Machine.Arm64,
            Architecture.Arm => Machine.Arm,
            _ => throw new InvalidOperationException($"Unknown .NET architecture: {arch}")
        };
    }

    private bool ProcessDotNetMethodLoad(MethodLoadUnloadVerboseTraceData data, RawProfileData profile) {
        //if (string.IsNullOrEmpty(data.ProcessName) ||
        //    !data.ProcessName.Contains(targetProc)) {
        //    return false;
        //}

        //Trace.WriteLine($"=> Load at {data.MethodStartAddress:X}: {data.MethodName} {data.MethodSignature},ProcessID: {data.ProcessID}, name: {data.ProcessName}");
        //Trace.WriteLine($"     id/token: {data.MethodID}/{data.MethodToken}, opts: {data.OptimizationTier}, size: {data.MethodSize}");
        
        var runtime = GetRuntime(data.ProcessID);

        if (runtime == null) {
            return false;
        }

        var method = runtime.GetMethodByHandle((ulong)data.MethodID);
        var module = method?.Type?.Module;
        
        if (module == null) {
            return false;
        }
        
        var runtimeArch = FromArchitecture(runtime.ClrInfo.DacInfo.TargetArchitecture);
        var moduleName = Utils.TryGetFileName(module.Name);
        var (moduleDebugInfo, moduleImage) = profile.GetOrAddModuleDebugInfo(data.ProcessID, moduleName,
                                                                            (long)module.ImageBase, runtimeArch);
        if (moduleDebugInfo == null) {
            return false;
        }

        var funcRva = data.MethodStartAddress - module.Address;
        //var funcRva = data.MethodStartAddress;
        var funcName = method != null ? method.Signature : $"{data.MethodNamespace}.{data.MethodName}";
        var funcInfo = new DebugFunctionInfo(funcName, (long)funcRva, data.MethodSize, data.MethodToken);

        // Save method code by copying it from the running process.
        funcInfo.Data = CopyDotNetMethod(data.MethodStartAddress, data.MethodSize, runtime);
        funcMap_[data.MethodID] = funcInfo;

        //? Alternative is to read the managed PDB
        moduleDebugInfo.AddFunctionInfo(funcInfo);
        profile.AddManagedMethodMapping(data.MethodID, funcInfo, moduleImage, (long)data.MethodStartAddress, data.MethodSize);
        
        //Trace.WriteLine($"=> managed {funcInfo}");
        //Trace.WriteLine($"     module base {method?.Type?.Module?.ImageBase:X}, addr {method?.Type?.Module?.Address:X}");
        return true;
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

    public void Dispose() {
        source_?.Dispose();
    }
}
