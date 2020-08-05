using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CSScriptLib;

namespace IRExplorer.Scripting {
    public class Script {
        private static ManualResetEvent warmUpCompletedEvent_;

        static Script() {
            warmUpCompletedEvent_ = new ManualResetEvent(true);
        }

        public string Name { get; set; }
        public string Code { get; set; }
        public bool ScriptResult { get; set; }
        public Exception ScriptException { get; set; }

        public Script(string code, string name = "") {
            Name = name;
            Code = code;
        }

        public static Script LoadFromFile(string filePath, string name = "") {
            try {
                return new Script(File.ReadAllText(filePath), name);
            }
            catch(Exception ex) {
                Trace.TraceError($"Failed to load script from file {filePath}: {ex.Message}");
                return null;;
            }
        }

        public static void WarmUp() {
            warmUpCompletedEvent_.Reset();

            //? TODO: Run dummy script to initialize engine
            //? Needs to  use the Core.* and Client.* namespaces to load all dependencies
            // Task.Run(() => );
            warmUpCompletedEvent_.Set();
        }

        public virtual bool Execute(ScriptSession session) {
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

        public Task<bool> ExecuteAsync(ScriptSession session) {
            warmUpCompletedEvent_.WaitOne();
            return Task.Run(() => Execute(session));
        }

        public async Task<bool> ExecuteAsync(ScriptSession session, TimeSpan timeout) {
            var task = ExecuteAsync(session);

            if (await Task.WhenAny(task, Task.Delay(timeout)) == task) {
                return await task;
            }

            //? TODO: Kill running script somehow
            session.Cancel();
            ScriptException = new TimeoutException("Script timed out");
            return false;
        }
    }
}
