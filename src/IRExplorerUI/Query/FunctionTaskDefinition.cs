// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;

namespace IRExplorerUI.Query {
    public class FunctionTaskInfo {
        public FunctionTaskInfo(string name, string description = "") {
            Name = name;
            Description = description;
            AutoExecute = true;
        }

        public string Name { get; set; }
        public string Description { get; set; }
        public bool AutoExecute { get; set; }
        public bool HasOptionsPanel { get; set; }
        public bool ShowOptionsPanelOnExecute { get; set; }
        public Type OptionsType { get; set; }
    }

    public class FunctionTaskDefinition {
        private FunctionTaskInfo taskInfo_;
        private Type taskType_;
        private object optionalData_;

        public FunctionTaskDefinition(Type taskType, object optionalData = null) {
            taskType_ = taskType;
            optionalData_ = optionalData;
        }

        public FunctionTaskDefinition(Type taskType, FunctionTaskInfo taskInfo,
                                      object optionalData = null) : this(taskType, optionalData) {
            taskInfo_ = taskInfo;
        }

        public FunctionTaskInfo TaskInfo => taskInfo_;

        public IFunctionTask CreateInstance(ISession session) {
            var actionInstance = (IFunctionTask)Activator.CreateInstance(taskType_);

            if (!actionInstance.Initialize(session, taskInfo_, optionalData_)) {
                return null;
            }

            return actionInstance;
        }

        public QueryData CreateOptionsPanel(ISession session) {
            if (!taskInfo_.HasOptionsPanel) {
                throw new InvalidOperationException("Task doesn't have an options panel!");
            }

            return CreateInstance(session).GetOptionsValues();
        }
    }
}
