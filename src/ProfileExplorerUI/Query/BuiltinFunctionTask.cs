﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Threading.Tasks;
using ProfileExplorer.Core;
using ProfileExplorer.Core.IR;
using ProfileExplorer.Core.Utilities;

namespace ProfileExplorer.UI.Query;

public class BuiltinFunctionTask : IFunctionTask {
  public delegate bool TaskCallback(FunctionIR function, IRDocument document,
                                    IFunctionTaskOptions options, IUISession session,
                                    CancelableTask cancelableTask);

  private TaskCallback callback_;
  public IUISession Session { get; private set; }
  public IFunctionTaskOptions Options { get; private set; }
  public FunctionTaskInfo TaskInfo { get; private set; }
  public bool Result { get; set; }
  public string ResultMessage { get; set; }
  public string OutputText { get; set; }

  public Task<bool> Execute(FunctionIR function, IRDocument document,
                            CancelableTask cancelableTask) {
    return Task.Run(() => callback_(function, document, Options,
                                    Session, cancelableTask));
  }

  public bool Initialize(IUISession session, FunctionTaskInfo taskInfo, object optionalData) {
    Session = session;
    TaskInfo = taskInfo;
    callback_ = (TaskCallback)optionalData;

    LoadOptions();
    return true;
  }

  public void SaveOptions() {
    if (Options != null) {
      Session.SaveFunctionTaskOptions(TaskInfo, Options);
    }
  }

  public void ResetOptions() {
    if (TaskInfo.OptionsType == null) {
      return;
    }

    Options = (IFunctionTaskOptions)Activator.CreateInstance(TaskInfo.OptionsType);
    Options.Reset();
  }

  public QueryData GetOptionsValues() {
    var data = new QueryData();
    data.AddInputs(Options);
    return data;
  }

  public void LoadOptionsFromValues(QueryData data) {
    Options = (IFunctionTaskOptions)data.ExtractInputs(TaskInfo.OptionsType);
  }

  public static FunctionTaskDefinition GetDefinition(FunctionTaskInfo taskInfo,
                                                     TaskCallback callback) {
    return new FunctionTaskDefinition(typeof(BuiltinFunctionTask), taskInfo, callback);
  }

  private void LoadOptions() {
    var options = Session.LoadFunctionTaskOptions(TaskInfo);

    if (options != null) {
      Options = options;
    }
    else {
      ResetOptions();
    }
  }
}