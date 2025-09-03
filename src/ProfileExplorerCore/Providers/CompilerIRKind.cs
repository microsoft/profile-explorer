// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace ProfileExplorer.Core.Providers;

/// <summary>
/// Defines the different compiler IR types supported by Profile Explorer.
/// </summary>
public enum CompilerIRKind {
  /// <summary>
  /// Assembly/machine code IR
  /// </summary>
  ASM,
  
  /// <summary>
  /// LLVM Intermediate Representation
  /// </summary>
  LLVM
}
