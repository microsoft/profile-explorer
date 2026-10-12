// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
namespace ProfileExplorer.Core.Binary;

/// <summary>
/// One resolved import: a module + function (or ordinal-only) import, plus the exact RVA of its
/// IAT slot -- the address a compiled indirect call/jump (e.g. x64 <c>call qword ptr [rip+N]</c>)
/// actually references at runtime. This is what turns an opaque indirect call target into a
/// meaningful API name for AI-facing pseudocode, without requiring any dynamic tracing.
/// </summary>
public sealed record ImportedFunctionReference(string ModuleName, string? FunctionName, long? Ordinal, long IatRva) {
  /// <summary>Stable display form: "Module!Function" or "Module!#Ordinal" when only an ordinal is known.</summary>
  public string QualifiedName => FunctionName != null ? $"{ModuleName}!{FunctionName}" : $"{ModuleName}!#{Ordinal}";
}

/// <summary>
/// One resolved export from this binary's own export directory. <see cref="ForwarderTarget"/> is
/// set (and <see cref="Rva"/> is meaningless -- 0) when this export forwards to another module's
/// export (e.g. "NTDLL.RtlAllocateHeap") rather than pointing at real code in this image.
/// </summary>
public sealed record ExportedFunctionReference(string? FunctionName, long Rva, long Ordinal, string? ForwarderTarget) {
  public bool IsForwarder => ForwarderTarget != null;
}
