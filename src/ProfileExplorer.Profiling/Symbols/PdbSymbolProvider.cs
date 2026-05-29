// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Runtime.InteropServices;
using Dia2Lib;

namespace ProfileExplorer.Profiling.Symbols;

/// <summary>
/// PDB symbol reader using the DIA SDK (msdia140.dll) via Dia2Lib COM interop.
/// Supports both registered COM and side-loaded DLL (no regsvr32 needed).
/// Ported from ProfileExplorerCore/Binary/PDBDebugInfoProvider.cs.
/// </summary>
internal class PdbSymbolProvider : IDebugInfoProvider {
  private const int MaxDemangledNameLength = 8192;
  private const int FunctionCacheMissThreshold = 100;

  private IDiaDataSource? diaSource_;
  private IDiaSession? session_;
  private IDiaSymbol? globalSymbol_;

  private List<FunctionDebugInfo>? sortedFuncList_;
  private Dictionary<string, FunctionDebugInfo>? functionsByName_;
  private bool sortedFuncListOverlapping_;
  private volatile int funcCacheMisses_;
  private string? debugFilePath_;

  private static bool diaRegistrationFailed_;
  private static string? diaRegistrationError_;
  private static readonly object undecorateLock_ = new();

  public static bool DiaRegistrationFailed => diaRegistrationFailed_;
  public static string? DiaRegistrationError => diaRegistrationError_;

  /// <summary>Optional path to msdia140.dll for side-loading.</summary>
  public static string? MsDiaPath { get; set; }

  public bool LoadDebugInfo(string debugFilePath) {
    if (!File.Exists(debugFilePath)) return false;
    debugFilePath_ = debugFilePath;

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

      LoadFunctionList();
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

  public IEnumerable<FunctionDebugInfo> EnumerateFunctions() => sortedFuncList_ ?? [];
  public List<FunctionDebugInfo> GetSortedFunctions() { if (sortedFuncList_ == null) LoadFunctionList(); return sortedFuncList_ ?? []; }

  public FunctionDebugInfo? FindFunction(string functionName) {
    if (functionsByName_?.TryGetValue(functionName, out var result) == true) return result;
    return sortedFuncList_?.FirstOrDefault(f => string.Equals(f.Name, functionName, StringComparison.Ordinal));
  }

  public FunctionDebugInfo? FindFunctionByRVA(long rva) {
    if (sortedFuncList_ != null) {
      var result = FunctionDebugInfo.BinarySearch(sortedFuncList_, rva, sortedFuncListOverlapping_);
      if (result != null) return result;
    }

    if (sortedFuncList_ == null && Interlocked.Increment(ref funcCacheMisses_) >= FunctionCacheMissThreshold) {
      LoadFunctionList();
      if (sortedFuncList_ != null) return FunctionDebugInfo.BinarySearch(sortedFuncList_, rva, sortedFuncListOverlapping_);
    }

    return FindFunctionByRVADirect(rva);
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
    lock (undecorateLock_) {
      try {
        var buffer = new char[MaxDemangledNameLength];
        int result = NativeMethods.UnDecorateSymbolName(decoratedName, buffer, MaxDemangledNameLength, 0);
        return result > 0 ? new string(buffer, 0, result) : decoratedName;
      }
      catch { return decoratedName; }
    }
  }

  public void Dispose() => Unload();

  // ── Private ──────────────────────────────────────────

  private void LoadFunctionList() {
    if (globalSymbol_ == null) return;
    try {
      var symbolMap = new Dictionary<long, FunctionDebugInfo>();
      var symbolList = new List<FunctionDebugInfo>();

      void Collect(SymTagEnum tag) {
        globalSymbol_.findChildren(tag, null, 0, out var enumSymbols);
        if (enumSymbols == null) return;
        try {
          while (true) {
            enumSymbols.Next(1, out var sym, out uint fetched);
            if (fetched == 0) break;
            try {
              string name = sym.name ?? "";
              long rva = sym.relativeVirtualAddress;
              uint size = (uint)sym.length;
              if (tag == SymTagEnum.SymTagPublicSymbol && symbolMap.TryGetValue(rva, out var existing) && existing.Size == size) {
                // Don't overwrite unmangled SymTagFunction names with mangled SymTagPublicSymbol names.
                // Only use the public symbol name if the function name is missing.
                if (string.IsNullOrEmpty(existing.Name)) {
                  existing.Name = name;
                }
              }
              else if (!symbolMap.ContainsKey(rva)) {
                var info = new FunctionDebugInfo(name, rva, size);
                symbolList.Add(info);
                symbolMap[rva] = info;
              }
            }
            finally { Marshal.ReleaseComObject(sym); }
          }
        }
        finally { Marshal.ReleaseComObject(enumSymbols); }
      }

      Collect(SymTagEnum.SymTagFunction);
      Collect(SymTagEnum.SymTagPublicSymbol);
      symbolList.Sort();

      for (int i = 0; i < symbolList.Count - 1; i++) {
        if (symbolList[i].EndRVA >= symbolList[i + 1].StartRVA) { sortedFuncListOverlapping_ = true; break; }
      }

      sortedFuncList_ = symbolList;
      functionsByName_ = new Dictionary<string, FunctionDebugInfo>(symbolList.Count, StringComparer.Ordinal);
      foreach (var func in symbolList) functionsByName_.TryAdd(func.Name, func);
    }
    catch { /* Function enumeration failed. */ }
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
