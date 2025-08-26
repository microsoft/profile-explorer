// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection.PortableExecutable;
using Microsoft.Diagnostics.Symbols;
using ProfileExplorerCore.IR;
using ProfileExplorerCore.IR.Tags;
using ProfileExplorerCore.Providers;
using ProfileExplorerCore.Settings;
using ProfileExplorerCore.Utilities;

namespace ProfileExplorerCore.Binary;

//? Provider ASM should return instance instead of JSONDebug
public class DotNetDebugInfoProvider : IDebugInfoProvider {
  private Dictionary<string, FunctionDebugInfo> functionMap_;
  private List<FunctionDebugInfo> functions_;
  private Machine architecture_;
  private Dictionary<FunctionDebugInfo, List<(int ILOffset, int NativeOffset)>> methodILNativeMap_;
  private Dictionary<long, MethodCode> methodCodeMap_;
  private bool hasManagedSymbolFileFailure_;

  public DotNetDebugInfoProvider(Machine architecture) {
    architecture_ = architecture;
    functionMap_ = new Dictionary<string, FunctionDebugInfo>();
    functions_ = new List<FunctionDebugInfo>();
    methodILNativeMap_ = new Dictionary<FunctionDebugInfo, List<(int ILOffset, int NativeOffset)>>();
  }

  public SymbolFileDescriptor ManagedSymbolFile { get; set; }
  public string ManagedAsmFilePath { get; set; }
  public Machine? Architecture => architecture_;
  public SymbolFileSourceSettings SymbolSettings { get; set; }

  public bool AnnotateSourceLocations(FunctionIR function, IRTextFunction textFunc) {
    return AnnotateSourceLocations(function, textFunc.Name);
  }

  public bool AnnotateSourceLocations(FunctionIR function,
                                      FunctionDebugInfo funcInfo) {
    var metadataTag = function.GetTag<AssemblyMetadataTag>();

    if (metadataTag == null) {
      return false;
    }

    if (!EnsureHasSourceLines(funcInfo)) {
      return false;
    }

    foreach (var pair in metadataTag.OffsetToElementMap) {
      var lineInfo = funcInfo.FindNearestLine(pair.Key);

      if (!lineInfo.IsUnknown) {
        var locationTag = pair.Value.GetOrAddTag<SourceLocationTag>();
        locationTag.Reset(); // Tag may be already populated.
        locationTag.Line = lineInfo.Line;
        locationTag.Column = lineInfo.Column;
      }
    }

    return true;
  }

  public FunctionDebugInfo FindFunction(string functionName) {
    return functionMap_.GetValueOr(functionName, FunctionDebugInfo.Unknown);
  }

  public IEnumerable<FunctionDebugInfo> EnumerateFunctions() {
    return functions_;
  }

  public List<FunctionDebugInfo> GetSortedFunctions() {
    return functions_;
  }

  public FunctionDebugInfo FindFunctionByRVA(long rva) {
    return FunctionDebugInfo.BinarySearch(functions_, rva);
  }

  public SourceFileDebugInfo FindFunctionSourceFilePath(IRTextFunction textFunc) {
    return FindFunctionSourceFilePath(textFunc.Name);
  }

  public SourceFileDebugInfo FindFunctionSourceFilePath(string functionName) {
    if (functionMap_.TryGetValue(functionName, out var funcInfo)) {
      return GetSourceFileInfo(funcInfo);
    }

    return SourceFileDebugInfo.Unknown;
  }

  public SourceFileDebugInfo FindSourceFilePathByRVA(long rva) {
    var funcInfo = FindFunctionByRVA(rva);

    if (EnsureHasSourceLines(funcInfo)) {
      return GetSourceFileInfo(funcInfo);
    }

    return SourceFileDebugInfo.Unknown;
  }

  public SourceLineDebugInfo FindSourceLineByRVA(long rva, bool includeInlinees) {
    var funcInfo = FindFunctionByRVA(rva);

    if (EnsureHasSourceLines(funcInfo)) {
      long offset = rva - funcInfo.StartRVA;
      return funcInfo.FindNearestLine(offset);
    }

    return SourceLineDebugInfo.Unknown;
  }

  public void Unload() {
  }

  public bool LoadDebugInfo(DebugFileSearchResult debugFile, IDebugInfoProvider other = null) {
    return true;
  }

  public void Dispose() {
  }

  public bool PopulateSourceLines(FunctionDebugInfo funcInfo) {
    return true;
  }

  public void UpdateArchitecture(Machine architecture) {
    if (architecture_ == Machine.Unknown) {
      architecture_ = architecture;
    }
  }

  public MethodCode FindMethodCode(FunctionDebugInfo funcInfo) {
    return methodCodeMap_?.GetValueOrNull(funcInfo.RVA);
  }

  public void AddFunctionInfo(FunctionDebugInfo funcInfo) {
    functions_.Add(funcInfo);
    functionMap_[funcInfo.Name] = funcInfo;
  }

  public void AddMethodILToNativeMap(FunctionDebugInfo functionDebugInfo,
                                     List<(int ILOffset, int NativeOffset)> ilOffsets) {
    methodILNativeMap_[functionDebugInfo] = ilOffsets;
  }

