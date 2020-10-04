// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using IRExplorerCore;
using IRExplorerCore.IR;
using System.Threading.Tasks;
using System;
using IRExplorerUI.Scripting;
using System.Diagnostics;

namespace IRExplorerUI.Query {
    public class ScriptFunctionTask : IFunctionTask {
        private string scriptCode_;
        private ScriptSession scriptSession_;

        public ISession Session { get; private set; }
        public IFunctionTaskOptions Options { get; private set; }
        public FunctionTaskInfo TaskInfo { get; private set; }

        public string OutputText => scriptSession_?.OutputText;

        public Exception ScriptException { get; private set; }
        public bool Result { get; private set; }
        public string ResultMessage { get; private set; }

        static Script instance_;

        public static FunctionTaskDefinition GetDefinition(string scriptCode) {
            try {
                var script = new Script(scriptCode);
                instance_ = script;
                var scriptInstance = script.LoadScript();

                FunctionTaskInfo taskInfo = scriptInstance.GetTaskInfo();
                return new FunctionTaskDefinition(typeof(ScriptFunctionTask), taskInfo, scriptCode);
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed to load script function task: {ex.Message}");
                return null;
            }
        }

        public async Task<bool> Execute(FunctionIR function, IRDocument document,
                                  CancelableTask cancelableTask) {
            scriptSession_ = new ScriptSession(document, Session) {
                SessionObject = Options
            };
            //var script = new Script(scriptCode_);
            var script = instance_;
            var scriptResult = await script.ExecuteAsync(scriptSession_);

            foreach (var pair in scriptSession_.MarkedElements) {
                document.MarkElement(pair.Item1, pair.Item2);
            }

            ScriptException = script.ScriptException;
            Result = scriptSession_.SessionResult;
            ResultMessage = scriptSession_.SessionResultMessage;
            return scriptResult;
        }

        public bool Initialize(ISession session, FunctionTaskInfo taskInfo, object optionalData) {
            Session = session;
            TaskInfo = taskInfo;
            scriptCode_ = (string)optionalData;

            //? TODO: Load options from session, or from App.Settings
            LoadOptions();
            return true;
        }

        private void LoadOptions() {
            ResetOptions();
        }

        public void SaveOptions() {
            var data = StateSerializer.Serialize(Options);
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
    }
}
