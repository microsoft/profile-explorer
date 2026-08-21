// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using Dia2Lib;
using ProfileExplorer.Core.Binary;

namespace ProfileExplorer.Profiling.Symbols;

/// <summary>
/// PDB symbol reader using the DIA SDK (msdia140.dll) via Dia2Lib COM interop.
/// Supports both registered COM and side-loaded DLL (no regsvr32 needed).
/// Ported from ProfileExplorerCore/Binary/PDBDebugInfoProvider.cs.
/// </summary>
public class PdbSymbolProvider : ISymbolDebugInfo {
  private const int MaxDemangledNameLength = 8192;
  private const int FunctionCacheMissThreshold = 100;

  // UnDecorateSymbolName (undname) flags — mirror PE's NativeMethods.UnDecorateFlags subset.
  private const int UndnameNoMsKeywords = 0x0002;   // strip __cdecl / __ptr64 etc. (PE's NoSpecialKeywords)
  private const int UndnameNoAllocationModel = 0x0008;
  private const int UndnameNoMsThistype = 0x0020;
  private const int UndnameNoAccessSpecifiers = 0x0080;
  private const int UndnameNoMemberType = 0x0200;
  private const int UndnameNameOnly = 0x1000;

  // The exact undname flag set DemangleFunctionName applies (minus NameOnly). Shared with the DIA
  // get_undecoratedNameEx overload comparison in FindFunctionSymbolByNameImpl so both sides produce
  // identical strings and overload selection keeps matching (strips __cdecl/__ptr64 like PE's
  // NoSpecialKeywords rendering).
  private const int UndnameDemangleFlags = UndnameNoAccessSpecifiers | UndnameNoAllocationModel |
                                           UndnameNoMemberType | UndnameNoMsKeywords | UndnameNoMsThistype;

  // DIA SDK enum DataKind value (cvconst.h); this generated Dia2Lib interop exposes
  // IDiaSymbol.dataKind as a raw uint rather than a named enum type.
  private const uint DataIsParam = 3;

  private IDiaDataSource? diaSource_;
  private IDiaSession? session_;
  private IDiaSymbol? globalSymbol_;

  private List<FunctionDebugInfo>? sortedFuncList_;
  private Dictionary<string, FunctionDebugInfo>? functionsByName_;
  private bool sortedFuncListOverlapping_;
  private volatile int funcCacheMisses_;
  private string? debugFilePath_;
  private SymbolFileDescriptor? cacheKey_;
  private string? cacheDirectory_;

  private static bool diaRegistrationFailed_;
  private static string? diaRegistrationError_;
  private static readonly object undecorateLock_ = new();

  public static bool DiaRegistrationFailed => diaRegistrationFailed_;
  public static string? DiaRegistrationError => diaRegistrationError_;

  /// <summary>Optional path to msdia140.dll for side-loading.</summary>
  public static string? MsDiaPath { get; set; }

  /// <summary>Processor architecture is not derived from the PDB; resolved elsewhere.</summary>
  public Machine? Architecture => null;

  public bool LoadDebugInfo(string debugFilePath, SymbolFileDescriptor? cacheKey = null,
                            string? cacheDirectory = null, bool enumerateImmediately = true) {
    if (!File.Exists(debugFilePath)) return false;
    debugFilePath_ = debugFilePath;
    cacheKey_ = cacheKey;
    cacheDirectory_ = cacheDirectory;

    try {
      diaSource_ = CreateDiaSource();
      if (diaSource_ == null) {
        diaRegistrationError_ ??= "Failed to create DIA source.";
        return false;
      }

      diaSource_.loadDataFromPdb(debugFilePath);
      diaSource_.openSession(out session_);
      if (session_ == null) return false;

      session_.findChildren(null, SymTagEnum.SymTagExe, null, 0, out var exeEnum);
      if (exeEnum != null) {
        exeEnum.Next(1, out var exeSym, out uint fetched);
        if (fetched > 0) globalSymbol_ = exeSym;
        Marshal.ReleaseComObject(exeEnum);
      }

      // When enumerateImmediately is false, defer reading the function list until the first
      // GetSortedFunctions/EnumerateFunctions call or the FindFunctionByRVA cache-miss threshold
      // (mirrors PE's lazy enumeration to avoid eagerly parsing PDBs that get few queries).
      if (enumerateImmediately) {
        EnsureFunctionListLoaded();
      }

      return true;
    }
    catch (COMException ex) {
      diaRegistrationFailed_ = true;
      diaRegistrationError_ = $"DIA COM error: 0x{ex.HResult:X8} - {ex.Message}";
      return false;
    }
    catch (Exception ex) {
      diaRegistrationError_ = $"DIA load error: {ex.GetType().Name}: {ex.Message}";
      return false;
    }
  }

