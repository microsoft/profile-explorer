// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CSScriptLib;

namespace IRExplorerUI.Scripting;

public class Script {
  private static readonly string WarmUpScript =
    string.Join(Environment.NewLine,
                "using System;",
                "using System.Collections.Generic;",
                "using System.Windows.Media;",
                "using IRExplorerCore;", "using IRExplorerCore.IR;",
                "using IRExplorerCore.Analysis;",
                "using IRExplorerUI;",
                "using IRExplorerUI.Scripting;",
                "public class Script {",
                "    public bool Execute(ScriptSession s) {",
                "        return true;",
                "    }",
                "}");
  private static object lockObject_;
  private static long initialized_;
  private dynamic script_;

  static Script() {
    initialized_ = 0;
    lockObject_ = new object();
  }

  public Script(string code, string name = "") {
    Name = name;
    Code = code;
  }

  public string Name { get; set; }
  public string Code { get; set; }
  public bool ScriptResult { get; set; }
  public Exception ScriptException { get; set; }

  public static Script LoadFromFile(string filePath, string name = "") {
    try {
      return new Script(File.ReadAllText(filePath), name);
    }
    catch (Exception ex) {
      Trace.TraceError($"Failed to load script from file {filePath}: {ex.Message}");
      return null;
    }
  }

  public static bool WarmUp() {
    // Initializing the Roslyn script engine can take 2-3 sec, a one-time cost,
    // this allows it to be done on a background thread before any script runs.
    if (Interlocked.Read(ref initialized_) != 0) {
      return true;
    }

    lock (lockObject_) {
      if (Interlocked.Read(ref initialized_) != 0) {
        return true;
      }

      var script = new Script(WarmUpScript);
      bool result = script.Execute(null, true);
      Interlocked.Exchange(ref initialized_, 1);
      return result;
    }
  }

  public dynamic LoadScript() {
    if (script_ == null) {
      // Load and compile the script only once.
      try {
        CSScript.EvaluatorConfig.Engine = EvaluatorEngine.Roslyn;
        script_ = CSScript.Evaluator.LoadCode(Code);
      }
      catch (Exception ex) {
        Trace.TraceError($"Failed to compile script: {ex.Message}");
        return null;
      }
    }

    return script_;
  }

  public bool Execute(ScriptSession session) {
    return Execute(session, false);
  }

  public Task<bool> ExecuteAsync(ScriptSession session) {
    return Task.Run(() => Execute(session));
  }

  public async Task<bool> ExecuteAsync(ScriptSession session, TimeSpan timeout) {
    var task = ExecuteAsync(session);

    if (await Task.WhenAny(task, Task.Delay(timeout)).ConfigureAwait(false) == task) {
      return await task.ConfigureAwait(false);
    }

    session.Cancel();
    ScriptException = new TimeoutException("Script timed out");
    return false;
  }

  private bool Execute(ScriptSession session, bool fromWarmUp) {
    if (!fromWarmUp && !WarmUp()) {
      return false;
    }

    try {
      LoadScript();
      ScriptResult = script_.Execute(session);
      return true;
    }
    catch (Exception ex) {
      ScriptException = ex;
      return false;
    }
  }
}
