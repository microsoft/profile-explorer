// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Threading.Tasks;
using IRExplorerCore;
using IRExplorerUI.Compilers;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;

namespace IRExplorerUI.Profile;

public sealed partial class ETWEventProcessor {
  private void ProcessDotNetEvents(RawProfileData profile, CancelableTask cancelableTask) {
    if (pipeServer_ != null) {
      pipeServer_.FunctionCodeReceived += (functionId, rejitId, processId, address, codeSize, codeBytes) => {
#if DEBUG
        Trace.WriteLine($"PipeServer_OnFunctionCodeReceived: {functionId}, {rejitId}, {address}, {codeSize}");
#endif
        profile.AddManagedMethodCode(functionId, rejitId, processId, address, codeSize, codeBytes);
      };

      pipeServer_.FunctionCallTargetsReceived += (functionId, rejitId, processId, address, name) => {
#if DEBUG
        Trace.WriteLine($"PipeServer_OnFunctionCallTargetsReceived: {functionId}, {rejitId}, {address}, {name}");
#endif
        profile.AddManagedMethodCallTarget(functionId, rejitId, processId, address, name);
      };

      Task.Run(() => {
        // Receive messages from the pipe client with the managed method code.
        Trace.WriteLine("Start .NET pipe server communication");
        pipeServer_.StartReceiving(cancelableTask.Token);
      });
    }

    //source_.Clr.GCStart += data => {
    //    Trace.WriteLine($"GCStart: {data}");
    //    double timestamp = data.TimeStampRelativeMSec;
    //    var counterEvent = new PerformanceCounterEvent(0,
    //        TimeSpan.FromMilliseconds(timestamp),
    //        1, (short)(1));
    //    profile.AddPerformanceCounterEvent(counterEvent);
    //};

    //source_.Clr.GCStop += data => {
    //    Trace.WriteLine($"GCStop: {data}");
    //    double timestamp = data.TimeStampRelativeMSec;
    //    var counterEvent = new PerformanceCounterEvent(0,
    //        TimeSpan.FromMilliseconds(timestamp),
    //        1, (short)(0));
    //    profile.AddPerformanceCounterEvent(counterEvent);
    //};

    source_.Clr.LoaderModuleLoad += data => {
      ProcessLoaderModuleLoad(data, profile);
    };

    source_.Clr.MethodLoadVerbose += data => {
      ProcessDotNetMethodLoad(data, profile, cancelableTask);
    };

    source_.Clr.MethodILToNativeMap += data => {
      ProcessDotNetILToNativeMap(data, profile);
    };

    // Needed when attaching to a running process to get info
    // about modules/methods loaded before the ETW session started.
    var rundownParser = new ClrRundownTraceEventParser(source_);

    rundownParser.LoaderModuleDCStart += data => {
      ProcessLoaderModuleLoad(data, profile, true);
    };

    //rundownParser.LoaderModuleDCStop += data => {
    //    ProcessLoaderModuleLoad(data, profile, true);
    //};

    rundownParser.MethodDCStartVerbose += data => {
      ProcessDotNetMethodLoad(data, profile, cancelableTask, true);
    };

    //rundownParser.MethodDCStopVerbose += data => {
    //    ProcessDotNetMethodLoad(data, profile, true);
    //};

    rundownParser.MethodILToNativeMapDCStart += data => {
      ProcessDotNetILToNativeMap(data, profile, true);
    };

    //rundownParser.MethodILToNativeMapDCStop += data => {
    //    ProcessDotNetILToNativeMap(data, profile, true);
    //};
  }