  public void Unload() {
    if (globalSymbol_ != null) { Marshal.ReleaseComObject(globalSymbol_); globalSymbol_ = null; }
    if (session_ != null) { Marshal.ReleaseComObject(session_); session_ = null; }
    if (diaSource_ != null) { Marshal.ReleaseComObject(diaSource_); diaSource_ = null; }
    sortedFuncList_ = null;
    functionsByName_ = null;
  }

  public IEnumerable<FunctionDebugInfo> EnumerateFunctions() { EnsureFunctionListLoaded(); return sortedFuncList_ ?? []; }
  public List<FunctionDebugInfo> GetSortedFunctions() { EnsureFunctionListLoaded(); return sortedFuncList_ ?? []; }

  public FunctionDebugInfo? FindFunction(string functionName) {
    if (functionsByName_?.TryGetValue(functionName, out var result) == true) return result;

    var listMatch = sortedFuncList_?.FirstOrDefault(f => string.Equals(f.Name, functionName, StringComparison.Ordinal));
    if (listMatch != null) return listMatch;

    // Fall back to a DIA query using the demangled name (mirrors PE's FindFunctionSymbol):
    // the cached list stores mangled public-symbol names, so a demangled query name
    // won't match by string — ask DIA directly, preferring the public symbol's mangled name.
    var sym = FindFunctionSymbolByName(functionName);
    if (sym == null) return null;

    try { return new FunctionDebugInfo(sym.name ?? "", sym.relativeVirtualAddress, (uint)sym.length); }
    finally { Marshal.ReleaseComObject(sym); }
  }

  public FunctionDebugInfo? FindFunctionByRVA(long rva) {
    if (sortedFuncList_ != null) {
      var result = FunctionDebugInfo.BinarySearch(sortedFuncList_, rva, sortedFuncListOverlapping_);
      if (result != null) return result;
    }

    if (sortedFuncList_ == null && Interlocked.Increment(ref funcCacheMisses_) >= FunctionCacheMissThreshold) {
      EnsureFunctionListLoaded();

      if (sortedFuncList_ != null) {
        var result = FunctionDebugInfo.BinarySearch(sortedFuncList_, rva, sortedFuncListOverlapping_);
        if (result != null) return result;
      }
    }

    // List search missed (or list not yet loaded): fall back to a direct DIA query, which also
    // resolves addresses inside PGO-split function chunks that the contiguous list doesn't cover.
    return FindFunctionByRVADirect(rva);
  }

  /// <summary>
  /// Recovers a function's signature (return type, calling convention, parameters in declaration
  /// order with names when not stripped) from PDB/DIA type information. This is exact PDB-sourced
  /// evidence: when no PDB is loaded, the function's DIA symbol can't be found by RVA, or it has
  /// no function-type record (all legitimate outcomes -- e.g. public-symbol-only PDBs), this
  /// returns null rather than fabricating a signature.
  /// </summary>
  public FunctionTypeInfo? TryGetFunctionSignature(FunctionDebugInfo funcInfo) {
    if (session_ == null || funcInfo == null || funcInfo.IsUnknown) {
      return null;
    }

    IDiaSymbol? funcSym = null;
    IDiaSymbol? functionType = null;

    try {
      session_.findSymbolByRVA((uint)funcInfo.StartRVA, SymTagEnum.SymTagFunction, out funcSym);

      if (funcSym == null) {
        return null;
      }

      functionType = funcSym.type;

      if (functionType == null) {
        return null; // No function-type record for this symbol.
      }

      IDiaSymbol? returnType = null;
      string? returnTypeName;

      try {
        returnType = functionType.type;
        returnTypeName = RenderTypeName(returnType);
      }
      finally {
        if (returnType != null) Marshal.ReleaseComObject(returnType);
      }

      string callingConvention = RenderCallingConvention(functionType);

      var parameterNames = new List<string?>();
      TryEnumerateParameterNames(funcSym, parameterNames);

      var parameters = new List<ParameterTypeInfo>();
      EnumerateArgumentTypes(functionType, parameterNames, parameters);

      return new FunctionTypeInfo(returnTypeName, callingConvention, parameters);
    }
    catch {
      return null; // DIA COM calls can throw on malformed/unusual PDB records -- fail closed.
    }
    finally {
      if (functionType != null) Marshal.ReleaseComObject(functionType);
      if (funcSym != null) Marshal.ReleaseComObject(funcSym);
    }
  }

