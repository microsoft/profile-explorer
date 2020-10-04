﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using IRExplorerCore;
using IRExplorerCore.IR;
using System.Threading.Tasks;
using System;
using IRExplorerUI.Scripting;
using System.Diagnostics;
using System.Collections.Generic;

namespace IRExplorerUI.Query {
    public class ScriptFunctionTask : IFunctionTask {
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

                    var instance = script.LoadScript();
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

        private string scriptCode_;
        private ScriptSession scriptSession_;

        public ISession Session { get; private set; }
        public IFunctionTaskOptions Options { get; private set; }
        public FunctionTaskInfo TaskInfo { get; private set; }

        public string OutputText => scriptSession_?.OutputText;

        public Exception ScriptException { get; private set; }
        public bool Result { get; private set; }
        public string ResultMessage { get; private set; }

        public static FunctionTaskDefinition GetDefinition(string scriptCode) {
            try {
                var scriptInstance = ScriptCache.CreateScriptInstance(scriptCode);

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
            //? To serialize, use JSON?
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
