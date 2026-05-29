// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using ProtoBuf;

namespace ProfileExplorer.Profiling.Symbols;

/// <summary>
/// Interface for debug information providers (PDB readers).
/// </summary>
public interface IDebugInfoProvider : IDisposable {
  /// <summary>Load debug information from a PDB file.</summary>
  bool LoadDebugInfo(string debugFilePath);

  /// <summary>Unload debug information and release resources.</summary>
  void Unload();

  /// <summary>Enumerate all functions defined in the PDB.</summary>
  IEnumerable<FunctionDebugInfo> EnumerateFunctions();

  /// <summary>Get a sorted list of all functions (sorted by RVA).</summary>
  List<FunctionDebugInfo> GetSortedFunctions();

  /// <summary>Find a function by name.</summary>
  FunctionDebugInfo? FindFunction(string functionName);

  /// <summary>Find the function containing the given RVA.</summary>
  FunctionDebugInfo? FindFunctionByRVA(long rva);

  /// <summary>Populate source line mappings for a function.</summary>
  bool PopulateSourceLines(FunctionDebugInfo funcInfo);

  /// <summary>Find the source file path for a function by name.</summary>
  SourceFileDebugInfo FindFunctionSourceFilePath(string functionName);

  /// <summary>Find the source file path for a function by RVA.</summary>
  SourceFileDebugInfo FindSourceFilePathByRVA(long rva);

  /// <summary>Find the source line for a specific RVA, optionally including inlinee info.</summary>
  SourceLineDebugInfo FindSourceLineByRVA(long rva, bool includeInlinees = false);
}

/// <summary>
/// Identifies a PDB file on a symbol server (GUID + Age + FileName).
/// </summary>
[ProtoContract(SkipConstructor = true)]
public class SymbolFileDescriptor : IEquatable<SymbolFileDescriptor> {
  public SymbolFileDescriptor(string fileName, Guid id, int age) {
    FileName = fileName;
    Id = id;
    Age = age;
  }

  public SymbolFileDescriptor(string fileName) {
    FileName = fileName;
  }

  [ProtoMember(1)] public string FileName { get; set; }
  [ProtoMember(2)] public Guid Id { get; set; }
  [ProtoMember(3)] public int Age { get; set; }

  public string SymbolName => Path.GetFileName(FileName);

  public bool Equals(SymbolFileDescriptor? other) {
    if (other is null) return false;
    return string.Equals(FileName, other.FileName, StringComparison.OrdinalIgnoreCase) &&
           Id == other.Id &&
           Age == other.Age;
  }

  public override bool Equals(object? obj) => obj is SymbolFileDescriptor other && Equals(other);
  public override int GetHashCode() => HashCode.Combine(FileName?.GetHashCode(StringComparison.OrdinalIgnoreCase), Id, Age);
  public override string ToString() => $"{Id}:{FileName}";

  public static bool operator ==(SymbolFileDescriptor? left, SymbolFileDescriptor? right) => Equals(left, right);
  public static bool operator !=(SymbolFileDescriptor? left, SymbolFileDescriptor? right) => !Equals(left, right);
}
