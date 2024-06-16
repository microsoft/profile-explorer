// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
#define TRACE_EVENT
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dia2Lib;
using IRExplorerCore;
using IRExplorerCore.IR;
using IRExplorerCore.IR.Tags;
using Microsoft.Diagnostics.Symbols;

namespace IRExplorerUI.Compilers;

//? TODO: Use for-each iterators everywhere
public sealed class PDBDebugInfoProvider : IDebugInfoProvider {
  private const int MaxDemangledFunctionNameLength = 8192;
  private const int FunctionCacheMissThreshold = 100;

  //? TODO: Save cache between sessions, including the unavailable PDBs.
  //? Invalidate unavailable ones if SymbolOption paths change so they get a chance
  //? to be searched for in new locations.
  private static ConcurrentDictionary<SymbolFileDescriptor, DebugFileSearchResult> resolvedSymbolsCache_ =
    new ConcurrentDictionary<SymbolFileDescriptor, DebugFileSearchResult>();
  private ConcurrentDictionary<long, SourceFileDebugInfo> sourceFileByRvaCache_ = new();
  private ConcurrentDictionary<string, SourceFileDebugInfo> sourceFileByNameCache_ = new();
  private ConcurrentDictionary<uint, List<SourceStackFrame>> inlineeByRvaCache_ = new();
  private static object undecorateLock_ = new object();
  private SymbolFileDescriptor symbolFile_;
  private SymbolFileSourceSettings settings_;
  private SymbolFileCache symbolCache_;
  private SymbolReader symbolReader_;
  private StringWriter symbolReaderLog_;
  private NativeSymbolModule symbolReaderPDB_;
  private string debugFilePath_;
  private IDiaDataSource diaSource_;
  private IDiaSession session_;
  private IDiaSymbol globalSymbol_;
  private List<FunctionDebugInfo> sortedFuncList_;
  private volatile int funcCacheMisses_;
  private bool loadFailed_;
  private bool disposed_;

  public PDBDebugInfoProvider(SymbolFileSourceSettings settings) {
    settings_ = settings;
  }

  ~PDBDebugInfoProvider() {
    Dispose(false);
  }

  public SymbolFileSourceSettings SymbolSettings { get; set; }
  public Machine? Architecture => null;

  public static async Task<DebugFileSearchResult>
    LocateDebugInfoFileAsync(SymbolFileDescriptor symbolFile,
                             SymbolFileSourceSettings settings) {
    if (symbolFile == null) {
      return DebugFileSearchResult.None;
    }

    if (resolvedSymbolsCache_.TryGetValue(symbolFile, out var searchResult)) {
      return searchResult;
    }

    return await Task.Run(() => {
      return LocateDebugInfoFile(symbolFile, settings);
    }).ConfigureAwait(false);
  }

  public static DebugFileSearchResult
    LocateDebugInfoFile(SymbolFileDescriptor symbolFile,
                        SymbolFileSourceSettings settings) {
    if (symbolFile == null) {
      return DebugFileSearchResult.None;
    }

    if (resolvedSymbolsCache_.TryGetValue(symbolFile, out var searchResult)) {
      return searchResult;
    }

    string result = null;
    using var logWriter = new StringWriter();

    // In case there is a timeout downloading the symbols, try again.
    string symbolSearchPath = ConstructSymbolSearchPath(settings);
    using var authHandler = new BasicAuthenticationHandler(settings);
    using var symbolReader = new SymbolReader(logWriter, symbolSearchPath, authHandler);
    symbolReader.SecurityCheck += s => true; // Allow symbols from "unsafe" locations.

    try {
      Trace.WriteLine($"Start PDB download for {symbolFile.FileName}, {symbolFile.Id}, {symbolFile.Age}");
      result = symbolReader.FindSymbolFilePath(symbolFile.FileName, symbolFile.Id, symbolFile.Age);
    }
    catch (Exception ex) {
      Trace.TraceError($"Failed FindSymbolFilePath for {symbolFile.FileName}: {ex.Message}");
    }

#if DEBUG
    Trace.WriteLine($">> TraceEvent FindSymbolFilePath for {symbolFile.FileName}");
    Trace.IndentLevel = 1;
    Trace.WriteLine(logWriter.ToString());
    Trace.IndentLevel = 0;
    Trace.WriteLine("<< TraceEvent");
#endif

    if (!string.IsNullOrEmpty(result) && File.Exists(result)) {
      searchResult = DebugFileSearchResult.Success(symbolFile, result, logWriter.ToString());
    }
    else {
      searchResult = DebugFileSearchResult.Failure(symbolFile, logWriter.ToString());
    }

    resolvedSymbolsCache_.TryAdd(symbolFile, searchResult);
    return searchResult;
  }

