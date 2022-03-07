// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using IRExplorerCore;
using IRExplorerCore.IR;
using System.Threading.Tasks;
using System;

namespace IRExplorerUI.Query {
    public class BuiltinFunctionTask : IFunctionTask {
        public delegate bool TaskCallback(FunctionIR function, IRDocument document,
                                          IFunctionTaskOptions options, ISession session,
                                          CancelableTask cancelableTask);
        private TaskCallback callback_;

        public ISession Session { get; private set; }
        public IFunctionTaskOptions Options { get; private set; }
        public FunctionTaskInfo TaskInfo { get; private set; }

        public bool Result { get; set; }
        public string ResultMessage { get; set; }
        public string OutputText { get; set; }

        public static FunctionTaskDefinition GetDefinition(FunctionTaskInfo taskInfo,
                                                           TaskCallback callback) {
            return new FunctionTaskDefinition(typeof(BuiltinFunctionTask), taskInfo, callback);
        }

        public Task<bool> Execute(FunctionIR function, IRDocument document,
                                  CancelableTask cancelableTask) {
            return Task.Run(() => callback_(function, document, Options,
                                            Session, cancelableTask));
        }

        public bool Initialize(ISession session, FunctionTaskInfo taskInfo, object optionalData) {
            Session = session;
            TaskInfo = taskInfo;
            callback_ = (TaskCallback)optionalData;

            LoadOptions();
            return true;
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
    }
}
