﻿// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using IRExplorerCore;
using IRExplorerUI.Compilers;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Parsers.Symbol;

namespace IRExplorerUI.Profile;

public sealed partial class ETWEventProcessor : IDisposable {
  public const int KernelProcessId = 0;
  private const double SamplingErrorMargin = 1.1; // 10% deviation from sampling interval allowed.
  private const int SampleReportingInterval = 10000;
  private const int MaxCoreCount = 4096;
  private ETWTraceEventSource source_;
  private string tracePath_;
  private bool isRealTime_;
  private bool handleDotNetEvents_;
  private int acceptedProcessId_;
  private bool handleChildProcesses_;
  private ProfilerNamedPipeServer pipeServer_;
  private List<int> childAcceptedProcessIds_;
  private ProfileDataProviderOptions providerOptions_;
  private bool samplingIntervalSet_;
  private int samplingInterval100NS_;
  private double samplingIntervalMS_;
  private double samplingIntervalLimitMS_;

  public ETWEventProcessor(ETWTraceEventSource source, ProfileDataProviderOptions providerOptions,
                           bool isRealTime = true,
                           int acceptedProcessId = 0, bool handleChildProcesses = false,
                           bool handleDotNetEvents = false,
                           ProfilerNamedPipeServer pipeServer = null) {
    Trace.WriteLine($"New ETWEventProcessor: ProcId {acceptedProcessId}, handleDotNet: {handleDotNetEvents}");
    source_ = source;
    providerOptions_ = providerOptions;
    isRealTime_ = isRealTime;
    acceptedProcessId_ = acceptedProcessId;
    handleDotNetEvents_ = handleDotNetEvents;
    handleChildProcesses_ = handleChildProcesses;
    pipeServer_ = pipeServer;
    childAcceptedProcessIds_ = new List<int>();
  }

  public ETWEventProcessor(string tracePath, ProfileDataProviderOptions providerOptions, int acceptedProcessId = 0) {
    Debug.Assert(File.Exists(tracePath));
    childAcceptedProcessIds_ = new List<int>();
    source_ = new ETWTraceEventSource(tracePath);
    tracePath_ = tracePath;
    providerOptions_ = providerOptions;
    acceptedProcessId_ = acceptedProcessId;
  }

  public static bool IsKernelAddress(ulong ip, int pointerSize) {
    if (pointerSize == 4) {
      return ip >= 0x80000000;
    }

    return ip >= 0xFFFF000000000000;
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

        sb.Append((char)(first | second << 8));
        offset += 2;
      }