  public static string ConstructSymbolSearchPath(SymbolFileSourceSettings settings) {
    string symbolPath = "";

    if (settings.UseEnvironmentVarSymbolPaths) {
      symbolPath += $"{settings.EnvironmentVarSymbolPath};";
    }

    foreach (string path in settings.SymbolPaths) {
      if (!string.IsNullOrEmpty(path)) {
        symbolPath += $"{path};";
      }
    }

    return symbolPath;
  }

  public static async Task<DebugFileSearchResult>
    LocateDebugInfoFile(string imagePath, SymbolFileSourceSettings settings) {
    using var binaryInfo = new PEBinaryInfoProvider(imagePath);

    if (binaryInfo.Initialize()) {
      return await LocateDebugInfoFileAsync(binaryInfo.SymbolFileInfo, settings).ConfigureAwait(false);
    }

    return DebugFileSearchResult.None;
  }

  public static string DemangleFunctionName(string name, FunctionNameDemanglingOptions options =
                                              FunctionNameDemanglingOptions.Default) {
    // Mangled MSVC C++ names always start with a ? char. 
    if (string.IsNullOrEmpty(name) || !name.StartsWith('?')) {
      return name;
    }
    
    var sb = new StringBuilder(MaxDemangledFunctionNameLength);
    var flags = NativeMethods.UnDecorateFlags.UNDNAME_COMPLETE;
    flags |= NativeMethods.UnDecorateFlags.UNDNAME_NO_ACCESS_SPECIFIERS |
             NativeMethods.UnDecorateFlags.UNDNAME_NO_ALLOCATION_MODEL |
             NativeMethods.UnDecorateFlags.UNDNAME_NO_MEMBER_TYPE;

    if (options.HasFlag(FunctionNameDemanglingOptions.OnlyName)) {
      flags |= NativeMethods.UnDecorateFlags.UNDNAME_NAME_ONLY;
    }

    if (options.HasFlag(FunctionNameDemanglingOptions.NoSpecialKeywords)) {
      flags |= NativeMethods.UnDecorateFlags.UNDNAME_NO_MS_KEYWORDS;
      flags |= NativeMethods.UnDecorateFlags.UNDNAME_NO_MS_THISTYPE;
    }

    if (options.HasFlag(FunctionNameDemanglingOptions.NoReturnType)) {
      flags |= NativeMethods.UnDecorateFlags.UNDNAME_NO_FUNCTION_RETURNS;
    }

    // DbgHelp UnDecorateSymbolName is not thread safe and can
    // return bogus function names if not under a global lock.
    lock (undecorateLock_) {
      NativeMethods.UnDecorateSymbolName(name, sb, MaxDemangledFunctionNameLength, flags);
    }

    return sb.ToString();
  }

  public static string DemangleFunctionName(IRTextFunction function, FunctionNameDemanglingOptions options =
                                              FunctionNameDemanglingOptions.Default) {
    return DemangleFunctionName(function.Name, options);
  }

  public bool LoadDebugInfo(DebugFileSearchResult debugFile, IDebugInfoProvider other = null) {
    if (debugFile == null || !debugFile.Found) {
      return false;
    }

    symbolFile_ = debugFile.SymbolFile;
    return LoadDebugInfo(debugFile.FilePath, other);
  }

