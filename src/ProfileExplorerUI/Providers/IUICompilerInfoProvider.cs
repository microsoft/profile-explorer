// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ProfileExplorer.UI.Query;
using ProfileExplorerCore;
using ProfileExplorerCore.Binary;
using ProfileExplorerCore.Diff;
using ProfileExplorerCore.IR;
using ProfileExplorerCore.Providers;
using ProfileExplorerCore.Settings;
using ProfileExplorerUI.Session;
using ProtoBuf;

namespace ProfileExplorer.UI;

public interface IUICompilerInfoProvider : ICompilerInfoProvider {
  new IUISession Session { get; }
  ISectionStyleProvider SectionStyleProvider { get; }
  IRRemarkProvider RemarkProvider { get; }
  List<QueryDefinition> BuiltinQueries { get; }
  List<FunctionTaskDefinition> BuiltinFunctionTasks { get; }
  List<FunctionTaskDefinition> ScriptFunctionTasks { get; }

  Task HandleLoadedSection(IRDocument document, FunctionIR function, IRTextSection section);
  Task HandleLoadedDocument(IUILoadedDocument document, string modulePath);
  IBlockFoldingStrategy CreateFoldingStrategy(FunctionIR function);
}