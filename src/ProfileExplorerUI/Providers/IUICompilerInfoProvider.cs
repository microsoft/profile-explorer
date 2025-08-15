// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ProfileExplorer.Core;
using ProfileExplorer.Core.IR;
using ProfileExplorer.UI;
using ProfileExplorer.UI.Compilers;
using ProfileExplorer.UI.Diff;
using ProfileExplorer.UI.Query;
using ProtoBuf;

namespace ProfileExplorer.Core;

public interface IUICompilerInfoProvider : ICompilerInfoProvider {
  string DefaultSyntaxHighlightingFile { get; }
  ISectionStyleProvider SectionStyleProvider { get; }
  List<QueryDefinition> BuiltinQueries { get; }
  List<FunctionTaskDefinition> BuiltinFunctionTasks { get; }
  List<FunctionTaskDefinition> ScriptFunctionTasks { get; }
  string OpenFileFilter { get; }
  string OpenDebugFileFilter { get; }

  IBlockFoldingStrategy CreateFoldingStrategy(FunctionIR function);
  IDiffInputFilter CreateDiffInputFilter();
  IDiffOutputFilter CreateDiffOutputFilter();
}