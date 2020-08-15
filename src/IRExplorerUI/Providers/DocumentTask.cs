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

namespace IRExplorerUI {
    public interface IDocumentTask {
        //? TODO: A way to pass options, maybe even show an options panel
        public ISessionManager Session { get; }
        public bool Initialize(ISessionManager session, object optionalData);
        Task<bool> Execute(FunctionIR function, IRDocument document, CancelableTask cancelableTask);
    }


    public class BuiltinDocumentTask : IDocumentTask {
        public delegate bool TaskCallback(FunctionIR function, IRDocument document,
                                          ISessionManager session, CancelableTask cancelableTask);
        private TaskCallback callback_;

        public static DocumentTaskDefinition GetDefinition(string name, string description,
                                                             TaskCallback callback) {
            return new DocumentTaskDefinition(typeof(BuiltinDocumentTask), name, description, callback);
        }

        public ISessionManager Session { get; private set; }

        public Task<bool> Execute(FunctionIR function, IRDocument document, CancelableTask cancelableTask) {
            return Task.Run(() => callback_(function, document, Session, cancelableTask));
        }

        public bool Initialize(ISessionManager session, object optionalData) {
            Session = session;
            callback_ = (TaskCallback)optionalData;
            return true;
        }
    }

    class TaskOptions {
        [DisplayName("First")]
        [Description("Description 1")]
        public bool One { get; set; }

        [DisplayName("Second")]
        [Description("Description 2")]
        public int Two { get; set; }

        [DisplayName("Second")]
        [Description("Description 2")]
        public Color A1 { get; set; }
        [DisplayName("Second")]
        [Description("Description 2")]
        public Color A2 { get; set; }
        [DisplayName("Second")]
        [Description("Description 2")]
        public Color A3 { get; set; }

        public TaskOptions() {

        }
    }

    public class DocumentTaskDefinition {
        private Type taskType_;
        private object optionalData_;

        public DocumentTaskDefinition(Type taskType, object optionalData = null) {
            taskType_ = taskType;
            optionalData_ = optionalData;
        }

        public DocumentTaskDefinition(Type taskType, string name, string description,
                                      object optionalData = null) : this(taskType, optionalData) {
            Name = name;
            Description = description;
        }

        public string Name { get; set; }
        public string Description { get; set; }
        public bool HasOptionsPanel { get; set; }

        public IDocumentTask CreateInstance(ISessionManager session) {
            var actionInstance = (IDocumentTask)Activator.CreateInstance(taskType_);

            if (!actionInstance.Initialize(session, optionalData_)) {
                return null;
            }

            return actionInstance;
        }

        public QueryData CreateOptionsPanel<T>(T inputObject) where T : class, new() {
            var data = new QueryData();
            data.AddInputs(inputObject);
            return data;
        }

        public T ExtractOptions<T>(QueryData data) where T : class, new() {
            return data.ExtractInputs<T>();
        }
    }
}
