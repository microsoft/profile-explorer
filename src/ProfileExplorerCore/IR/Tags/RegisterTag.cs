// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;

namespace ProfileExplorer.Core.IR.Tags;

public sealed class RegisterTag : ITag {
  public RegisterTag(RegisterIR register, TaggedObject owner) {
    Register = register;
    Owner = owner;
  }

  public RegisterIR Register { get; set; }
  public string Name => "Register";
  public TaggedObject Owner { get; set; }

  public override bool Equals(object obj) {
    return obj is RegisterTag tag &&
           EqualityComparer<RegisterIR>.Default.Equals(Register, tag.Register);
  }

  public override int GetHashCode() {
    return HashCode.Combine(Register);
  }

  public override string ToString() {
    return $"Register: {Register}";
  }
}