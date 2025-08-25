// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Reflection.Metadata;
using System.Threading.Tasks;
using ProfileExplorerCore2.Binary;
using ProfileExplorerCore2.Profile.Data;
using ProfileExplorerCore2.Providers;
using ProfileExplorerCore2.Settings;
using ProfileExplorerCore2.Utilities;

namespace ProfileExplorerCore2.Session;

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