  /// <summary>
  /// Collects declared parameter names (in DIA enumeration order, which matches declaration order)
  /// by walking the function's SymTagData children and filtering to DataIsParam. Optimized code
  /// commonly strips these -- an empty result is expected and not an error.
  /// </summary>
  private void TryEnumerateParameterNames(IDiaSymbol funcSym, List<string?> names) {
    try {
      funcSym.findChildren(SymTagEnum.SymTagData, null, 0, out var enumSyms);

      if (enumSyms == null) {
        return;
      }

      try {
        while (true) {
          enumSyms.Next(1, out var child, out uint fetched);

          if (fetched == 0) {
            break;
          }

          try {
            // DIA's IDiaSymbol.dataKind returns a raw uint (this generated interop doesn't expose
            // a named DataKind enum type, unlike the SymTagEnum used for query filtering). 3 is
            // DataIsParam per the DIA SDK's stable, well-documented enum DataKind.
            if (child.dataKind == DataIsParam) {
              names.Add(child.name);
            }
          }
          finally {
            Marshal.ReleaseComObject(child);
          }
        }
      }
      finally {
        Marshal.ReleaseComObject(enumSyms);
      }
    }
    catch {
      // Leave names empty -- parameters are still reported, just without names.
    }
  }

  /// <summary>
  /// Enumerates the function type's SymTagFunctionArgType children (one per parameter, in
  /// declaration order) and pairs each with the corresponding name collected by
  /// <see cref="TryEnumerateParameterNames"/> (by position -- DIA doesn't otherwise correlate the
  /// two lists). A parameter-count mismatch (e.g. names partially stripped) still produces the
  /// correct types; extra/missing names are simply left null rather than guessed.
  /// </summary>
  private void EnumerateArgumentTypes(IDiaSymbol functionType, List<string?> parameterNames,
                                      List<ParameterTypeInfo> parameters) {
    try {
      functionType.findChildren(SymTagEnum.SymTagFunctionArgType, null, 0, out var enumSyms);

      if (enumSyms == null) {
        return;
      }

      try {
        int index = 0;

        while (true) {
          enumSyms.Next(1, out var argType, out uint fetched);

          if (fetched == 0) {
            break;
          }

          try {
            IDiaSymbol? paramType = null;

            try {
              paramType = argType.type; // SymTagFunctionArgType wraps the real parameter type.
              string? typeName = RenderTypeName(paramType);
              string? name = index < parameterNames.Count ? parameterNames[index] : null;
              parameters.Add(new ParameterTypeInfo(name, typeName));
            }
            finally {
              if (paramType != null) Marshal.ReleaseComObject(paramType);
            }

            index++;
          }
          finally {
            Marshal.ReleaseComObject(argType);
          }
        }
      }
      finally {
        Marshal.ReleaseComObject(enumSyms);
      }
    }
    catch {
      // Leave parameters as whatever was collected before the failure -- partial info beats none.
    }
  }