  private bool LoadDebugInfo(string debugFilePath, IDebugInfoProvider other = null) {
    if (loadFailed_) {
      return false; // Failed before, don't try again.
    }

    try {
      debugFilePath_ = debugFilePath;
      diaSource_ = new DiaSourceClass();
      diaSource_.loadDataFromPdb(debugFilePath);
      diaSource_.openSession(out session_);
    }
    catch (Exception ex) {
      Trace.TraceError($"Failed to load debug file {debugFilePath}: {ex.Message}");
      loadFailed_ = true;
      return false;
    }

    try {
      session_.findChildren(null, SymTagEnum.SymTagExe, null, 0, out var exeSymEnum);
      globalSymbol_ = exeSymEnum.Item(0);
    }
    catch (Exception ex) {
      Trace.TraceError($"Failed to locate global sym for file {debugFilePath}: {ex.Message}");
      loadFailed_ = true;
      return false;
    }

    if (other is PDBDebugInfoProvider otherPdb) {
      // Copy the already loaded function list from another PDB
      // provider that was created on another thread and is unusable otherwise.
      lock (this) {
        symbolCache_ = otherPdb.symbolCache_;
        sortedFuncList_ = otherPdb.sortedFuncList_;
      }
    }

    return true;
  }

  public void Unload() {
    if (globalSymbol_ != null) {
      Marshal.ReleaseComObject(globalSymbol_);
      globalSymbol_ = null;
    }

    if (session_ != null) {
      Marshal.ReleaseComObject(session_);
      session_ = null;
    }

    if (diaSource_ != null) {
      Marshal.ReleaseComObject(diaSource_);
      diaSource_ = null;
    }
  }

  public bool AnnotateSourceLocations(FunctionIR function, IRTextFunction textFunc) {
    return AnnotateSourceLocations(function, textFunc.Name);
  }

  public bool AnnotateSourceLocations(FunctionIR function, FunctionDebugInfo funcDebugInfo) {
    var funcSymbol = FindFunctionSymbolByRVA(funcDebugInfo.RVA);

    if (funcSymbol == null) {
      return false;
    }

    return AnnotateSourceLocationsImpl(function, funcSymbol);
  }

  public bool AnnotateSourceLocations(FunctionIR function, string functionName) {
    var funcSymbol = FindFunctionSymbol(functionName);

    if (funcSymbol == null) {
      return false;
    }

    return AnnotateSourceLocationsImpl(function, funcSymbol);
  }

  private bool AnnotateSourceLocationsImpl(FunctionIR function, IDiaSymbol funcSymbol) {
    var metadataTag = function.GetTag<AssemblyMetadataTag>();

    if (metadataTag == null) {
      return false;
    }

    uint funcRVA = funcSymbol.relativeVirtualAddress;

    foreach (var pair in metadataTag.OffsetToElementMap) {
      uint instrRVA = funcRVA + (uint)pair.Key;
      AnnotateInstructionSourceLocation(pair.Value, instrRVA, funcSymbol);
    }

    return true;
  }

  public SourceFileDebugInfo FindFunctionSourceFilePath(IRTextFunction textFunc) {
    return FindFunctionSourceFilePath(textFunc.Name);
  }

  public SourceFileDebugInfo FindSourceFilePathByRVA(long rva) {
    // Find the first line in the function.
    if (sourceFileByRvaCache_.TryGetValue(rva, out var fileInfo)) {
      return fileInfo;
    }

    var (lineInfo, sourceFile) = FindSourceLineByRVAImpl(rva);
    fileInfo = FindFunctionSourceFilePathImpl(lineInfo, sourceFile, (uint)rva);

    sourceFileByRvaCache_.TryAdd(rva, fileInfo);
    return fileInfo;
  }

  public SourceFileDebugInfo FindFunctionSourceFilePath(string functionName) {
    if (sourceFileByNameCache_.TryGetValue(functionName, out var fileInfo)) {
      return fileInfo;
    }

    var funcSymbol = FindFunctionSymbol(functionName);

    if (funcSymbol == null) {
      return SourceFileDebugInfo.Unknown;
    }

    // Find the first line in the function.
    var (lineInfo, sourceFile) = FindSourceLineByRVAImpl(funcSymbol.relativeVirtualAddress);
    fileInfo = FindFunctionSourceFilePathImpl(lineInfo, sourceFile, funcSymbol.relativeVirtualAddress);
    sourceFileByNameCache_.TryAdd(functionName, fileInfo);
    return fileInfo;
  }