      return sb.ToString();
    }
  }

  public List<ProcessSummary> BuildProcessSummary(ProcessListProgressHandler progressCallback,
                                                  CancelableTask cancelableTask) {
    // Default 1ms sampling interval.
    UpdateSamplingInterval(SampleReportingInterval);

    source_.Kernel.PerfInfoCollectionStart += data => { // We don't recieve this event... why is it here?
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

    // Use a dummy process summary to collect all the process info.
    var profile = new RawProfileData(tracePath_);
    var summaryBuilder = new ProcessSummaryBuilder(profile);

    int lastReportedSample = 0;
    int lastProcessListSample = 0;
    int nextProcessListSample = SampleReportingInterval * 10;
    int sampleId = 0;
    var lastProcessListReport = DateTime.UtcNow;

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

      sampleId++;
      var sampleWeight = TimeSpan.FromMilliseconds(samplingIntervalMS_);
      var sampleTime = TimeSpan.FromMilliseconds(data.TimeStampRelativeMSec);
      summaryBuilder.AddSample(sampleWeight, sampleTime, context);
      profile.ReturnContext(contextId);

      // Rebuild process list and update UI from time to time.
      if (sampleId - lastReportedSample >= SampleReportingInterval) {
        List<ProcessSummary> processList = null;
        var currentTime = DateTime.UtcNow;

        if (sampleId - lastProcessListSample >= nextProcessListSample &&
            (currentTime - lastProcessListReport).TotalMilliseconds > 1000) {
          // Rebuild the process list every few seconds.
          processList = summaryBuilder.MakeSummaries();
          lastProcessListSample = sampleId;
          lastProcessListReport = currentTime;
        }

        if (progressCallback != null) {
          int current = (int)data.TimeStampRelativeMSec; // Copy since data gets reused.
          int total = (int)source_.SessionDuration.TotalMilliseconds;

          ThreadPool.QueueUserWorkItem(state => {
            progressCallback(new ProcessListProgress {
              Total = total,
              Current = current,
              Processes = processList
            });
          });
        }

        lastReportedSample = sampleId;
      }
    };

    // Go over events and accumulate samples to build the process summary.
    source_.Process();

    if (cancelableTask.IsCanceled) {
      return new List<ProcessSummary>();
    }

    profile.Dispose();
    return summaryBuilder.MakeSummaries();
  }

  public RawProfileData ProcessEvents(ProfileLoadProgressHandler progressCallback,
                                      CancelableTask cancelableTask) {
    UpdateSamplingInterval(SampleReportingInterval);
    ImageIDTraceData lastImageIdData = null;
    ProfileImage lastProfileImage = null;
    long lastProfileImageTime = 0;

    // Info used to associate a sample with the last call stack running on a CPU core.
    double[] perCoreLastTime = new double[MaxCoreCount];
    int[] perCoreLastSample = new int[MaxCoreCount];
    var perCoreLastKernelStack = new (int StackId, long Timestamp)[MaxCoreCount];
    var perContextLastSample = new Dictionary<int, int>();
    int lastReportedSample = 0;

    // Info used to handle compressed stack event
    var kernelStackKeyToPendingSamples = new Dictionary<ulong, List<int>>();
    var userStackKeyToPendingSamples = new Dictionary<ulong, List<int>>();
    var profile = new RawProfileData(tracePath_, handleDotNetEvents_);

    // For ETL file, the image timestamp (needed to find a binary on a symbol server)
    // can show up in the ImageID event instead the usual Kernel.ImageGroup.
    var symbolParser = new SymbolTraceEventParser(source_);

    symbolParser.ImageID += data => {
      // The image timestamp often is part of this event when reading an ETL file.
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

    symbolParser.ImageIDDbgID_RSDS += data => {
      if (IsAcceptedProcess(data.ProcessID)) {
        Trace.WriteLine($"PDB signature: imageBase: {data.ImageBase}, file: {data.PdbFileName}, age: {data.Age}, guid: {data.GuidSig}");
        var symbolFile = new SymbolFileDescriptor(data.PdbFileName, data.GuidSig, data.Age);
        profile.AddDebugFileForImage(symbolFile, (long)data.ImageBase, data.ProcessID);
      }
    };

    // Start of main ETW event handlers.
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

    source_.Kernel.ThreadSetName += data => {
      if (IsAcceptedProcess(data.ProcessID)) {
        var proc = profile.GetOrCreateProcess(data.ProcessID);
        var thread = proc.FindThread(data.ThreadID, profile);

        if (thread != null) {
          thread.Name = data.ThreadName;
        }
      }
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
      if (!IsAcceptedProcess(data.ProcessID)) {
        return; // Ignore events from other processes.
      }

      bool isKernelStack = data.FrameCount > 0 &&
                           IsKernelAddress(data.InstructionPointer(data.FrameCount - 1), data.PointerSize);
      bool isKernelStackStart = data.FrameCount > 0 &&
                                IsKernelAddress(data.InstructionPointer(0), data.PointerSize);

      // if (data.FrameCount > 0) {
      //   Trace.WriteLine("-----------------------------------");
      //   Trace.WriteLine($"Stack {data.InstructionPointer(0):X}, timestamp {data.EventTimeStampRelativeMSec}, TS {data.EventTimeStampQPC}, thread {data.ThreadID}");
      //   Trace.WriteLine($"   kernel {isKernelStack}, kernelStart {isKernelStackStart}");
      // }

      //? TODO: Change
      // - per thread list of unresolved kernel stacks
      //     - search by matching timestamp
      //     - clear list once user stack processed
      // - assoc sample stack only on matching IP
      //? TODO: Also fix StackWalkStackKeyKernel

      //Trace.WriteLine($"User stack {data.InstructionPointer(0):X}, proc {data.ProcessID}, name {data.ProcessName}, TS {data.EventTimeStampQPC}");
      var context = profile.RentTempContext(data.ProcessID, data.ThreadID, data.ProcessorNumber);
      int contextId = profile.AddContext(context);
      int frameCount = data.FrameCount;
      ProfileStack kstack = null;

      if (!isKernelStack && !isKernelStackStart) {
        // This is a user mode stack, check if before it an associated
        // kernel mode stack was recorded - if so, merge the two stacks.
        var lastKernelStack = perCoreLastKernelStack[data.ProcessorNumber];

        if (lastKernelStack.StackId != 0 &&
            lastKernelStack.Timestamp == data.EventTimeStampQPC) {
          //Trace.WriteLine($"  Found matching KernelStack {lastKernelStack.StackId} at {lastKernelStack.Timestamp} on CPU {data.ProcessorNumber}");

          // Append at the end of the kernel stack, marking a user -> kernel mode transition.
          kstack = profile.FindStack(lastKernelStack.StackId);
          int kstackFrameCount = kstack.FrameCount;
          long[] frames = new long[kstack.FrameCount + data.FrameCount];
          kstack.FramePointers.CopyTo(frames, 0);

          //Trace.WriteLine($"    kernel mode end IP: {frames[kstackFrameCount - 1]:X}");
          //Trace.WriteLine($"    user mode start IP: {data.InstructionPointer(0):X}");

          for (int i = 0; i < frameCount; i++) {
            frames[kstackFrameCount + i] = (long)data.InstructionPointer(i);
          }

          kstack.FramePointers = frames;
          kstack.UserModeTransitionIndex = kstackFrameCount; // Frames after index are user mode.

          //? TODO
          perCoreLastKernelStack[data.ProcessorNumber] = (0, 0); // Clear the last kernel stack.
        }
      }

      if (kstack == null) {
        // This is either a kernel mode stack, or a user mode stack with no associated kernel mode stack.
        var stack = profile.RentTemporaryStack(frameCount, contextId);

        // Copy data from event to the temp. stack pointer array.
        // Slightly faster to copy the entire array as a whole.
        unsafe {
          var ptr = (void*)((IntPtr)(void*)data.DataStart + 16);
          int bytes = data.PointerSize * frameCount;
          var span = new Span<byte>(ptr, bytes);

          fixed (long* destPtr = stack.FramePointers) {
            var destSpan = new Span<byte>(destPtr, bytes);
            span.CopyTo(destSpan);
          }
        }

        int stackId = profile.AddStack(stack, context);

        // if (isKernelStack) {
        //   Trace.WriteLine($"  New KernelStack {stackId} at {data.EventTimeStampQPC}");
        // }
        // else {
        //   Trace.WriteLine($"  New UserStack {stackId} at {data.EventTimeStampQPC}");
        // }

        // Try to associate with a previous sample from the same context.
        int sampleId = perCoreLastSample[data.ProcessorNumber];
        long frameIp = (long)data.InstructionPointer(0);

        //? TODO: Check fmore than the last sample?
        if (!profile.TrySetSampleStack(sampleId, stackId, frameIp, contextId)) {
#if DEBUG
          Trace.WriteLine($"Couldn't set stack {stackId} for sample {sampleId}");
#endif
        }

        if (isKernelStack) {
          //Trace.WriteLine($"    register KernelStack {stackId} on CPU {data.ProcessorNumber}");
          perCoreLastKernelStack[data.ProcessorNumber] = (stackId, data.EventTimeStampQPC);
        }

        profile.ReturnStack(stackId);
      }

      profile.ReturnContext(contextId);
    };

    source_.Kernel.StackWalkStackKeyKernel += data => {
      if (!IsAcceptedProcess(data.ProcessID)) {
        return; // Ignore events from other processes.
      }

      var context = profile.RentTempContext(data.ProcessID, data.ThreadID, data.ProcessorNumber);
      int contextId = profile.AddContext(context);

      int sampleId = perCoreLastSample[data.ProcessorNumber];
      var triggeringEventTimestamp = TimeSpan.FromMilliseconds(data.EventTimeStampRelativeMSec);

      // Check if the last sample on the core did not trigger this stack collection
      if (sampleId == 0 || profile.Samples[sampleId - 1].Time != triggeringEventTimestamp) {
        // Check if the last sample from the context did not trigger this stack collection
        if (!perContextLastSample.TryGetValue(contextId, out sampleId) ||
            profile.Samples[sampleId - 1].Time != triggeringEventTimestamp) {
          // We don't know what sample this stack belongs to so we won't collect it
          return;
        }
      }

      // Add the sample id to our list of pending samples for this stack key so when the definition comes along we can add the stack to our profile
      if (kernelStackKeyToPendingSamples.TryGetValue(data.StackKey, out var pendingSamples)) {
        pendingSamples.Add(sampleId);
      }
      else {
        kernelStackKeyToPendingSamples.Add(data.StackKey, new List<int> {sampleId});
      }

      profile.ReturnContext(contextId);
    };

    source_.Kernel.StackWalkStackKeyUser += delegate(StackWalkRefTraceData data) {
      if (!IsAcceptedProcess(data.ProcessID)) {
        return; // Ignore events from other processes.
      }

      var context = profile.RentTempContext(data.ProcessID, data.ThreadID, data.ProcessorNumber);
      int contextId = profile.AddContext(context);

      int sampleId = perCoreLastSample[data.ProcessorNumber];
      var triggeringEventTimestamp = TimeSpan.FromMilliseconds(data.EventTimeStampRelativeMSec);

      // Check if the last sample on the core did not trigger this stack collection
      if (sampleId == 0 || profile.Samples[sampleId - 1].Time != triggeringEventTimestamp) {
        // Check if the last sample from the context did not trigger this stack collection
        if (!perContextLastSample.TryGetValue(contextId, out sampleId) ||
            profile.Samples[sampleId - 1].Time != triggeringEventTimestamp) {
          // We don't know what sample this stack belongs to so we won't collect it
          return;
        }
      }

      // Add the sample id to our list of pending samples for this stack key so when the definition comes along we can add the stack to our profile
      if (userStackKeyToPendingSamples.TryGetValue(data.StackKey, out var pendingSamples)) {
        pendingSamples.Add(sampleId);
      }
      else {
        userStackKeyToPendingSamples.Add(data.StackKey, new List<int> {sampleId});
      }

      profile.ReturnContext(contextId);
    };

    source_.Kernel.AddCallbackForEvents(delegate(StackWalkDefTraceData data) {
      if (data.FrameCount == 0 ||
          userStackKeyToPendingSamples.Count == 0 && kernelStackKeyToPendingSamples.Count == 0) {
        return; // Ignore data that won't fulfill any pending samples
      }

      bool isKernelAddress = IsKernelAddress(data.InstructionPointer(0), data.PointerSize);
      List<int> pendingSamples;

      if (isKernelAddress && !kernelStackKeyToPendingSamples.TryGetValue(data.StackKey, out pendingSamples)) {
        return;
      }

      if (!userStackKeyToPendingSamples.TryGetValue(data.StackKey, out pendingSamples)) {
        return;
      }

      foreach (int sampleId in pendingSamples) {
        var sample = profile.Samples[sampleId - 1];

        // Check if we already have part of the stack for this sample
        if (sample.StackId == 0) {
          var profileStack = profile.RentTemporaryStack(data.FrameCount, sample.ContextId);

          // Copy data from event to the temp. stack pointer array.
          // Slightly faster to copy the entire array as a whole.
          unsafe {
            var ptr = (void*)((IntPtr)(void*)data.DataStart + 8);
            int bytes = data.PointerSize * data.FrameCount;
            var span = new Span<byte>(ptr, bytes);

            fixed (long* destPtr = profileStack.FramePointers) {
              var destSpan = new Span<byte>(destPtr, bytes);
              span.CopyTo(destSpan);
            }
          }

          int stackId = profile.AddStack(profileStack, profile.FindContext(sample.ContextId));
          profile.SetSampleStack(sampleId, stackId, sample.ContextId);

          profile.ReturnStack(stackId);
        }
        else if (isKernelAddress) {
          var profileStack = profile.FindStack(sample.StackId);
          int ustackFrameCount = profileStack.FrameCount;
          int kstackFrameCount = data.FrameCount;
          long[] frames = new long[kstackFrameCount + ustackFrameCount];
          profileStack.FramePointers.CopyTo(frames, kstackFrameCount);

          for (int i = 0; i < kstackFrameCount; i++) {
            frames[i] = (long)data.InstructionPointer(i);
          }

          profileStack.FramePointers = frames;
          profileStack.UserModeTransitionIndex = kstackFrameCount; // Frames after kernel stack are user mode.
        }
        else {
          var profileStack = profile.FindStack(sample.StackId);
          int ustackFrameCount = data.FrameCount;
          int kstackFrameCount = profileStack.FrameCount;
          long[] frames = new long[kstackFrameCount + ustackFrameCount];
          profileStack.FramePointers.CopyTo(frames, 0);

          for (int i = 0; i < ustackFrameCount; i++) {
            frames[i + kstackFrameCount] = (long)data.InstructionPointer(i);
          }

          profileStack.FramePointers = frames;
          profileStack.UserModeTransitionIndex = kstackFrameCount; // Frames after kernel stack are user mode.
        }
      }

      if (isKernelAddress) {
        kernelStackKeyToPendingSamples.Remove(data.StackKey);
      }
      else {
        userStackKeyToPendingSamples.Remove(data.StackKey);
      }
    });

    void HandlePerfInfoCollection(SampledProfileIntervalTraceData data, RawProfileData profile) {
      if (data.SampleSource == 0) {
        UpdateSamplingInterval(data.NewInterval);
        profile.TraceInfo.SamplingInterval = TimeSpan.FromMilliseconds(samplingIntervalMS_);
        samplingIntervalSet_ = true;
      }
      else {
        // The description of a PMC event.
        var dataSpan = data.EventData().AsSpan();
        string name = ReadWideString(dataSpan, 12);
        var counterInfo = new PerformanceCounter(data.SampleSource, name, data.NewInterval);
        profile.AddPerformanceCounter(counterInfo);
      }
    }

    source_.Kernel.PerfInfoCollectionStart +=
      data => HandlePerfInfoCollection(
        data, profile); // I haven't really seen us recieve this event - was it just a mistake?

    source_.Kernel.PerfInfoCollectionEnd += data => HandlePerfInfoCollection(data, profile);

    source_.Kernel.PerfInfoSetInterval += data => {
      if (data.SampleSource == 0 && !samplingIntervalSet_) {
        UpdateSamplingInterval(data.OldInterval);
        profile.TraceInfo.SamplingInterval = TimeSpan.FromMilliseconds(samplingIntervalMS_);
        samplingIntervalSet_ = true;
      }
    };

    source_.Kernel.PerfInfoSample += data => {
      if (!IsAcceptedProcess(data.ProcessID)) {
        return; // Ignore events from other processes.
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
      // Trace.WriteLine($"Sample {sampleId}, timestamp {timestamp}, IP {data.InstructionPointer:X} kernel {isKernelCode}, CPU {cpu}, thread {data.ThreadID}");

      // Remember the sample, to be matched later with a call stack.
      perCoreLastSample[cpu] = sampleId;
      perContextLastSample[contextId] = sampleId;

      // Report progress.
      if (progressCallback != null && sampleId - lastReportedSample >= SampleReportingInterval) {
        if (cancelableTask != null && cancelableTask.IsCanceled) {
          source_.StopProcessing();
        }

        int current = (int)data.TimeStampRelativeMSec; // Copy since data gets reused.
        int total = (int)source_.SessionDuration.TotalMilliseconds;
        UpdateProgress(progressCallback, ProfileLoadStage.TraceReading, total, current);
      }
    };

    //    source_.Kernel.ThreadCSwitch += data => {
    //        if (!(IsAcceptedProcess(data.OldProcessID) || IsAcceptedProcess(data.NewProcessID))) {
    //            return; // Ignore events from other processes.
    //        }

    //        Trace.WriteLine($"ThreadCSwitch {data}");
    //        Trace.WriteLine($"   switch from TId/PId: {data.OldThreadID}/{data.OldProcessID} to new TId/PId {data.NewThreadID}/{data.NewProcessID}, old reason {data.OldThreadWaitReason}, oldstate {data.OldThreadState}");

    //        //var context = profile.RentTempContext(data.ProcessID, data.ThreadID, data.ProcessorNumber);
    //        //int contextId = profile.AddContext(context);
    //        //double timestamp = data.TimeStampRelativeMSec;

    //        //bool isScheduledOut = data.OldThreadState == ThreadState.Wait ||
    //        //                      data.OldThreadState == ThreadState.Standby;

    //        //var counterEvent = new PerformanceCounterEvent(0,
    //        //    TimeSpan.FromMilliseconds(timestamp),
    //        //    contextId, (short)(isScheduledOut ? 1 : 0));
    //        //profile.AddPerformanceCounterEvent(counterEvent);
    //    };

    ////FileIOCreate: < Event MSec = "7331.6505" PID = "10344" PName = "threads" TID = "10392" EventName = "FileIO/Create" IrpPtr = "0xFFFFD58C43FBDB48" FileObject = "0xFFFFD58C608455D0" CreateOptions = "FILE_ATTRIBUTE_ARCHIVE, FILE_ATTRIBUTE_DEVICE" CreateDisposition = "OPEN_EXISTING" FileAttributes = "Normal" ShareAccess = "ReadWrite" FileName = "C:\test\results.log" />
    ////FileIORead: < Event MSec = "7332.1418" PID = "10344" PName = "threads" TID = "10392" EventName = "FileIO/Read" FileName = "C:\test\results.log" Offset = "53,248" IrpPtr = "0xFFFFD58C43FBDB48" FileObject = "0xFFFFD58C608455D0" FileKey = "0xFFFF8C891339EB10" IoSize = "4,096" IoFlags = "0" />
    //                                                                                                                                                                                                                                                                                            source_.Kernel.FileIOCreate += data => {
    //        if (!IsAcceptedProcess(data.ProcessID)) {
    //            return; // Ignore events from other processes.
    //        }
    //        Trace.WriteLine($"FileIOCreate: {data}\n");
    //    };

    //    source_.Kernel.FileIOClose += data => {
    //        if (!IsAcceptedProcess(data.ProcessID)) {
    //            return; // Ignore events from other processes.
    //        }
    //        Trace.WriteLine($"FileIOClose: {data}\n");
    //    };
    //    source_.Kernel.FileIORead += data => {
    //        if (!IsAcceptedProcess(data.ProcessID)) {
    //            return; // Ignore events from other processes.
    //        }
    //        Trace.WriteLine($"FileIORead: {data}\n");
    //        //double timestamp = data.TimeStampRelativeMSec;
    //        //var counterEvent = new PerformanceCounterEvent(0,
    //        //    TimeSpan.FromMilliseconds(timestamp),
    //        //    1, (short)(1));
    //        //profile.AddPerformanceCounterEvent(counterEvent);
    //    };
    //    source_.Kernel.FileIOName+= data => {
    //        if (!IsAcceptedProcess(data.ProcessID)) {
    //            return; // Ignore events from other processes.
    //        }
    //        Trace.WriteLine($"FileIOName: {data}\n");
    //    };

    //    source_.Kernel.DiskIOReadInit += data => {
    //        if (!IsAcceptedProcess(data.ProcessID)) {
    //            return; // Ignore events from other processes.
    //        }
    //        Trace.WriteLine($"DiskIOReadInit: {data}\n");
    //    };

    //    source_.Kernel.DiskIORead += data => {
    //        if (!IsAcceptedProcess(data.ProcessID)) {
    //            return; // Ignore events from other processes.
    //        }
    //        Trace.WriteLine($"DiskIORead: {data}\n");
    //    };

#if false
        GC.Collect();
        GC.WaitForPendingFinalizers();
        long memory = GC.GetTotalMemory(true);
#endif

    if (providerOptions_.IncludePerformanceCounters) {
      Trace.WriteLine("Collecting PMC events");

      source_.Kernel.PerfInfoPMCSample += data => {
        if (!IsAcceptedProcess(data.ProcessID)) {
          return; // Ignore events from other processes.
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
      ProcessDotNetEvents(profile, cancelableTask);
    }

    // Go over all ETW events, which will call the registered handlers.
    try {
      Trace.WriteLine("Start processing ETW events");
      var sw = Stopwatch.StartNew();

      if (isRealTime_) {
        profile.TraceInfo.ProfileStartTime = DateTime.Now;
      }

      UpdateProgress(progressCallback, ProfileLoadStage.TraceReading, 0, 0);
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

    Trace.WriteLine("Done processing ETW events");
    Trace.WriteLine($"  samples: {profile.Samples.Count}");
    Trace.WriteLine($"  events: {profile.PerformanceCountersEvents.Count}");
    //Trace.Flush();

    profile.LoadingCompleted();

    if (handleDotNetEvents_) {
      profile.ManagedLoadingCompleted();
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
#endif
    return profile;
  }

  public void Dispose() {
    source_?.Dispose();
    source_ = null;
  }

  private void UpdateSamplingInterval(int value) {
    samplingInterval100NS_ = value;
    samplingIntervalMS_ = (double)samplingInterval100NS_ / 10000;
    samplingIntervalLimitMS_ = samplingIntervalMS_ * SamplingErrorMargin;
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

  private bool IsAcceptedProcess(int processID) {
    if (acceptedProcessId_ == 0) {
      return true; // No filtering.
    }

    if (processID == acceptedProcessId_ ||
        processID == 0) { // Always accept the System process.
      return true;
    }

    return childAcceptedProcessIds_.Contains(processID);
  }
}