  /// <summary>
  /// Best-effort DIA type-name renderer: base types (int, char, bool, ...), pointers (recursively
  /// rendered pointee + "*"), arrays ("ElementType[N]"), and named UDT/enum/typedef (via
  /// <c>.name</c>). Returns an explicit "&lt;unknown-type:SymTagN&gt;" placeholder rather than
  /// throwing or fabricating a name for symbol kinds this doesn't recognize (e.g. function
  /// pointers, bitfields) -- an honest gap, not a silent wrong answer.
  /// </summary>
  private string? RenderTypeName(IDiaSymbol? type) {
    if (type == null) {
      return "void"; // A null return-type symbol conventionally means "void" in DIA.
    }

    try {
      var symTag = (SymTagEnum)type.symTag;

      switch (symTag) {
        case SymTagEnum.SymTagBaseType:
          return RenderBaseTypeName(type);

        case SymTagEnum.SymTagPointerType: {
          IDiaSymbol? pointee = null;

          try {
            pointee = type.type;
            return $"{RenderTypeName(pointee) ?? "void"}*";
          }
          finally {
            if (pointee != null) Marshal.ReleaseComObject(pointee);
          }
        }

        case SymTagEnum.SymTagArrayType: {
          IDiaSymbol? elementType = null;

          try {
            elementType = type.type;
            string elementName = RenderTypeName(elementType) ?? "void";
            ulong elementLength = elementType?.length ?? 0;
            long count = elementLength > 0 ? (long)(type.length / elementLength) : 0;
            return $"{elementName}[{count}]";
          }
          finally {
            if (elementType != null) Marshal.ReleaseComObject(elementType);
          }
        }

        case SymTagEnum.SymTagUDT:
        case SymTagEnum.SymTagEnum:
        case SymTagEnum.SymTagTypedef:
          return string.IsNullOrEmpty(type.name) ? $"<unnamed-{symTag}>" : type.name;

        default:
          return $"<unknown-type:{symTag}>";
      }
    }
    catch {
      return null;
    }
  }

  /// <summary>
  /// Renders a DIA BasicType (<c>type.baseType</c>) + size (<c>type.length</c>) pair into a C-style
  /// type name. BasicType values are per the DIA SDK (cvconst.h) -- stable and well-documented;
  /// an unrecognized (baseType, length) combination is rendered explicitly rather than guessed.
  /// </summary>
  private static string RenderBaseTypeName(IDiaSymbol type) {
    uint baseType = type.baseType;
    ulong length = type.length;

    return (baseType, length) switch {
      (1, _) => "void",
      (2, _) => "char",
      (3, _) => "wchar_t",
      (6, 1) => "signed char",
      (6, 2) => "short",
      (6, 4) => "int",
      (6, 8) => "long long",
      (7, 1) => "unsigned char",
      (7, 2) => "unsigned short",
      (7, 4) => "unsigned int",
      (7, 8) => "unsigned long long",
      (8, 4) => "float",
      (8, 8) => "double",
      (10, _) => "bool",
      (13, 4) => "long",
      (14, 4) => "unsigned long",
      (32, _) => "char16_t",
      (33, _) => "char32_t",
      _ => $"<basetype:{baseType}/{length}>"
    };
  }

  /// <summary>
  /// Renders a DIA calling-convention code (<c>functionType.callingConvention</c>, CV_call_e per
  /// the DIA SDK/cvconst.h -- stable and well-documented) into its familiar MSVC keyword.
  /// </summary>
  private static string RenderCallingConvention(IDiaSymbol functionType) {
    try {
      uint cc = functionType.callingConvention;

      return cc switch {
        0 => "__cdecl",
        4 => "__fastcall",
        7 => "__stdcall",
        11 => "__thiscall",
        22 => "__clrcall",
        _ => $"<callingconvention:{cc}>"
      };
    }
    catch {
      return "<unknown>";
    }
  }

  public bool PopulateSourceLines(FunctionDebugInfo funcInfo) {
    if (session_ == null) return false;
    try {
      session_.findLinesByRVA((uint)funcInfo.StartRVA, (uint)funcInfo.Size, out var lineEnum);
      if (lineEnum == null) return false;

      funcInfo.SourceLines ??= [];
      try {
        while (true) {
          lineEnum.Next(1, out var line, out uint fetched);
          if (fetched == 0) break;
          string? filePath = null;
          try { filePath = line.sourceFile?.fileName; } catch { }
          funcInfo.SourceLines.Add(new SourceLineDebugInfo((int)line.addressOffset, (int)line.lineNumber, (int)line.columnNumber, filePath));
        }
      }
      finally { Marshal.ReleaseComObject(lineEnum); }

      if (funcInfo.SourceLines.Count > 0) funcInfo.SourceFileName = funcInfo.SourceLines[0].FilePath;
      return funcInfo.SourceLines.Count > 0;
    }
    catch { return false; }
  }

