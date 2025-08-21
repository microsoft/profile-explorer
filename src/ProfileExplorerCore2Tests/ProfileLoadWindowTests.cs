// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ProfileExplorerCore2;
using ProfileExplorerCore2.Binary;
using ProfileExplorerCore2.Compilers.Architecture;
using ProfileExplorerCore2.Compilers.ASM;
using ProfileExplorerCore2.IR;
using ProfileExplorerCore2.IR.Tags;
using ProfileExplorerCore2.Profile.Data;
using ProfileExplorerCore2.Profile.ETW;
using ProfileExplorerCore2.Profile.Processing;
using ProfileExplorerCore2.Providers;
using ProfileExplorerCore2.Session;
using ProfileExplorerCore2.Settings;
using ProfileExplorerCore2.Utilities;
using ProfileExplorerCore2.Windows;
using Xunit;

namespace ProfileExplorerCore2Tests;

public class ProfileLoadWindowTests
{
    [Fact]
    public async Task LoadTraceAndProcess_NoErrorsOrExceptions()
    {
        // Arrange
        var session = new TestSession();
        var window = new ProfileLoadWindow(session);
        string etlPath = @"C:\Users\benjaming.REDMOND\OneDrive - Microsoft\Documents\My Documents\Tracing\trace.etl";
        int processId = 34376;

        // Act & Assert
        var processList = await window.LoadProcessList(etlPath);
        Assert.NotNull(processList);
        window.SelectProcess(processId);
        var report = await window.LoadProfileTrace();
        Assert.NotNull(report);
    }
}

// Minimal ISession implementation for testing
public class TestSession : ISession {
  public IRTextSection CurrentDocumentSection => throw new NotImplementedException();

  public ICompilerInfoProvider CompilerInfo => throw new NotImplementedException();

  public SessionStateManager SessionState => throw new NotImplementedException();

  public bool IsSessionStarted => throw new NotImplementedException();

  public bool IsInDiffMode => throw new NotImplementedException();

  public bool IsInTwoDocumentsDiffMode => throw new NotImplementedException();

  public bool IsInTwoDocumentsMode => throw new NotImplementedException();

  public IRTextSummary MainDocumentSummary => throw new NotImplementedException();

  public IRTextSummary DiffDocumentSummary => throw new NotImplementedException();

  public ProfileData ProfileData => throw new NotImplementedException();

  public ProfileFilterState ProfileFilter { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

  public Task<bool> FilterProfileSamples(ProfileFilterState filter) {
    throw new NotImplementedException();
  }

  public IRTextFunction FindFunctionWithId(int funcNumber, Guid summaryId) {
    throw new NotImplementedException();
  }

  public Task<IDebugInfoProvider> GetDebugInfoProvider(IRTextFunction function) {
    throw new NotImplementedException();
  }

  public IRTextSummary GetDocumentSummary(IRTextSection section) {
    throw new NotImplementedException();
  }

  public Task<string> GetDocumentTextAsync(IRTextSummary summary) {
    throw new NotImplementedException();
  }

  public IRTextSection GetNextSection(IRTextSection section) {
    throw new NotImplementedException();
  }

  public IRTextSection GetPreviousSection(IRTextSection section) {
    throw new NotImplementedException();
  }

  public Task<string> GetSectionOutputTextAsync(IRPassOutput output, IRTextSection section) {
    throw new NotImplementedException();
  }

  public Task<List<string>> GetSectionOutputTextLinesAsync(IRPassOutput output, IRTextSection section) {
    throw new NotImplementedException();
  }

  public Task<ParsedIRTextSection> LoadAndParseSection(IRTextSection section) {
    throw new NotImplementedException();
  }

  public Task<LoadedDocument> LoadProfileBinaryDocument(string filePath, string modulePath, IDebugInfoProvider debugInfo = null) {
    throw new NotImplementedException();
  }

  public Task<bool> LoadProfileData(string profileFilePath, List<int> processIds, ProfileDataProviderOptions options, SymbolFileSourceSettings symbolSettings, ProfileDataReport report, ProfileLoadProgressHandler progressCallback, CancelableTask cancelableTask) {
    throw new NotImplementedException();
  }

  public Task<bool> LoadProfileData(RawProfileData data, List<int> processIds, ProfileDataProviderOptions options, SymbolFileSourceSettings symbolSettings, ProfileDataReport report, ProfileLoadProgressHandler progressCallback, CancelableTask cancelableTask) {
    throw new NotImplementedException();
  }

  public Task<LoadedDocument> OpenSessionDocument(string filePath) {
    throw new NotImplementedException();
  }

  public Task<bool> RemoveProfileSamplesFilter() {
    throw new NotImplementedException();
  }

  public Task<bool> SaveSessionDocument(string filePath) {
    throw new NotImplementedException();
  }

  public void SetApplicationProgress(bool visible, double percentage, string title = null) {
    throw new NotImplementedException();
  }

  public void SetApplicationStatus(string text, string tooltip = "") {
    throw new NotImplementedException();
  }

  public void SetSectionAnnotationState(IRTextSection section, bool hasAnnotations) {
    throw new NotImplementedException();
  }

  public Task<bool> SetupNewSession(LoadedDocument mainDocument, List<LoadedDocument> otherDocuments, ProfileData profileData) {
    throw new NotImplementedException();
  }

  public Task<bool> StartNewSession(string sessionName, SessionKind sessionKind, ICompilerInfoProvider compilerInfo) {
    throw new NotImplementedException();
  }

  public Task SwitchActiveFunction(IRTextFunction function, bool handleProfiling = true) {
    throw new NotImplementedException();
  }

  public void UpdateDocumentTitles() {
    throw new NotImplementedException();
  }

  public void UpdatePanelTitles() {
    throw new NotImplementedException();
  }
}
