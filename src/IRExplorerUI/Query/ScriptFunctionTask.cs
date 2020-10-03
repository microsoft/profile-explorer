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

        public ISession Session { get; private set; }
        public IFunctionTaskOptions Options { get; private set; }
        public FunctionTaskInfo TaskInfo { get; private set; }

        public static FunctionTaskDefinition GetDefinition(string scriptCode) {
            try {
                var script = new Script(scriptCode);
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
            var scriptSession = new ScriptSession(document, Session);
            var script = new Script(scriptCode_);
            var result = await script.ExecuteAsync(scriptSession);

            foreach (var pair in scriptSession.MarkedElements) {
                document.MarkElement(pair.Item1, pair.Item2);
            }

            return result;
            //return Task.Run(() => callback_(function, document, Options,
            //                                Session, cancelableTask));
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