  public SourceFileDebugInfo FindFunctionSourceFilePath(string functionName) {
    var func = FindFunction(functionName);
    return func == null ? SourceFileDebugInfo.Unknown : FindSourceFilePathByRVA(func.RVA);
  }

  public SourceFileDebugInfo FindSourceFilePathByRVA(long rva) {
    var lineInfo = FindSourceLineByRVA(rva);
    return lineInfo.IsUnknown ? SourceFileDebugInfo.Unknown : new SourceFileDebugInfo(lineInfo.FilePath, lineInfo.FilePath, lineInfo.Line);
  }

  public SourceLineDebugInfo FindSourceLineByRVA(long rva, bool includeInlinees = false) {
    if (session_ == null) return SourceLineDebugInfo.Unknown;
    try {
      session_.findLinesByRVA((uint)rva, 0, out var lineEnum);
      if (lineEnum == null) return SourceLineDebugInfo.Unknown;
      try {
        lineEnum.Next(1, out var line, out uint fetched);
        if (fetched == 0) return SourceLineDebugInfo.Unknown;
        string? filePath = null;
        try { filePath = line.sourceFile?.fileName; } catch { }
        return new SourceLineDebugInfo((int)line.addressOffset, (int)line.lineNumber, (int)line.columnNumber, filePath);
      }
      finally { Marshal.ReleaseComObject(lineEnum); }
    }
    catch { return SourceLineDebugInfo.Unknown; }
  }

  public static string? UndecorateName(string decoratedName) {
    return DemangleFunctionName(decoratedName);
  }

  /// <summary>
  /// Demangle an MSVC C++ symbol name. Mirrors PE's PDBDebugInfoProvider.DemangleFunctionName:
  /// only names starting with '?' are mangled; others are returned unchanged. Uses the same
  /// undname flag set and a global lock (UnDecorateSymbolName is not thread-safe).
  /// </summary>
  public static string DemangleFunctionName(string name, bool onlyName = false) {
    // Mangled MSVC C++ names always start with a '?' char.
    if (string.IsNullOrEmpty(name) || !name.StartsWith('?')) {
      return name;
    }

    // Include NoMsKeywords/NoMsThistype so calling-convention + pointer modifiers (__cdecl, __ptr64)
    // are stripped, matching the pre-refactor core's NoSpecialKeywords rendering. The same flag set is
    // reused for the DIA overload comparison in FindFunctionSymbolByNameImpl (see UndnameDemangleFlags).
    int flags = UndnameDemangleFlags;
    if (onlyName) flags |= UndnameNameOnly;

    // DbgHelp UnDecorateSymbolName is not thread safe and can return bogus
    // function names if not called under a global lock.
    lock (undecorateLock_) {
      try {
        var buffer = new char[MaxDemangledNameLength];
        int result = NativeMethods.UnDecorateSymbolName(name, buffer, MaxDemangledNameLength, flags);
        return result > 0 ? new string(buffer, 0, result) : name;
      }
      catch { return name; }
    }
  }

  public void Dispose() => Unload();

  // ── Private ──────────────────────────────────────────

  /// <summary>
  /// Load the function list on demand (from cache when available, else enumerate the PDB and
  /// persist the cache). Idempotent — a no-op once the list is populated.
  /// </summary>
  private void EnsureFunctionListLoaded() {
    if (sortedFuncList_ != null) return;
    if (!TryLoadFunctionListFromCache(cacheKey_, cacheDirectory_)) {
      LoadFunctionList();
      TrySaveFunctionListToCache(cacheKey_, cacheDirectory_);
    }
  }

