// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection.Metadata;
using System.Threading.Tasks;
using ProfileExplorer.Core.Analysis;
using ProfileExplorer.Core.Binary;
using ProfileExplorer.Core.Compilers.Architecture;
using ProfileExplorer.Core.Compilers.ASM;
using ProfileExplorer.Core.IR;
using ProfileExplorer.Core.IR.Tags;
using ProfileExplorer.Core.Profile.Data;
using ProfileExplorer.Core.Profile.ETW;
using ProfileExplorer.Core.Providers;
using ProfileExplorer.Core.Settings;
using ProfileExplorer.Core.Utilities;
using static System.Collections.Specialized.BitVector32;

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

    provider.SetupNewSessionRequested += OnSetupNewSessionRequested;
    provider.StartNewSessionRequested += OnStartNewSessionRequested;

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

  private async Task OnStartNewSessionRequested(string sessionName, SessionKind sessionKind, ICompilerInfoProvider compilerInfo) {
    compilerInfo_ = compilerInfo;
  }

  private async Task OnSetupNewSessionRequested(ILoadedDocument mainDocument, List<ILoadedDocument> otherDocuments, ProfileData profileData) {
    documents_.Add(mainDocument);
    documents_.AddRange(otherDocuments);
  }

  public async Task<ParsedIRTextSection> LoadAndParseSection(IRTextSection section) {
    var summary = section.ParentFunction.ParentSummary;
    var docInfo = FindLoadedDocument(section);

    // This shouldn't happen if document was loaded properly...
    if (docInfo == null || docInfo.Loader == null) {
      Trace.WriteLine($"Failed LoadAndParseSection for function {section.ParentFunction.Name}");
      return null;
    }

    var parsedSection = docInfo.Loader.LoadSection(section);

    if (parsedSection != null && parsedSection.Function != null) {
      var funcDebugInfo = ProfileData?.GetFunctionProfile(section.ParentFunction)?.FunctionDebugInfo;
      var loadedDoc = FindLoadedDocument(section.ParentFunction);
      await compilerInfo_.AnalyzeLoadedFunction(parsedSection.Function, section, loadedDoc, funcDebugInfo);
      return parsedSection;
    }

    string placeholderText = "Could not find function code";
    var dummyFunc = new FunctionIR(section.ParentFunction.Name);
    return new ParsedIRTextSection(section, placeholderText.AsMemory(), dummyFunc) {
      LoadFailed = true
    };
  }

  private ILoadedDocument FindLoadedDocument(IRTextSection section) {
    var summary = section.ParentFunction.ParentSummary;
    return documents_.Find(item => item.Summary == summary);
  }

  private ILoadedDocument FindLoadedDocument(IRTextFunction func) {
    var summary = func.ParentSummary;
    return documents_.Find(item => item.Summary == summary);
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
