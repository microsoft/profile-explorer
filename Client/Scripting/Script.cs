using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Client.Scripting {
    public class Script {
        static ManualResetEvent warmUpCompletedEvent_;

        static Script() {
            warmUpCompletedEvent_ = new ManualResetEvent(true);
        }

        public static void WarmUp() {
            warmUpCompletedEvent_.Reset();
            //? TODO: Run dummy script to initialize engine
            //? Needs to  use the Core.* and Client.* namespaces to load all dependencies
            // Task.Run(() => );
            warmUpCompletedEvent_.Set();
        }

        public string Name { get; set; }
        public string Code { get; set; }
        public Exception ScriptException { get; set; }

        public virtual bool Execute(ScriptSession session) {
            try {
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

            if(await Task.WhenAny(task, Task.Delay(timeout)) == task) {
                return await task;
            }

            //? TODO: Kill running script somehow
            session.Cancel();
            ScriptException = new TimeoutException("Script timed out");
            return false;
        }
    }
}