  public SourceLineDebugInfo FindSourceLineByRVA(long rva, bool includeInlinees) {
    return FindSourceLineByRVAImpl(rva, includeInlinees).Item1;
  }

  public FunctionDebugInfo FindFunctionByRVA(long rva) {
    try {
      if (sortedFuncList_ != null) {
        return FunctionDebugInfo.BinarySearch(sortedFuncList_, rva);
      }

      // Preload the function list only when there are enough queries
      // to justify the time spent in reading the entire PDB.
      if (Interlocked.Increment(ref funcCacheMisses_) >= FunctionCacheMissThreshold) {
        var funcList = GetSortedFunctions();

        if (funcList != null) {
          return FunctionDebugInfo.BinarySearch(funcList, rva);
        }
      }

      var symbol = FindFunctionSymbolByRVA(rva);

      if (symbol != null) {
        return new FunctionDebugInfo(symbol.name, symbol.relativeVirtualAddress, (long)symbol.length);
      }
    }
    catch (Exception ex) {
      Trace.TraceError($"Failed to find function for RVA {rva}: {ex.Message}");
    }

    return null;
  }

  public FunctionDebugInfo FindFunction(string functionName) {
    var funcSym = FindFunctionSymbol(functionName);
    return funcSym != null ? new FunctionDebugInfo(funcSym.name, funcSym.relativeVirtualAddress, (long)funcSym.length)
      : null;
  }

  public IEnumerable<FunctionDebugInfo> EnumerateFunctions() {
    return GetSortedFunctions();
  }

  public List<FunctionDebugInfo> GetSortedFunctions() {
    if (sortedFuncList_ != null) {
      return sortedFuncList_;
    }

    lock (this) {
      return Utils.RunSync(GetSortedFunctionsAsync);
    }
  }

  private async Task<List<FunctionDebugInfo>> GetSortedFunctionsAsync() {
    // This method assumes lock is taken by caller.
    if (sortedFuncList_ != null) {
      return sortedFuncList_;
    }

    if (settings_.CacheSymbolFiles) {
      symbolCache_ = await SymbolFileCache.DeserializeAsync(symbolFile_, settings_.SymbolCacheDirectoryPath).
        ConfigureAwait(false);
    }

    if (symbolCache_ != null) {
      Trace.WriteLine($"PDB cache loaded for {symbolFile_.FileName}");
      sortedFuncList_ = symbolCache_.FunctionList;
    }
    else {
      // Create sorted list of functions and public symbols.
      var funcList = CollectFunctionDebugInfo();

      if (funcList == null) {
        sortedFuncList_ = null;
        return null;
      }

      funcList.Sort();
      Interlocked.MemoryBarrier();
      sortedFuncList_ = funcList;

      if (settings_.CacheSymbolFiles) {
        // Save symbol cache file.
        symbolCache_ = new SymbolFileCache() {
          SymbolFile = symbolFile_,
          FunctionList = sortedFuncList_
        };

        await SymbolFileCache.SerializeAsync(symbolCache_, settings_.SymbolCacheDirectoryPath).
          ConfigureAwait(false);
        Trace.WriteLine($"PDB cache created for {symbolFile_.FileName}");
      }
    }

    return sortedFuncList_;
  }

  public void Dispose() {
    Dispose(true);
    GC.SuppressFinalize(this);
  }

  private void Dispose(bool disposing) {
    if (disposed_) {
      return;
    }

    if (disposing) {
      symbolReaderPDB_?.Dispose();
      symbolReader_?.Dispose();
      symbolReaderLog_?.Dispose();
      symbolReaderPDB_ = null;
      symbolReader_ = null;
      symbolCache_ = null;
      sortedFuncList_ = null;
    }

    Unload();
    disposed_ = true;
  }

