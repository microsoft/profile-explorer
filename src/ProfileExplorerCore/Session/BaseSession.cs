// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection.Metadata;
using System.Threading.Tasks;
using ProfileExplorer.Core.Binary;
using ProfileExplorer.Core.Profile.Data;
using ProfileExplorer.Core.Providers;
using ProfileExplorer.Core.Settings;
using ProfileExplorer.Core.Utilities;
using System.Collections.Generic;
using ProfileExplorer.Core.Compilers.ASM;
using ProfileExplorer.Core.Compilers.Architecture;
using ProfileExplorer.Core.Profile.ETW;
using ProfileExplorer.Core.Analysis;

namespace ProfileExplorer.Core.Session;

public enum SessionKind {
  Default = 0,
  FileSession = 1,
  DebugSession = 2
}
public class BaseSession : ISession
{
    private ICompilerInfoProvider compilerInfo_;
  private CancelableTaskInstance documentLoadTask_;

  private object lockObject_;
  private List<ILoadedDocument> documents_;
  private List<CancelableTask> pendingTasks_;
  private ILoadedDocument mainDocument_;

  public ICompilerInfoProvider CompilerInfo => compilerInfo_;
    public ProfileData ProfileData { get; private set; }

    public IReadOnlyList<ILoadedDocument> Documents => documents_;

    public BaseSession()
    {
    lockObject_ = new object();
    documents_ = new List<ILoadedDocument>();
    pendingTasks_ = new List<CancelableTask>();

      compilerInfo_ = null;
    }

    public ILoadedDocument FindLoadedDocument(IRTextFunction func) {
      var summary = func.ParentSummary;
      return documents_.Find(item => item.Summary == summary);
    }

    public ICompilerInfoProvider CreateCompilerInfoProvider(IRMode mode) {
      return new ASMCompilerInfoProvider(mode);
    }

    public ILoadedDocument CreateLoadedDocument(string filePath, string modulePath, Guid id) {
      return new LoadedDocument(filePath, modulePath, id);
    }

    public ILoadedDocument CreateDummyDocument(string name) {
      return LoadedDocument.CreateDummyDocument(name);
    }

  public async Task<bool> LoadProfileData(string profileFilePath, List<int> processIds, ProfileDataProviderOptions options, SymbolFileSourceSettings symbolSettings, ProfileDataReport report, ProfileLoadProgressHandler progressCallback, CancelableTask cancelableTask) {
    var sw = Stopwatch.StartNew();
    
    // Initialize with a default compiler info provider
    if (compilerInfo_ == null) {
      compilerInfo_ = CreateCompilerInfoProvider(IRMode.Default);
    }
    
    using var provider = new ETWProfileDataProvider(compilerInfo_);
    
    var result = await provider.LoadTraceAsync(profileFilePath, processIds,
                                               options, symbolSettings,
                                               report, progressCallback, cancelableTask);

    if (result != null) {
      result.Report = report;
      ProfileData = result;
      UnloadProfilingDebugInfo();
    }

    Trace.WriteLine($"Done profile load and setup: {sw}, {sw.ElapsedMilliseconds} ms");
    Trace.Flush();
    return result != null;
  }

  private void UnloadProfilingDebugInfo() {
    if (ProfileData == null) {
      return;
    }

    // Free memory used by the debug info by unloading any objects
    // such as the PDB DIA reader using COM.
    Task.Run(() => {
      foreach ((string module, var debugInfo) in ProfileData.ModuleDebugInfo) {
        debugInfo.Unload();
      }
    });
  }

  public async Task CancelPendingTasks() {
    List<CancelableTask> tasks;

    lock (lockObject_) {
      tasks = pendingTasks_.CloneList();
    }

    foreach (var task in tasks) {
      task.Cancel();
      await task.WaitToCompleteAsync();
    }
  }
}
