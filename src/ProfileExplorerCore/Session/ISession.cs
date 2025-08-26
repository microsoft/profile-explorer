// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Reflection.Metadata;
using System.Threading.Tasks;
using ProfileExplorerCore.Binary;
using ProfileExplorerCore.Profile.Data;
using ProfileExplorerCore.Providers;
using ProfileExplorerCore.Settings;
using ProfileExplorerCore.Utilities;

namespace ProfileExplorerCore.Session;

public interface ISession {
  ICompilerInfoProvider CompilerInfo { get; }
  ProfileData ProfileData { get; }
  IReadOnlyList<ILoadedDocument> Documents { get; }

  ILoadedDocument FindLoadedDocument(IRTextFunction func);

  Task<bool> StartNewSession(string sessionName, SessionKind sessionKind, ICompilerInfoProvider compilerInfo);
  Task<bool> SetupNewSession(ILoadedDocument mainDocument, List<ILoadedDocument> otherDocuments, ProfileData profileData);

  Task<ILoadedDocument> LoadProfileBinaryDocument(string filePath, string modulePath, IDebugInfoProvider debugInfo = null);
  Task<bool> LoadProfileData(string profileFilePath, List<int> processIds, ProfileDataProviderOptions options, SymbolFileSourceSettings symbolSettings, ProfileDataReport report, ProfileLoadProgressHandler progressCallback, CancelableTask cancelableTask);
}