  private bool EnsureLoaded() {
    if (session_ != null) {
      return true;
    }

    return LoadDebugInfo(debugFilePath_);
  }

  private SourceFileDebugInfo FindFunctionSourceFilePathImpl(SourceLineDebugInfo lineInfo,
                                                             IDiaSourceFile sourceFile, uint rva) {
    if (lineInfo.IsUnknown) {
      return SourceFileDebugInfo.Unknown;
    }

    string originalFilePath = lineInfo.FilePath;
    string localFilePath = lineInfo.FilePath;
    bool localFileFound = File.Exists(localFilePath);
    bool hasChecksumMismatch = false;

    if (localFileFound) {
      // Check if the PDB file checksum matches the one of the local file.
      hasChecksumMismatch = !SourceFileChecksumMatchesPDB(sourceFile, localFilePath);
    }

    // Try to use the source server if no exact local file found.
    if ((!localFileFound || hasChecksumMismatch) && settings_.SourceServerEnabled) {
      try {
        lock (this) {
          if (symbolReaderPDB_ == null) {
            var authHandler = new BasicAuthenticationHandler(settings_);
            symbolReaderLog_ = new StringWriter();
            symbolReader_ = new SymbolReader(symbolReaderLog_, null, authHandler);
            symbolReader_.SecurityCheck += s => true; // Allow symbols from "unsafe" locations.
            symbolReaderPDB_ = symbolReader_.OpenNativeSymbolFile(debugFilePath_);

            if (symbolReaderPDB_ == null) {
              Trace.WriteLine($"Failed to initialize SymbolReader for {lineInfo.FilePath}");
              return new SourceFileDebugInfo(localFilePath, originalFilePath, lineInfo.Line, hasChecksumMismatch);
            }
          }

          // Query for the source file location on the server.
          var sourceLine = symbolReaderPDB_.SourceLocationForRva(rva);

          if (sourceLine?.SourceFile != null) {
            // Try to download the source file.
            // The checksum should match, but do a check just in case.
            Trace.WriteLine($"Query source server for {sourceLine?.SourceFile?.BuildTimeFilePath}");
            string filePath = sourceLine.SourceFile.GetSourceFile();

            if (ValidateDownloadedSourceFile(filePath)) {
              Trace.WriteLine($"Downloaded source file {filePath}");
              localFilePath = filePath;
              hasChecksumMismatch = !SourceFileChecksumMatchesPDB(sourceFile, localFilePath);
            }
            else {
              Trace.WriteLine($"Failed to download source file {localFilePath}");
              Trace.WriteLine(symbolReaderLog_.ToString());
              symbolReaderLog_.GetStringBuilder().Clear();
              Trace.WriteLine("---------------------------------");
            }
          }
        }
      }
      catch (Exception ex) {
        Trace.TraceError($"Failed to locate source file for {debugFilePath_}: {ex.Message}");
      }
    }

    return new SourceFileDebugInfo(localFilePath, originalFilePath, lineInfo.Line, hasChecksumMismatch);
  }

  private bool ValidateDownloadedSourceFile(string filePath) {
    try {
      if (!File.Exists(filePath)) {
        return false;
      }

      // If the source server requires authentication, but it's not properly set up,
      // usually an HTML error page is returned instead, treat it as a failure.
      //? TODO: Better way to detect this, may need change in TraceEvent lib.
      string fileText = File.ReadAllText(filePath);
      return !fileText.Contains(@"<!DOCTYPE html");
    }
    catch (Exception ex) {
      Trace.WriteLine($"Failed to validate downloaded source file {filePath}: {ex.Message}");
      return false;
    }
  }

  private bool SourceFileChecksumMatchesPDB(IDiaSourceFile sourceFile, string filePath) {
    var hashAlgo = GetSourceFileChecksumHashAlgorithm(sourceFile);
    byte[] pdbChecksum = GetSourceFileChecksum(sourceFile);
    byte[] fileChecksum = ComputeSourceFileChecksum(filePath, hashAlgo);
    return pdbChecksum != null && fileChecksum != null &&
           pdbChecksum.SequenceEqual(fileChecksum);
  }

