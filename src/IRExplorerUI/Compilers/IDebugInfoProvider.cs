// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using IRExplorerCore;
using IRExplorerCore.IR;
using ProtoBuf;

namespace IRExplorerUI.Compilers;

public interface IDebugInfoProvider : IDisposable {
  public Machine? Architecture { get; }
  public SymbolFileSourceSettings SymbolSettings { get; set; }
  bool LoadDebugInfo(string debugFilePath, IDebugInfoProvider other = null);
  bool LoadDebugInfo(DebugFileSearchResult debugFile, IDebugInfoProvider other = null);
  void Unload();
  bool CanUseInstance();
  bool AnnotateSourceLocations(FunctionIR function, IRTextFunction textFunc);
  bool AnnotateSourceLocations(FunctionIR function, string functionName);
  IEnumerable<FunctionDebugInfo> EnumerateFunctions();
  List<FunctionDebugInfo> GetSortedFunctions();
  FunctionDebugInfo FindFunction(string functionName);
  FunctionDebugInfo FindFunctionByRVA(long rva);
  bool PopulateSourceLines(FunctionDebugInfo funcInfo);
  SourceFileDebugInfo FindFunctionSourceFilePath(IRTextFunction textFunc);
  SourceFileDebugInfo FindFunctionSourceFilePath(string functionName);
  SourceFileDebugInfo FindSourceFilePathByRVA(long rva);
  SourceLineDebugInfo FindSourceLineByRVA(long rva);
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

  public bool Equals(SymbolFileDescriptor other) {
    return FileName.Equals(other.FileName, StringComparison.OrdinalIgnoreCase) &&
           Id == other.Id &&
           Age == other.Age;
  }
}