  private bool TryLoadFunctionListFromCache(SymbolFileDescriptor? cacheKey, string? cacheDirectory) {
    if (cacheKey == null || string.IsNullOrEmpty(cacheDirectory)) return false;

    try {
      var cached = SymbolFileCache.DeserializeAsync(cacheKey, cacheDirectory).GetAwaiter().GetResult();

      if (cached?.FunctionList is { Count: > 0 }) {
        InitializeFunctionList(cached.FunctionList);
        return true;
      }
    }
    catch {
      // Cache miss/corruption — fall back to DIA enumeration.
    }

    return false;
  }

  private void TrySaveFunctionListToCache(SymbolFileDescriptor? cacheKey, string? cacheDirectory) {
    if (cacheKey == null || string.IsNullOrEmpty(cacheDirectory) || sortedFuncList_ is not { Count: > 0 }) {
      return;
    }

    try {
      var cache = new SymbolFileCache { SymbolFile = cacheKey, FunctionList = sortedFuncList_ };
      SymbolFileCache.SerializeAsync(cache, cacheDirectory).GetAwaiter().GetResult();
    }
    catch {
      // Best-effort cache write.
    }
  }

  private void InitializeFunctionList(List<FunctionDebugInfo> symbolList) {
    // Sort by StartRVA (via CompareTo), breaking ties deterministically by name. ICF-folded functions
    // share the same RVA (and often size), and DIA enumerates them in a non-deterministic order; an
    // unstable RVA-only sort therefore lets that order leak into FindFunctionByRVA, making disassembly
    // call-target names differ between runs. The ordinal name tie-break makes the list — and thus the
    // resolved name for a folded address — stable and reproducible.
    symbolList.Sort((a, b) => {
      int rvaCompare = a.CompareTo(b);
      return rvaCompare != 0 ? rvaCompare : string.CompareOrdinal(a.Name, b.Name);
    });

    sortedFuncListOverlapping_ = false;

    for (int i = 0; i < symbolList.Count - 1; i++) {
      if (symbolList[i].EndRVA >= symbolList[i + 1].StartRVA) {
        sortedFuncListOverlapping_ = true;
        break;
      }
    }

    sortedFuncList_ = symbolList;
    functionsByName_ = new Dictionary<string, FunctionDebugInfo>(symbolList.Count, StringComparer.Ordinal);
    foreach (var func in symbolList) functionsByName_.TryAdd(func.Name, func);
  }

  private void LoadFunctionList() {
    if (globalSymbol_ == null) return;
    try {
      var symbolMap = new Dictionary<long, FunctionDebugInfo>();
      var symbolList = new List<FunctionDebugInfo>();

      void Enumerate(SymTagEnum tag, Action<string, long, uint> handle) {
        globalSymbol_.findChildren(tag, null, 0, out var enumSymbols);
        if (enumSymbols == null) return;
        try {
          while (true) {
            enumSymbols.Next(1, out var sym, out uint fetched);
            if (fetched == 0) break;
            try {
              handle(sym.name ?? "", sym.relativeVirtualAddress, (uint)sym.length);
            }
            finally { Marshal.ReleaseComObject(sym); }
          }
        }
        finally { Marshal.ReleaseComObject(enumSymbols); }
      }

      // Mirror PE's legacy CollectFunctionDebugInfo exactly so function naming matches the
      // historical behavior, including ICF-folded addresses that map to multiple symbol names:
      // add every function symbol (the last one at a given RVA wins the map slot), then let a
      // public symbol with the same RVA+size overwrite the name (public symbols carry the
      // mangled name). Public symbols at an RVA with no function symbol are added as-is.
      Enumerate(SymTagEnum.SymTagFunction, (name, rva, size) => {
        var info = new FunctionDebugInfo(name, rva, size);
        symbolList.Add(info);
        symbolMap[rva] = info;
      });

      Enumerate(SymTagEnum.SymTagPublicSymbol, (name, rva, size) => {
        if (symbolMap.TryGetValue(rva, out var existing)) {
          if (existing.Size == size) {
            existing.Name = name;
          }
        }
        else {
          symbolList.Add(new FunctionDebugInfo(name, rva, size));
        }
      });

      InitializeFunctionList(symbolList);
    }
    catch { /* Function enumeration failed. */ }
  }

