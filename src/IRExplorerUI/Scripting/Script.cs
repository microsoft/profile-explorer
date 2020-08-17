using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CSScriptLib;

namespace IRExplorerUI.Scripting {
    public class Script {
        private static readonly string WarmUpScript =
            string.Join(Environment.NewLine,
                        "using System;",
                        "using System.Collections.Generic;",
                        "using System.Windows.Media;",
                        "using IRExplorerCore;", "using IRExplorerCore.IR;",
                        "using IRExplorerCore.Analysis;",
                        "using IRExplorerCore.UTC;", "using IRExplorerUI;",
                        "using IRExplorerUI.Scripting;",
                        "\n",
                        "public class Script {",
                        "    // s: provides script interaction with Compiler Studio (text output, marking, etc.)",
                        "    public bool Execute(ScriptSession s) {",
                        "        // Write C#-based script here.",
                        "        return true;",
                        "    }",
                        "}");
        private static object lockObject_;
        private static long initialized_;

        public string Name { get; set; }
        public string Code { get; set; }
        public bool ScriptResult { get; set; }
        public Exception ScriptException { get; set; }

        static Script() {
            initialized_ = 0;
            lockObject_ = new object();
        }

        public Script(string code, string name = "") {
            Name = name;
            Code = code;
        }

        public static Script LoadFromFile(string filePath, string name = "") {
            try {
                return new Script(File.ReadAllText(filePath), name);
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed to load script from file {filePath}: {ex.Message}");
                return null;
                ;
            }
        }

        public static bool WarmUp() {
            if (Interlocked.Read(ref initialized_) != 0) {
                return true;
            }

            lock (lockObject_) {
                if (Interlocked.Read(ref initialized_) != 0) {
                    return true;
                }

                var script = new Script(WarmUpScript);
                bool result = script.Execute(null, fromWarmUp: true);
                Interlocked.Exchange(ref initialized_, 1);
                return result;
            }
        }

        private bool Execute(ScriptSession session, bool fromWarmUp) {
            if (!fromWarmUp && !WarmUp()) {
                return false;
            }

            try {
                CSScript.EvaluatorConfig.Engine = EvaluatorEngine.Roslyn;
                dynamic script = CSScript.Evaluator.LoadCode(Code);
                ScriptResult = script.Execute(session);
                return true;
            }
            catch (Exception ex) {
                ScriptException = ex;
                return false;
            }
        }

        public bool Execute(ScriptSession session) {
            return Execute(session, fromWarmUp: false);
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
    }
}
