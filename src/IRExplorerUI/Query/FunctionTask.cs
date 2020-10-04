// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using IRExplorerUI.Diff;
using IRExplorerCore;
using IRExplorerCore.IR;
using System.Collections.Generic;
using IRExplorerUI.Query;
using System.Threading.Tasks;
using System;
using System.Reflection;
using System.ComponentModel;
using System.Windows.Media;

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
}
