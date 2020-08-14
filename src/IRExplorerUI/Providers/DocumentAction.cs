// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using IRExplorerUI.Diff;
using IRExplorerCore;
using IRExplorerCore.IR;
using System.Collections.Generic;
using IRExplorerUI.Query;
using System.Threading.Tasks;
using System;

namespace IRExplorerUI {
    public interface IDocumentAction {
        //? TODO: A way to pass options, maybe even show an options panel
        public ISessionManager Session { get; }
        public bool Initialize(ISessionManager session, object optionalData);
        Task<bool> Execute(FunctionIR function, IRDocument document);
    }


    public class BuiltinDocumentAction : IDocumentAction {
        public delegate bool ActionCallback(FunctionIR function, IRDocument document,
                                            ISessionManager session, CancelableTask cancelableTask);
        private ActionCallback callback_;

        public static DocumentActionDefinition GetDefinition(string name, string description,
                                                             ActionCallback callback) {
            return new DocumentActionDefinition(typeof(BuiltinDocumentAction), name, description, callback);
        }

        public ISessionManager Session { get; private set; }

        public Task<bool> Execute(FunctionIR function, IRDocument document) {
            var task = new CancelableTask();
            return Task.Run(() => callback_(function, document, Session, task));
        }

        public bool Initialize(ISessionManager session, object optionalData) {
            Session = session;
            callback_ = (ActionCallback)optionalData;
            return true;
        }
    }


    public class DocumentActionDefinition {
        private Type actionType_;
        private object optionalData_;

        public DocumentActionDefinition(Type actionType, object optionalData = null) {
            actionType_ = actionType;
            optionalData_ = optionalData;
        }

        public DocumentActionDefinition(Type actionType, string name, string description,
                                        object optionalData = null) : this(actionType, optionalData) {
            Name = name;
            Description = description;
        }

        public string Name { get; set; }
        public string Description { get; set; }

        public IDocumentAction CreateInstance(ISessionManager session) {
            var actionInstance = (IDocumentAction)Activator.CreateInstance(actionType_);

            if (!actionInstance.Initialize(session, optionalData_)) {
                return null;
            }

            return actionInstance;
        }
    }
}
