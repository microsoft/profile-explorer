// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using ProfileExplorerCore;
using ProfileExplorerCore.IR;
using ProfileExplorer.UI.Scripting;
using ProfileExplorerCore.Utilities;

namespace ProfileExplorer.UI.Query;

public class ScriptFunctionTask : IFunctionTask {
  private string scriptCode_;
  private ScriptSession scriptSession_;
  public Exception ScriptException { get; private set; }
  public IUISession Session { get; private set; }
  public IFunctionTaskOptions Options { get; private set; }
  public FunctionTaskInfo TaskInfo { get; private set; }
  public string OutputText => scriptSession_?.OutputText;
  public bool Result { get; private set; }
  public string ResultMessage { get; private set; }

  public async Task<bool> Execute(FunctionIR function, IRDocument document,
                                  CancelableTask cancelableTask) {
    var script = ScriptCache.CreateScript(scriptCode_);

    if (script == null) {
      return false; // Failed to load script.
    }

    // Options are passed through SessionObject, function through CurrentFunction,
    // taken by the session from the document.
    scriptSession_ = new ScriptSession(document, Session) {
      SessionObject = Options
    };

    bool scriptResult = await script.ExecuteAsync(scriptSession_);

    foreach (var pair in scriptSession_.MarkedElements) {
      document.MarkElement(pair.Item1, pair.Item2);
    }

    foreach (var pair in scriptSession_.IconElementOverlays) {
      var info = pair.Item2;
      document.AddIconElementOverlay(pair.Item1, info.Icon, 16, 16,
                                     info.Label, info.ToolTip, info.AlignmentX,
                                     VerticalAlignment.Center, info.MarginX);
    }

    ScriptException = script.ScriptException;
    Result = scriptSession_.SessionResult;
    ResultMessage = scriptSession_.SessionResultMessage;
    return scriptResult;
  }

  public bool Initialize(IUISession session, FunctionTaskInfo taskInfo, object optionalData) {
    Session = session;
    TaskInfo = taskInfo;
    scriptCode_ = (string)optionalData;
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

  public static FunctionTaskDefinition GetDefinition(string scriptCode) {
    try {
      dynamic scriptInstance = ScriptCache.CreateScriptInstance(scriptCode);

      if (scriptInstance == null) {
        return null; // Failed to load script.
      }

      FunctionTaskInfo taskInfo = scriptInstance.GetTaskInfo();
      return new FunctionTaskDefinition(typeof(ScriptFunctionTask), taskInfo, scriptCode);
    }
    catch (Exception ex) {
      Trace.TraceError($"Failed to load script function task: {ex.Message}");
      return null;
    }
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

  // The script cache is used mostly to ensure there is a single instance
  // of the script object being created and the options type provided in GetDefinition
  // (FunctionTaskInfo.OptionsType) will be compatible with the type when the script executes. 
  // Compiling the script twice would create two randomly-named binaries and the options type
  // would be incompatible when it's exactly the same.
  private static class ScriptCache {
    private static Dictionary<string, Script> cache_;
    private static Dictionary<string, dynamic> instanceCache_;
    private static object lockObject_;

    static ScriptCache() {
      cache_ = new Dictionary<string, Script>();
      instanceCache_ = new Dictionary<string, dynamic>();
      lockObject_ = new object();
    }

    public static Script CreateScript(string scriptCode) {
      lock (lockObject_) {
        if (cache_.TryGetValue(scriptCode, out var script)) {
          return script;
        }

        script = new Script(scriptCode);
        cache_[scriptCode] = script;

        dynamic instance = script.LoadScript();
        instanceCache_[scriptCode] = instance;
        return instance != null ? script : null;
      }
    }

    public static dynamic CreateScriptInstance(string scriptCode) {
      var script = CreateScript(scriptCode);

      if (script != null) {
        lock (lockObject_) {
          return instanceCache_[scriptCode];
        }
      }

      return null;
    }
  }
}