  private (SourceLineDebugInfo, IDiaSourceFile)
    FindSourceLineByRVAImpl(long rva, bool includeInlinees = false) {
    if (!EnsureLoaded()) {
      return (SourceLineDebugInfo.Unknown, null);
    }

    try {
      session_.findLinesByRVA((uint)rva, 0, out var lineEnum);

      while (true) {
        lineEnum.Next(1, out var lineNumber, out uint retrieved);

        if (retrieved == 0) {
          break;
        }

        var sourceFile = lineNumber.sourceFile;
        var sourceLine = new SourceLineDebugInfo((int)lineNumber.addressOffset,
                                                 (int)lineNumber.lineNumber,
                                                 (int)lineNumber.columnNumber,
                                                 sourceFile.fileName);

        if (includeInlinees) {
          var funcSymbol = FindFunctionSymbolByRVA(rva);

          if (funcSymbol != null) {
            // Enumerate the functions that got inlined at this call site.
            foreach (var inlinee in EnumerateInlinees(funcSymbol, (uint)rva)) {
              if (string.IsNullOrEmpty(inlinee.FilePath)) {
                // If the file name is not set, it means it's the same file
                // as the function into which the inlining happened.
                inlinee.FilePath = sourceFile.fileName;
              }

              sourceLine.AddInlinee(inlinee);
            }
          }
        }

        return (sourceLine, sourceFile);
      }
    }
    catch (Exception ex) {
      Trace.TraceError($"Failed to get line for RVA {rva}: {ex.Message}");
    }

    return (SourceLineDebugInfo.Unknown, null);
  }

  private HashAlgorithm GetSourceFileChecksumHashAlgorithm(IDiaSourceFile sourceFile) {
    return sourceFile.checksumType switch {
      1 => MD5.Create(),
      2 => SHA1.Create(),
      3 => SHA256.Create(),
      _ => MD5.Create()
    };
  }

  private unsafe byte[] GetSourceFileChecksum(IDiaSourceFile sourceFile) {
    // Call once to get the size of the hash.
    byte* dummy = null;
    sourceFile.get_checksum(0, out uint hashSizeInBytes, out *dummy);

    // Allocate buffer and get the actual hash.
    byte[] hash = new byte[hashSizeInBytes];

    fixed (byte* bufferPtr = hash) {
      sourceFile.get_checksum((uint)hash.Length, out uint bytesFetched, out *bufferPtr);
    }

    return hash;
  }

  private byte[] ComputeSourceFileChecksum(string sourceFile, HashAlgorithm hashAlgo) {
    try {
      using var stream = File.OpenRead(sourceFile);
      return hashAlgo.ComputeHash(stream);
    }
    catch (Exception ex) {
      Trace.TraceError($"Failed to compute hash for {sourceFile}: {ex.Message}");
      return Array.Empty<byte>();
    }
  }

  private bool AnnotateInstructionSourceLocation(IRElement instr, uint instrRVA, IDiaSymbol funcSymbol) {
    if (!EnsureLoaded()) {
      return false;
    }

    try {
      session_.findLinesByRVA(instrRVA, 0, out var lineEnum);

      while (true) {
        lineEnum.Next(1, out var lineNumber, out uint retrieved);

        if (retrieved == 0) {
          break;
        }

        var locationTag = instr.GetOrAddTag<SourceLocationTag>();
        locationTag.Reset(); // Tag may be already populated.
        locationTag.Line = (int)lineNumber.lineNumber;
        locationTag.Column = (int)lineNumber.columnNumber;

        if (lineNumber.sourceFile != null) {
          locationTag.FilePath = string.Intern(lineNumber.sourceFile.fileName);
        }

        // Enumerate the functions that got inlined at this call site.
        foreach (var inlinee in EnumerateInlinees(funcSymbol, instrRVA)) {
          if (string.IsNullOrEmpty(inlinee.FilePath)) {
            // If the file name is not set, it means it's the same file
            // as the function into which the inlining happened.
            inlinee.FilePath = locationTag.FilePath;
          }

          locationTag.AddInlinee(inlinee);
        }
      }

      Marshal.ReleaseComObject(lineEnum);
    }
    catch (Exception ex) {
      Trace.TraceError($"Failed to get source lines for {funcSymbol.name}: {ex.Message}");
      return false;
    }

    return true;
  }

