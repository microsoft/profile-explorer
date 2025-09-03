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
  private ProfileData profileData_;
  private List<ILoadedDocument> documents_;

  public ICompilerInfoProvider CompilerInfo => compilerInfo_;
  public ProfileData ProfileData => profileData_;
  public IReadOnlyList<ILoadedDocument> Documents => documents_;

  public BaseSession()
  {
    profileData_ = null;
    documents_ = new List<ILoadedDocument>();
  }

  public async Task<bool> LoadProfileData(string profileFilePath, List<int> processIds, ProfileDataProviderOptions options, SymbolFileSourceSettings symbolSettings, ProfileDataReport report, ProfileLoadProgressHandler progressCallback, CancelableTask cancelableTask) {    
    using var provider = new ETWProfileDataProvider();
    
    var result = await provider.LoadTraceAsync(profileFilePath, processIds,
                                               options, symbolSettings,
                                               report, progressCallback, cancelableTask);

    if (result != null) {
      result.Report = report;
      profileData_ = result;
      UnloadProfilingDebugInfo();
    }

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
}
