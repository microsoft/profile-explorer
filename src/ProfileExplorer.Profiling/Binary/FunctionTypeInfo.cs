// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Collections.Generic;

namespace ProfileExplorer.Core.Binary;

/// <summary>
/// A single function parameter as recovered from PDB/DIA: its declared name (when not stripped by
/// optimization) and its rendered type name. Either may be null/empty when DIA doesn't have the
/// information -- never fabricated.
/// </summary>
public sealed record ParameterTypeInfo(string? Name, string? TypeName);

/// <summary>
/// A function's signature as recovered from PDB/DIA type information: return type, calling
/// convention, and parameters in declaration order. This is exact PDB-sourced evidence (not a
/// heuristic reconstruction) -- when a PDB is unavailable, no <see cref="FunctionTypeInfo"/> can be
/// produced at all, which is the correct, honest outcome rather than guessing a signature.
/// </summary>
public sealed record FunctionTypeInfo(string? ReturnTypeName, string? CallingConvention,
                                      IReadOnlyList<ParameterTypeInfo> Parameters);