  private IEnumerable<SourceStackFrame>
    EnumerateInlinees(IDiaSymbol funcSymbol, uint instrRVA) {
    if (inlineeByRvaCache_.TryGetValue(instrRVA, out var inlineeList)) {
      // Use the preloaded list of inlinees.
      foreach (var inlinee in inlineeList) {
        yield return inlinee;
      }
    }
    else {
      inlineeList = new List<SourceStackFrame>();
      funcSymbol.findInlineFramesByRVA(instrRVA, out var inlineeFrameEnum);

      foreach (IDiaSymbol inlineFrame in inlineeFrameEnum) {
        inlineFrame.findInlineeLinesByRVA(instrRVA, 0, out var inlineeLineEnum);

        while (true) {
          inlineeLineEnum.Next(1, out var inlineeLineNumber, out uint inlineeRetrieved);

          if (inlineeRetrieved == 0) {
            break;
          }

          // Getting the source file of the inlinee often fails, ignore it.
          string inlineeFileName = null;

          try {
            inlineeFileName = inlineeLineNumber.sourceFile.fileName;
          }
          catch {
            //? TODO: Any way to detect this and avoid throwing?
          }

          var inlinee = new SourceStackFrame(
            inlineFrame.name, inlineeFileName,
            (int)inlineeLineNumber.lineNumber,
            (int)inlineeLineNumber.columnNumber);
          inlineeList.Add(inlinee);
          yield return inlinee;
        }

        Marshal.ReleaseComObject(inlineeLineEnum);
      }

      Marshal.ReleaseComObject(inlineeFrameEnum);
      inlineeByRvaCache_.TryAdd(instrRVA, inlineeList);
    }
  }

  public bool PopulateSourceLines(FunctionDebugInfo funcInfo) {
    if (funcInfo.HasSourceLines) {
      return true; // Already populated.
    }

    if (!EnsureLoaded()) {
      return false;
    }

    try {
      session_.findLinesByRVA((uint)funcInfo.StartRVA, (uint)funcInfo.Size, out var lineEnum);

      while (true) {
        lineEnum.Next(1, out var lineNumber, out uint retrieved);

        if (retrieved == 0) {
          break;
        }

        funcInfo.AddSourceLine(new SourceLineDebugInfo(
                                 (int)lineNumber.addressOffset,
                                 (int)lineNumber.lineNumber,
                                 (int)lineNumber.columnNumber));
      }

      return true;
    }
    catch (Exception ex) {
      Trace.TraceError($"Failed to populate source lines for {funcInfo.Name}: {ex.Message}");
      return false;
    }
  }

  private List<FunctionDebugInfo> CollectFunctionDebugInfo() {
    if (!EnsureLoaded()) {
      return null;
    }

    List<FunctionDebugInfo> funcSymbols = null;
    IDiaEnumSymbols symbolEnum = null;
    IDiaEnumSymbols publicSymbolEnum = null;

    try {
      globalSymbol_.findChildren(SymTagEnum.SymTagFunction, null, 0, out symbolEnum);
      globalSymbol_.findChildren(SymTagEnum.SymTagPublicSymbol, null, 0, out publicSymbolEnum);
      funcSymbols = new List<FunctionDebugInfo>(symbolEnum.count + publicSymbolEnum.count);

      foreach (IDiaSymbol sym in symbolEnum) {
        //Trace.WriteLine($" FuncSym {sym.name}: RVA {sym.relativeVirtualAddress:X}, size {sym.length}");
        var funcInfo = new FunctionDebugInfo(sym.name, sym.relativeVirtualAddress, (long)sym.length);
        funcSymbols.Add(funcInfo);
      }

      foreach (IDiaSymbol sym in publicSymbolEnum) {
        //Trace.WriteLine($" PublicSym {sym.name}: RVA {sym.relativeVirtualAddress:X} size {sym.length}");
        var funcInfo = new FunctionDebugInfo(sym.name, sym.relativeVirtualAddress, (long)sym.length);
        funcSymbols.Add(funcInfo);
      }
    }
    catch (Exception ex) {
      Trace.TraceError($"Failed to enumerate functions: {ex.Message}");
    }
    finally {
      if (symbolEnum != null) {
        Marshal.ReleaseComObject(symbolEnum);
      }

      if (publicSymbolEnum != null) {
        Marshal.ReleaseComObject(publicSymbolEnum);
      }
    }

    return funcSymbols;
  }