  /// <summary>
  /// Locate a function/public symbol by name via DIA, demangling the query name the way PE does
  /// (mirrors FindFunctionSymbol/FindFunctionSymbolImpl). Tries SymTagFunction first, then
  /// SymTagPublicSymbol; among class::method matches, prefers the one whose full undecorated
  /// name matches, else returns the first candidate.
  /// </summary>
  private IDiaSymbol? FindFunctionSymbolByName(string functionName) {
    if (globalSymbol_ == null) return null;

    string demangledName = DemangleFunctionName(functionName);
    string queryDemangledName = DemangleFunctionName(functionName, onlyName: true);

    return FindFunctionSymbolByNameImpl(SymTagEnum.SymTagFunction, demangledName, queryDemangledName)
           ?? FindFunctionSymbolByNameImpl(SymTagEnum.SymTagPublicSymbol, demangledName, queryDemangledName);
  }

  private IDiaSymbol? FindFunctionSymbolByNameImpl(SymTagEnum symbolType, string demangledName,
                                                   string queryDemangledName) {
    IDiaEnumSymbols? symbolEnum = null;
    try {
      globalSymbol_.findChildren(symbolType, queryDemangledName, 0, out symbolEnum);
      if (symbolEnum == null) return null;

      IDiaSymbol? candidateSymbol = null;
      while (true) {
        symbolEnum.Next(1, out var symbol, out uint retrieved);
        if (retrieved == 0) break;

        // Class::function matches; check the full unmangled name to pick the right overload. Use the
        // SAME flag set DemangleFunctionName produced demangledName with, so the strings are comparable.
        try {
          symbol.get_undecoratedNameEx(UndnameDemangleFlags, out string symbolDemangledName);
          if (symbolDemangledName == demangledName) {
            // Exact match — release any earlier first-match candidate before returning.
            if (candidateSymbol != null && !ReferenceEquals(symbol, candidateSymbol)) {
              Marshal.ReleaseComObject(candidateSymbol);
            }

            return symbol;
          }
        }
        catch { /* ignore and keep first candidate */ }

        // Keep the first symbol as a fallback candidate; release any others.
        if (candidateSymbol == null) {
          candidateSymbol = symbol;
        }
        else if (!ReferenceEquals(symbol, candidateSymbol)) {
          Marshal.ReleaseComObject(symbol);
        }
      }

      return candidateSymbol; // First match (PE returns first match).
    }
    catch { return null; }
    finally {
      if (symbolEnum != null) Marshal.ReleaseComObject(symbolEnum);
    }
  }

  private FunctionDebugInfo? FindFunctionByRVADirect(long rva) {
    if (session_ == null) return null;
    try {
      session_.findSymbolByRVA((uint)rva, SymTagEnum.SymTagFunction, out var funcSym);
      session_.findSymbolByRVA((uint)rva, SymTagEnum.SymTagPublicSymbol, out var pubSym);

      IDiaSymbol? best = funcSym;
      if (pubSym != null) {
        if (funcSym == null) { best = pubSym; }
        else if (funcSym.relativeVirtualAddress == pubSym.relativeVirtualAddress && funcSym.length == pubSym.length) {
          best = pubSym; Marshal.ReleaseComObject(funcSym);
        }
        else { Marshal.ReleaseComObject(pubSym); }
      }
      if (best == null) return null;
      try { return new FunctionDebugInfo(best.name ?? "", best.relativeVirtualAddress, (uint)best.length); }
      finally { Marshal.ReleaseComObject(best); }
    }
    catch { return null; }
  }

  /// <summary>
  /// Create a DIA data source (<c>Dia2Lib.IDiaDataSource</c>) using registration-free side-loading of
  /// msdia140.dll, falling back to registered COM. Exposed so other DIA consumers (e.g. PE Core's
  /// <c>PDBDebugInfoProvider</c>) share this activation path instead of depending on a
  /// regsvr32-registered msdia140.dll — which may be missing or the wrong architecture (e.g. an x86
  /// build hijacking the CLSID machine-wide). Returned as <see cref="object"/> so callers in other
  /// assemblies don't need a direct Dia2Lib reference at the call site (they resolve the DIA type
  /// transitively and cast). Returns null if both activation paths fail; see
  /// <see cref="DiaRegistrationError"/>.
  /// </summary>
  public static object? CreateDiaDataSource() {
    return CreateDiaSource();
  }