  private void ProcessLoaderModuleLoad(ModuleLoadUnloadTraceData data, RawProfileData profile, bool rundown = false) {
    if (!IsAcceptedProcess(data.ProcessID)) {
      return; // Ignore events from other processes.
    }

#if DEBUG
    Trace.WriteLine($"=> R-{rundown} Managed module {data.ModuleID}, {data.ModuleILFileName} in proc {data.ProcessID}");
#endif
    var runtimeArch = Machine.Amd64;
    string moduleName = data.ModuleILFileName;
    var moduleDebugInfo =
      profile.GetOrAddManagedModuleDebugInfo(data.ProcessID, moduleName, data.ModuleID, runtimeArch);

    if (moduleDebugInfo != null) {
      moduleDebugInfo.ManagedSymbolFile = FromModuleLoad(data);
#if DEBUG
      Trace.WriteLine($"Set managed symbol {moduleDebugInfo.ManagedSymbolFile}");
#endif
    }
  }

  private void ProcessDotNetILToNativeMap(MethodILToNativeMapTraceData data, RawProfileData profile,
                                          bool rundown = false) {
    if (!IsAcceptedProcess(data.ProcessID)) {
      return; // Ignore events from other processes.
    }

#if DEBUG
    Trace.WriteLine(
      $"=> R-{rundown} ILMap token: {data.MethodID}, entries: {data.CountOfMapEntries}, ProcessID: {data.ProcessID}, name: {data.ProcessName}");
#endif
    var methodMapping = profile.FindManagedMethod(data.MethodID, data.ReJITID, data.ProcessID);

    if (methodMapping == null) {
      return;
    }

    var ilOffsets = new List<(int ILOffset, int NativeOffset)>(data.CountOfMapEntries);

    for (int i = 0; i < data.CountOfMapEntries; i++) {
      ilOffsets.Add((data.ILOffset(i), data.NativeOffset(i)));
    }

    var (debugInfo, _) = profile.GetModuleDebugInfo(data.ProcessID, methodMapping.ModuleId);

    if (debugInfo != null) {
      debugInfo.AddMethodILToNativeMap(methodMapping.FunctionDebugInfo, ilOffsets);
    }
  }

  private void ProcessDotNetMethodLoad(MethodLoadUnloadVerboseTraceData data, RawProfileData profile,
                                       CancelableTask cancelableTask, bool rundown = false) {
    if (!IsAcceptedProcess(data.ProcessID)) {
      return; // Ignore events from other processes.
    }

    if (rundown) {
      if (pipeServer_ != null && !cancelableTask.IsCanceled) {
#if DEBUG
        Trace.WriteLine($"Request {data.MethodStartAddress:x}: {data.MethodSignature}");
#endif
        if (!pipeServer_.RequestFunctionCode((long)data.MethodStartAddress, data.MethodID, (int)data.ReJITID,
                                             data.ProcessID)) {
          Trace.WriteLine($"Failed to request rundown method {data.MethodStartAddress:x}");
        }
      }
    }

#if DEBUG
    Trace.WriteLine(
      $"=> R-{rundown} Load at {data.MethodStartAddress}: {data.MethodNamespace}.{data.MethodName}, {data.MethodSignature},ProcessID: {data.ProcessID}, name: {data.ProcessName}");
    Trace.WriteLine(
      $"     id/token: {data.MethodID}/{data.MethodToken}, opts: {data.OptimizationTier}, size: {data.MethodSize}");
#endif

    string funcName = $"{data.MethodNamespace}.{data.MethodName}";
    var funcInfo = new FunctionDebugInfo(funcName, (long)data.MethodStartAddress, data.MethodSize,
                                         (short)data.OptimizationTier, data.MethodToken, (short)data.ReJITID);
    profile.AddManagedMethodMapping(data.ModuleID, data.MethodID, data.ReJITID, funcInfo,
                                    (long)data.MethodStartAddress, data.MethodSize, data.ProcessID);
  }

  private SymbolFileDescriptor FromModuleLoad(ModuleLoadUnloadTraceData data) {
    return new SymbolFileDescriptor(data.ManagedPdbBuildPath, data.ManagedPdbSignature, data.ManagedPdbAge);
  }
}