  private IDiaSymbol FindFunctionSymbol(string functionName) {
    string demangledName = DemangleFunctionName(functionName);
    string queryDemangledName = DemangleFunctionName(functionName, FunctionNameDemanglingOptions.OnlyName);
    var result = FindFunctionSymbolImpl(SymTagEnum.SymTagFunction, functionName, demangledName, queryDemangledName);

    if (result != null) {
      return result;
    }

    return FindFunctionSymbolImpl(SymTagEnum.SymTagPublicSymbol, functionName, demangledName, queryDemangledName);
  }

  private IDiaSymbol FindFunctionSymbolByRVA(long rva) {
    if (!EnsureLoaded()) {
      return null;
    }

    try {
      session_.findSymbolByRVA((uint)rva, SymTagEnum.SymTagFunction, out var funcSym);

      if (funcSym != null) {
        return funcSym;
      }

      session_.findSymbolByRVA((uint)rva, SymTagEnum.SymTagPublicSymbol, out var pubSym);

      if (pubSym != null) {
        return pubSym;
      }
    }
    catch (Exception ex) {
      Trace.TraceError($"Failed to find function symbol for RVA {rva}: {ex.Message}");
    }

    return null;
  }

  private IDiaSymbol FindFunctionSymbolImpl(SymTagEnum symbolType, string functionName, string demangledName,
                                            string queryDemangledName) {
    if (!EnsureLoaded()) {
      return null;
    }

    try {
      globalSymbol_.findChildren(symbolType, queryDemangledName, 0, out var symbolEnum);
      IDiaSymbol candidateSymbol = null;

      while (true) {
        symbolEnum.Next(1, out var symbol, out uint retrieved);

        if (retrieved == 0) {
          break;
        }

        // Class::function matches, now check the entire unmangled name
        // to pick that right function overload.
        candidateSymbol ??= symbol;
        symbol.get_undecoratedNameEx((uint)NativeMethods.UnDecorateFlags.UNDNAME_NO_ACCESS_SPECIFIERS,
                                     out string symbolDemangledName);

        if (symbolDemangledName == demangledName) {
          return symbol;
        }
      }

      return candidateSymbol; // Return first match.
    }
    catch (Exception ex) {
      Trace.TraceError($"Failed to get function symbol for {functionName}: {ex.Message}");
      return null;
    }
  }
}

sealed class BasicAuthenticationHandler : MessageProcessingHandler {
  private SymbolFileSourceSettings settings_;

  public BasicAuthenticationHandler(SymbolFileSourceSettings settings) {
    settings_ = settings;
    InnerHandler = new HttpClientHandler();
  }

  protected override HttpRequestMessage
    ProcessRequest(HttpRequestMessage request, CancellationToken cancellationToken) {
    Trace.WriteLine($"HTTP request: {request.RequestUri}, host: {request.RequestUri.Host}");

    if (settings_.AuthorizationTokenEnabled) {
      string username = settings_.AuthorizationUser;
      string pat = settings_.AuthorizationToken;
      Trace.WriteLine($"Using PAT for user {username}, token {new string('*', pat.Length)}");

      string headerValue = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{pat}"));
      request.Headers.Add("Authorization", $"Basic {headerValue}");
    }

    return request;
  }

  protected override HttpResponseMessage ProcessResponse(HttpResponseMessage response,
                                                         CancellationToken cancellationToken) {
    return response;
  }
}
