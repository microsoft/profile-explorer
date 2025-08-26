// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Reflection.Metadata;
using System.Threading.Tasks;
using ProfileExplorer.Core.Binary;
using ProfileExplorer.Core.Compilers.Architecture;
using ProfileExplorer.Core.Profile.Data;
using ProfileExplorer.Core.Providers;
using ProfileExplorer.Core.Settings;
using ProfileExplorer.Core.Utilities;

namespace ProfileExplorer.Core.Session;

public interface ISession {
  ICompilerInfoProvider CompilerInfo { get; }
  ProfileData ProfileData { get; }
  IReadOnlyList<ILoadedDocument> Documents { get; }

  ILoadedDocument FindLoadedDocument(IRTextFunction func);

  ICompilerInfoProvider CreateCompilerInfoProvider(IRMode mode);
  Task<bool> StartNewSession(string sessionName, SessionKind sessionKind, ICompilerInfoProvider compilerInfo);
  Task<bool> SetupNewSession(ILoadedDocument mainDocument, List<ILoadedDocument> otherDocuments, ProfileData profileData);

  Task<ILoadedDocument> LoadProfileBinaryDocument(string filePath, string modulePath, IDebugInfoProvider debugInfo = null);
  Task<bool> LoadProfileData(string profileFilePath, List<int> processIds, ProfileDataProviderOptions options, SymbolFileSourceSettings symbolSettings, ProfileDataReport report, ProfileLoadProgressHandler progressCallback, CancelableTask cancelableTask);
}