  private static IDiaDataSource? CreateDiaSource() {
    diaRegistrationError_ = null;
    var source = TryCreateViaSideLoad();
    if (source != null) return source;
    string? sideErr = diaRegistrationError_;

    diaRegistrationError_ = null;
    source = TryCreateViaRegistry();
    if (source != null) return source;
    string? regErr = diaRegistrationError_;

    diaRegistrationError_ = $"Side-load: [{sideErr ?? "no msdia140.dll"}]. Registry: [{regErr ?? "COM failed"}]";
    return null;
  }

  private static IDiaDataSource? TryCreateViaSideLoad() {
    string? dllPath = MsDiaPath;
    if (string.IsNullOrEmpty(dllPath)) {
      string dir = Path.GetDirectoryName(typeof(PdbSymbolProvider).Assembly.Location) ?? "";
      string[] candidates = [
        Path.Combine(dir, "msdia140.dll"),
        Path.Combine(dir, "x64", "msdia140.dll"),
        Path.Combine(dir, "amd64", "msdia140.dll"),
        Path.Combine(dir, "runtimes", "win-x64", "native", "msdia140.dll"),
        Path.Combine(dir, "..", "external", "msdia140.dll"),
        Path.Combine(dir, "..", "..", "..", "..", "external", "msdia140.dll"),
        Path.Combine(dir, "..", "..", "..", "..", "..", "external", "msdia140.dll"),
      ];
      dllPath = candidates.FirstOrDefault(File.Exists);
    }
    if (string.IsNullOrEmpty(dllPath) || !File.Exists(dllPath)) return null;

    try {
      nint hModule = NativeMethods.LoadLibrary(dllPath);
      if (hModule == 0) { diaRegistrationError_ = $"LoadLibrary failed for {dllPath}"; return null; }

      nint proc = NativeMethods.GetProcAddress(hModule, "DllGetClassObject");
      if (proc == 0) { diaRegistrationError_ = "DllGetClassObject not found"; return null; }

      var getClassObj = Marshal.GetDelegateForFunctionPointer<NativeMethods.DllGetClassObjectDelegate>(proc);
      var clsid = new Guid("E6756135-1E65-4D17-8576-610761398C3C");
      var iid = new Guid("00000001-0000-0000-C000-000000000046");
      int hr = getClassObj(ref clsid, ref iid, out var factory);
      if (hr != 0) { diaRegistrationError_ = $"DllGetClassObject HR=0x{hr:X8}"; return null; }

      try {
        var cf = (NativeMethods.IClassFactory)factory;
        var iunknown = new Guid("00000000-0000-0000-C000-000000000046");
        cf.CreateInstance(null, ref iunknown, out var instance);
        return instance as IDiaDataSource;
      }
      finally { Marshal.ReleaseComObject(factory); }
    }
    catch (Exception ex) {
      diaRegistrationError_ = $"Side-load: {ex.GetType().Name}: {ex.Message}";
      return null;
    }
  }

  private static IDiaDataSource? TryCreateViaRegistry() {
    try { return new DiaSourceClass(); }
    catch (COMException ex) {
      diaRegistrationFailed_ = true;
      diaRegistrationError_ = $"COM error: 0x{ex.HResult:X8} - {ex.Message}";
      return null;
    }
    catch { return null; }
  }

  private static class NativeMethods {
    [DllImport("dbghelp.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    public static extern int UnDecorateSymbolName(string name, [Out] char[] outputString, int maxStringLength, int flags);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern nint LoadLibrary(string lpLibFileName);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    public static extern nint GetProcAddress(nint hModule, string lpProcName);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int DllGetClassObjectDelegate(ref Guid rclsid, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);

    [ComImport, Guid("00000001-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IClassFactory {
      void CreateInstance([MarshalAs(UnmanagedType.IUnknown)] object? pUnkOuter, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppvObject);
      void LockServer([MarshalAs(UnmanagedType.Bool)] bool fLock);
    }
  }
}
