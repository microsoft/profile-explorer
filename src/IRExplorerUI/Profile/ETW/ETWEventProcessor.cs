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

namespace IRExplorerUI.Profile.ETW;

public class ETWEventProcessor : IDisposable {
    private ETWTraceEventSource source_;
    private bool isRealTime_;

    public ETWEventProcessor(ETWTraceEventSource source, bool isRealTime = true) {
        source_ = source;
        isRealTime_ = isRealTime;
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

        // Default 1ms sampling interval, 1M ns.
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

            //queue.Add((StackWalkStackTraceData)data.Clone());
            var context = profile.RentTempContext(data.ProcessID, data.ThreadID, data.ProcessorNumber);
            int contextId = profile.AddContext(context);

            int frameCount = data.FrameCount;
            var stack = profile.RentTemporaryStack(frameCount, contextId);

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

            if (cancelableTask != null && cancelableTask.IsCanceled) {
                source_.StopProcessing();
            }

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

            var context = profile.RentTempContext(data.ProcessID, data.ThreadID, cpu);
            int contextId = profile.AddContext(context);
            var sample = new ProfileSample((long)data.InstructionPointer,
                                           TimeSpan.FromMilliseconds(timestamp),
                                           TimeSpan.FromMilliseconds(weight),
                                           isKernelCode, contextId);
            int sampleId = profile.AddSample(sample);

            profile.ReturnContext(contextId);

            perCoreLastSample[cpu] = sampleId;
            perContextLastSample[contextId] = sampleId;

            int sampleCount = profile.samples_.Count;

            if (sampleCount - lastReportedSampleCount >= sampleReportInterval) {
                progressCallback?.Invoke(new ProfileLoadProgress(ProfileLoadStage.TraceLoading) {
                    Total = sampleCount,
                    Current = sampleCount
                });
                lastReportedSampleCount = sampleCount;
            }
        };

        source_.Process();

        Trace.WriteLine($"Done processing ETW events");
        Trace.WriteLine($"    stacks: {profile.stacks_.Count}");
        Trace.WriteLine($"    samples: {profile.samples_.Count}");
        Trace.WriteLine($"    ctxs: {profile.contexts_.Count}");
        Trace.WriteLine($"    procs: {profile.processes_.Count}");
        Trace.WriteLine($"    imgs: {profile.imagesMap_.Count}");
        Trace.WriteLine($"    threads: {profile.threadsMap_.Count}");

        profile.LoadingCompleted();
        return profile;
    }

    public void Dispose() {
        source_?.Dispose();
    }
}
