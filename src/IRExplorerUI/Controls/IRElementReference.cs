// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using IRExplorerCore.IR;
using ProtoBuf;

namespace IRExplorerUI;

[ProtoContract(SkipConstructor = true)]
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