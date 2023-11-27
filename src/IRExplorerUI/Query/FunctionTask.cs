// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System.Threading.Tasks;
using IRExplorerCore;
using IRExplorerCore.IR;

namespace IRExplorerUI.Query;

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
