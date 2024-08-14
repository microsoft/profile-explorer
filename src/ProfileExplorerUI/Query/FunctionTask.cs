// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Threading.Tasks;
using ProfileExplorer.Core;
using ProfileExplorer.Core.IR;

namespace ProfileExplorer.UI.Query;

public interface IFunctionTaskOptions {
  public void Reset();
}

public interface IFunctionTask {
  IFunctionTaskOptions Options { get; }
  ISession Session { get; }
  FunctionTaskInfo TaskInfo { get; }
  bool Result { get; }
  string ResultMessage { get; }
  string OutputText { get; }
  void ResetOptions();
  void SaveOptions();
  QueryData GetOptionsValues();
  void LoadOptionsFromValues(QueryData data);
  bool Initialize(ISession session, FunctionTaskInfo taskInfo, object optionalData);
  Task<bool> Execute(FunctionIR function, IRDocument document, CancelableTask cancelableTask);
}