// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Reflection.PortableExecutable;
using ProfileExplorer.Core;
using ProfileExplorer.Core.IR;
using ProfileExplorer.UI.Compilers;
using ProtoBuf;

namespace ProfileExplorer.UI.Binary;

public interface IDebugInfoProvider : IDisposable {
  public Machine? Architecture { get; }
  public SymbolFileSourceSettings SymbolSettings { get; set; }

  //bool LoadDebugInfo(string debugFilePath, IDebugInfoProvider other = null);
  bool LoadDebugInfo(DebugFileSearchResult debugFile, IDebugInfoProvider other = null);
  void Unload();
  bool AnnotateSourceLocations(FunctionIR function, IRTextFunction textFunc);
  bool AnnotateSourceLocations(FunctionIR function, FunctionDebugInfo funcDebugInfo);
  IEnumerable<FunctionDebugInfo> EnumerateFunctions();
  List<FunctionDebugInfo> GetSortedFunctions();
  FunctionDebugInfo FindFunction(string functionName);
  FunctionDebugInfo FindFunctionByRVA(long rva);
  bool PopulateSourceLines(FunctionDebugInfo funcInfo);
  SourceFileDebugInfo FindFunctionSourceFilePath(IRTextFunction textFunc);
  SourceFileDebugInfo FindFunctionSourceFilePath(string functionName);
  SourceFileDebugInfo FindSourceFilePathByRVA(long rva);
  SourceLineDebugInfo FindSourceLineByRVA(long rva, bool includeInlinees = false);
}

[ProtoContract(SkipConstructor = true)]
public class SymbolFileDescriptor : IEquatable<SymbolFileDescriptor> {
  public SymbolFileDescriptor(string fileName, Guid id, int age) {
    FileName = fileName != null ? string.Intern(fileName) : null;
    Id = id;
    Age = age;
  }

  public SymbolFileDescriptor(string fileName) {
    FileName = fileName != null ? string.Intern(fileName) : null;
  }

  [ProtoMember(1)]
  public string FileName { get; set; }
  [ProtoMember(2)]
  public Guid Id { get; set; }
  [ProtoMember(3)]
  public int Age { get; set; }
  public string SymbolName => Utils.TryGetFileName(FileName);

  public bool Equals(SymbolFileDescriptor other) {
    return FileName.Equals(other.FileName, StringComparison.OrdinalIgnoreCase) &&
           Id == other.Id &&
           Age == other.Age;
  }

  public static bool operator ==(SymbolFileDescriptor left, SymbolFileDescriptor right) {
    return Equals(left, right);
  }

  public static bool operator !=(SymbolFileDescriptor left, SymbolFileDescriptor right) {
    return !Equals(left, right);
  }

  public override string ToString() {
    return $"{Id}:{FileName}";
  }

  public override bool Equals(object obj) {
    if (ReferenceEquals(null, obj)) {
      return false;
    }

    if (ReferenceEquals(this, obj)) {
      return true;
    }

    if (obj.GetType() != GetType()) {
      return false;
    }

    return Equals((SymbolFileDescriptor)obj);
  }

  public override int GetHashCode() {
    return HashCode.Combine(FileName.GetHashCode(StringComparison.OrdinalIgnoreCase), Id, Age);
  }
}