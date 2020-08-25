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
        IFunctionTaskOptions Options { get;}
        ISessionManager Session { get; }

        void ResetOptions();
        void SaveOptions();
        QueryData CreateOptionsPanel();
        void LoadPanelOptions(QueryData data);

        bool Initialize(ISessionManager session, object optionalData);
        Task<bool> Execute(FunctionIR function, IRDocument document, CancelableTask cancelableTask);
    }

    public class BuiltinFunctionTask : IFunctionTask {
        public delegate bool TaskCallback(FunctionIR function, IRDocument document,
                                          IFunctionTaskOptions options, ISessionManager session, 
                                          CancelableTask cancelableTask);
        private TaskCallback callback_;
        private Type optionsType_;

        public ISessionManager Session { get; private set; }
        public IFunctionTaskOptions Options { get; private set; }
        
        public static FunctionTaskDefinition GetDefinition(string name, string description,
                                                           TaskCallback callback, Type optionsType = null) {
            return new FunctionTaskDefinition(typeof(BuiltinFunctionTask), name, description,
                                              new Tuple<TaskCallback, Type>(callback, optionsType));
        }

        public Task<bool> Execute(FunctionIR function, IRDocument document,
                                  CancelableTask cancelableTask) {
            return Task.Run(() => callback_(function, document, Options,
                                            Session, cancelableTask));
        }

        public bool Initialize(ISessionManager session, object optionalData) {
            Session = session;
            var data = (Tuple<TaskCallback, Type>)optionalData;
            callback_ = data.Item1;
            optionsType_ = data.Item2;

            //? load options from session
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
            Options = (IFunctionTaskOptions)Activator.CreateInstance(optionsType_);
            Options.Reset();
        }

        public QueryData CreateOptionsPanel() {
            var data = new QueryData();
            data.AddInputs(Options);
            return data;
        }

        public void LoadPanelOptions(QueryData data) {
            Options = (IFunctionTaskOptions)data.ExtractInputs(optionsType_);
        }
    }

    public class FunctionTaskDefinition {
        private Type taskType_;
        private object optionalData_;

        public FunctionTaskDefinition(Type taskType, object optionalData = null) {
            taskType_ = taskType;
            optionalData_ = optionalData;
        }

        public FunctionTaskDefinition(Type taskType, string name, string description,
                                      object optionalData = null) : this(taskType, optionalData) {
            Name = name;
            Description = description;
        }

        public FunctionTaskDefinition WithOptions(bool hasOptionsPanel, bool showOptionsPanelOnExecute) {
            HasOptionsPanel = hasOptionsPanel;
            ShowOptionsPanelOnExecute = showOptionsPanelOnExecute;
            return this;
        }

        public string Name { get; set; }
        public string Description { get; set; }
        public bool HasOptionsPanel { get; set; }
        public bool ShowOptionsPanelOnExecute { get; set; }

        public IFunctionTask CreateInstance(ISessionManager session) {
            var actionInstance = (IFunctionTask)Activator.CreateInstance(taskType_);

            if (!actionInstance.Initialize(session, optionalData_)) {
                return null;
            }

            return actionInstance;
        }

        public QueryData CreateOptionsPanel(ISessionManager session) {
            if (!HasOptionsPanel) {
                throw new InvalidOperationException("Task doesn't have an options panel!");
            }

            return CreateInstance(session).CreateOptionsPanel();
        }
    }
}