  public void LoadingCompleted() {
    functions_.Sort();
  }

  public void AddMethodCode(long codeAddress, MethodCode code) {
    methodCodeMap_ ??= new Dictionary<long, MethodCode>();
    methodCodeMap_[codeAddress] = code;
  }

  public bool AnnotateSourceLocations(FunctionIR function, string functionName) {
    var funcInfo = FindFunction(functionName);

    if (funcInfo == null) {
      return false;
    }

    return AnnotateSourceLocations(function, funcInfo);
  }

  public bool LoadDebugInfo(string debugFilePath, IDebugInfoProvider other = null) {
    return true;
  }

  private bool EnsureHasSourceLines(FunctionDebugInfo functionDebugInfo) {
    if (functionDebugInfo == null || functionDebugInfo.IsUnknown) {
      return false;
    }

    if (functionDebugInfo.HasSourceLines) {
      return true; // Already populated.
    }

    if (ManagedSymbolFile == null || hasManagedSymbolFileFailure_) {
      return false; // Previous attempt failed.
    }

    // Locate the managed debug file.
    var options = SymbolSettings != null ? SymbolSettings : CoreSettingsProvider.SymbolSettings;

    if (File.Exists(ManagedSymbolFile.FileName)) {
      options.InsertSymbolPath(ManagedSymbolFile.FileName);
    }

    string symbolSearchPath = PDBDebugInfoProvider.ConstructSymbolSearchPath(options);

    using var logWriter = new StringWriter();
    using var symbolReader = new SymbolReader(logWriter, symbolSearchPath);
    symbolReader.SecurityCheck += s => true; // Allow symbols from "unsafe" locations.
    string debugFile =
      symbolReader.FindSymbolFilePath(ManagedSymbolFile.FileName, ManagedSymbolFile.Id, ManagedSymbolFile.Age);

    Trace.WriteLine($">> TraceEvent FindSymbolFilePath for {ManagedSymbolFile.FileName}: {debugFile}");
    Trace.IndentLevel = 1;
    Trace.WriteLine(logWriter.ToString());
    Trace.IndentLevel = 0;
    Trace.WriteLine("<< TraceEvent");

    if (!File.Exists(debugFile)) {
      // Don't try again if PDB not found.
      hasManagedSymbolFileFailure_ = true;
      return false;
    }

    lock (functionDebugInfo) {
      if (!methodILNativeMap_.TryGetValue(functionDebugInfo, out var ilOffsets)) {
        return false;
      }

      try {
        var pdb = symbolReader.OpenSymbolFile(debugFile);

        if (pdb == null) {
          hasManagedSymbolFileFailure_ = true;
          return false;
        }

        // Find the source lines and native code offset mapping for each IL offset.
        foreach (var pair in ilOffsets) {
          var sourceLoc = pdb.SourceLocationForManagedCode((uint)functionDebugInfo.Id, pair.ILOffset);

          if (sourceLoc != null) {
            if (sourceLoc.SourceFile != null && functionDebugInfo.SourceFileName == null) {
              functionDebugInfo.SourceFileName = sourceLoc.SourceFile.GetSourceFile();
              functionDebugInfo.OriginalSourceFileName ??= sourceLoc.SourceFile.BuildTimeFilePath;
            }

            //? TODO: Remove SourceFileName from SourceLineDebugInfo
            var lineInfo = new SourceLineDebugInfo(pair.NativeOffset, sourceLoc.LineNumber,
                                                   sourceLoc.ColumnNumber, functionDebugInfo.SourceFileName);
            functionDebugInfo.AddSourceLine(lineInfo);
          }
        }
      }
      catch (Exception ex) {
        Trace.TraceError($"Failed to read managed PDB from {debugFile}: {ex.Message}\n{ex.StackTrace}");
        hasManagedSymbolFileFailure_ = true;
      }

      return functionDebugInfo.HasSourceLines;
    }
  }

  private SourceFileDebugInfo GetSourceFileInfo(FunctionDebugInfo info) {
    return new SourceFileDebugInfo(info.SourceFileName,
                                   info.OriginalSourceFileName,
                                   info.FirstSourceLine.Line);
  }

  public struct AddressNamePair {
    public long Address { get; set; }
    public string Name { get; set; }

    public AddressNamePair(long address, string name) {
      Address = address;
      Name = name;
    }
  }

  public class MethodCode {
    public MethodCode(long address, int size, byte[] code) {
      Address = address;
      Size = size;
      Code = code;
      CallTargets = new List<AddressNamePair>();
    }

    public long Address { get; set; }
    public int Size { get; set; }
    public byte[] Code { get; set; }
    public List<AddressNamePair> CallTargets { get; set; }

    public string FindCallTarget(long address) {
      //? TODO: Map

      int index = CallTargets.FindIndex(item => item.Address == address);

      if (index != -1) {
        return CallTargets[index].Name;
      }

      return null;
    }
  }

  private class ManagedProcessCode {
    public int ProcessId { get; set; }
    public int MachineType { get; set; }
    public List<MethodCode> Methods { get; set; }
  }
}