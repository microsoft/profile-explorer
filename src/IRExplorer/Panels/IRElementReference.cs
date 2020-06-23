// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using CoreLib.IR;
using ProtoBuf;

namespace Client {
    [ProtoContract]
    public class IRElementReference {
        [ProtoMember(1)]
        public ulong Id;
        public IRElement Value;

        public IRElementReference() {
            Id = 0;
        }

        public IRElementReference(IRElement element) {
            Id = element.Id;
            Value = element;
        }

        public IRElementReference(ulong id, IRElement element = null) {
            Id = id;
            Value = element;
        }

        public IRElementReference(IRElementId id, IRElement element = null) {
            Id = id.ToLong();
            Value = element;
        }

        public static implicit operator IRElementReference(IRElement element) {
            return new IRElementReference(element.Id, element);
        }

        public static implicit operator IRElement(IRElementReference elementRef) {
            return elementRef.Value;
        }
    }
}
