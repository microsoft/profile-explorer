// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using IRExplorerUI.Diff;
using IRExplorerCore;
using IRExplorerCore.IR;
using System.Collections.Generic;
using IRExplorerUI.Query;
using System.Threading.Tasks;
using System.Reflection;
using System.ComponentModel;
using System;

namespace IRExplorerUI.Query {
    public interface IFunctionTaskOptions {
        public void Reset();
    }

    public interface IFunctionTask {
        IFunctionTaskOptions Options { get; }
        ISession Session { get; }
        FunctionTaskInfo TaskInfo { get; }

        bool Result { get; }
        string ResultMessage { get; }
        string OutputText { get; }

        void ResetOptions();
        void SaveOptions();
        QueryData GetOptionsValues();
        void LoadOptionsFromValues(QueryData data);

        bool Initialize(ISession session, FunctionTaskInfo taskInfo, object optionalData);
        Task<bool> Execute(FunctionIR function, IRDocument document, CancelableTask cancelableTask);
    }

    public class FunctionTaskOptions {
        public ISession Session { get; protected set; }
        public IFunctionTaskOptions Options { get; protected set; }
        public FunctionTaskInfo TaskInfo { get; protected set; }

        public QueryData GetOptionsValues() {
            var data = new QueryData();
            data.AddInputs(Options);
            return data;
        }

        public void LoadOptionsFromValues(QueryData data) {
            Options = (IFunctionTaskOptions)data.ExtractInputs(TaskInfo.OptionsType);
        }

        public void ResetOptions() {
            if (TaskInfo.OptionsType == null) {
                return;
            }

            Options = (IFunctionTaskOptions)Activator.CreateInstance(TaskInfo.OptionsType);
            Options.Reset();
        }

        public void SaveOptions() {
            if (Options != null) {
                Session.SaveFunctionTaskOptions(TaskInfo, Options);
            }
        }

        public void LoadOptions() {
            var options = Session.LoadFunctionTaskOptions(TaskInfo);

            if (options != null) {
                Options = options;
            }
            else {
                ResetOptions();
            }
        }
    }
}
