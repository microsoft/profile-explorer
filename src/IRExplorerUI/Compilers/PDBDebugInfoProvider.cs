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

  //? TODO: Save cache between sessions, including the unavailable PDBs.
  //? Invalidate unavailable ones if SymbolOption paths change so they get a chance
  //? to be searched for in new locations.
  private static ConcurrentDictionary<SymbolFileDescriptor, DebugFileSearchResult> resolvedSymbolsCache_ =
    new ConcurrentDictionary<SymbolFileDescriptor, DebugFileSearchResult>();
  private static object undecorateLock_ = new object();

  private SymbolFileSourceSettings settings_;
  private string debugFilePath_;
  private IDiaDataSource diaSource_;
  private IDiaSession session_;
  private IDiaSymbol globalSymbol_;
  private List<FunctionDebugInfo> sortedFunctionList_;
  private bool loadFailed_;
  private bool disposed_;
  private int creationThreadId_;

  public PDBDebugInfoProvider(SymbolFileSourceSettings settings) {
    settings_ = settings;
    creationThreadId_ = Thread.CurrentThread.ManagedThreadId;
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

    return LoadDebugInfo(debugFile.FilePath, other);
  }

  public bool LoadDebugInfo(string debugFilePath, IDebugInfoProvider other = null) {
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
        sortedFunctionList_ = otherPdb.sortedFunctionList_;
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

  public bool AnnotateSourceLocations(FunctionIR function, string functionName) {
    var metadataTag = function.GetTag<AssemblyMetadataTag>();

    if (metadataTag == null) {
      return false;
    }

    var funcSymbol = FindFunctionSymbol(functionName);

    if (funcSymbol == null) {
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
    var (lineInfo, sourceFile) = FindSourceLineByRVAImpl(rva);
    return FindFunctionSourceFilePathImpl(lineInfo, sourceFile, (uint)rva);
  }

  public SourceFileDebugInfo FindFunctionSourceFilePath(string functionName) {
    var funcSymbol = FindFunctionSymbol(functionName);

    if (funcSymbol == null) {
      return SourceFileDebugInfo.Unknown;
    }

    // Find the first line in the function.
    var (lineInfo, sourceFile) = FindSourceLineByRVAImpl(funcSymbol.relativeVirtualAddress);
    return FindFunctionSourceFilePathImpl(lineInfo, sourceFile, funcSymbol.relativeVirtualAddress);
  }

  public SourceLineDebugInfo FindSourceLineByRVA(long rva) {
    return FindSourceLineByRVAImpl(rva).Item1;
  }

  public FunctionDebugInfo FindFunctionByRVA(long rva) {
    if (!EnsureLoaded()) {
      return null;
    }

    try {
      session_.findSymbolByRVA((uint)rva, SymTagEnum.SymTagFunction, out var funcSym);

      if (funcSym != null) {
        return new FunctionDebugInfo(funcSym.name, funcSym.relativeVirtualAddress, (long)funcSym.length);
      }

      // Do another lookup as a public symbol.
      session_.findSymbolByRVA((uint)rva, SymTagEnum.SymTagPublicSymbol, out var funcSym2);

      if (funcSym2 != null) {
        return new FunctionDebugInfo(funcSym2.name, funcSym2.relativeVirtualAddress, (long)funcSym2.length);
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
    // Use cached list if available.
    lock (this) {
      if (sortedFunctionList_ != null) {
        return sortedFunctionList_;
      }
    }

    return EnumerateFunctionsImpl();
  }

  public List<FunctionDebugInfo> GetSortedFunctions() {
    lock (this) {
      if (sortedFunctionList_ != null) {
        return sortedFunctionList_;
      }

      sortedFunctionList_ = new List<FunctionDebugInfo>();

      foreach (var funcInfo in EnumerateFunctionsImpl()) {
        if (!funcInfo.IsUnknown) {
          sortedFunctionList_.Add(funcInfo);
        }
      }

      sortedFunctionList_.Sort();
      return sortedFunctionList_;
    }
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
      sortedFunctionList_ = null;
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
        using var logWriter = new StringWriter();
        using var authHandler = new BasicAuthenticationHandler(settings_);
        using var symbolReader = new SymbolReader(logWriter, null, authHandler);
        symbolReader.SecurityCheck += s => true; // Allow symbols from "unsafe" locations.
        using var pdb = symbolReader.OpenNativeSymbolFile(debugFilePath_);
        var sourceLine = pdb.SourceLocationForRva(rva);

        Trace.WriteLine($"Query source server for {sourceLine?.SourceFile?.BuildTimeFilePath}");

        if (sourceLine?.SourceFile != null) {
          // Download the source file.
          // The checksum should match, but do a check just in case.
          string filePath = sourceLine.SourceFile.GetSourceFile();

          if (File.Exists(filePath)) {
            Trace.WriteLine($"Downloaded source file {filePath}");
            localFilePath = filePath;
            hasChecksumMismatch = !SourceFileChecksumMatchesPDB(sourceFile, localFilePath);
          }
          else {
            Trace.WriteLine($"Failed to download source file {filePath}");
            Trace.WriteLine(logWriter.ToString());
            Trace.WriteLine("---------------------------------");
          }
        }
      }
      catch (Exception ex) {
        Trace.TraceError($"Failed to locate source file for {debugFilePath_}: {ex.Message}");
      }
    }

    return new SourceFileDebugInfo(localFilePath, originalFilePath, lineInfo.Line, hasChecksumMismatch);
  }

  private bool SourceFileChecksumMatchesPDB(IDiaSourceFile sourceFile, string filePath) {
    var hashAlgo = GetSourceFileChecksumHashAlgorithm(sourceFile);
    byte[] pdbChecksum = GetSourceFileChecksum(sourceFile);
    byte[] fileChecksum = ComputeSourceFileChecksum(filePath, hashAlgo);
    return pdbChecksum != null && fileChecksum != null &&
           pdbChecksum.SequenceEqual(fileChecksum);
  }

  private (SourceLineDebugInfo, IDiaSourceFile) FindSourceLineByRVAImpl(long rva) {
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
        return (new SourceLineDebugInfo((int)lineNumber.addressOffset, (int)lineNumber.lineNumber,
                                        (int)lineNumber.columnNumber, sourceFile.fileName), sourceFile);
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
        funcSymbol.findInlineFramesByRVA(instrRVA, out var inlineeFrameEnum);

        foreach (IDiaSymbol inlineFrame in inlineeFrameEnum) {
          inlineFrame.findInlineeLinesByRVA(instrRVA, 0, out var inlineeLineEnum);

          while (true) {
            inlineeLineEnum.Next(1, out var inlineeLineNumber, out uint inlineeRetrieved);

            if (inlineeRetrieved == 0) {
              break;
            }

            // Getting the source file of the inlinee often fails, ignore it.
            string inlineeFileName = "";

            try {
              inlineeFileName = inlineeLineNumber.sourceFile.fileName;
            }
            catch {
              // If the file name is not set, it means it's the same file
              // as the function into which the inlining happened.
              inlineeFileName = locationTag.FilePath;
            }

            locationTag.AddInlinee(inlineFrame.name, inlineeFileName,
                                   (int)inlineeLineNumber.lineNumber,
                                   (int)inlineeLineNumber.columnNumber);
          }
        }
      }
    }
    catch (Exception ex) {
      Trace.TraceError($"Failed to get source lines for {funcSymbol.name}: {ex.Message}");
      return false;
    }

    return true;
  }

  public bool PopulateSourceLines(FunctionDebugInfo funcInfo) {
    if (funcInfo.HasSourceLines) {
      return true; // Already populated.
    }

    lock (funcInfo) {
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
  }

  private IEnumerable<FunctionDebugInfo> EnumerateFunctionsImpl() {
    if (!EnsureLoaded()) {
      yield break;
    }

    IDiaEnumSymbols symbolEnum;

    try {
      globalSymbol_.findChildren(SymTagEnum.SymTagFunction, null, 0, out symbolEnum);
    }
    catch (Exception ex) {
      Trace.TraceError($"Failed to enumerate functions: {ex.Message}");
      yield break;
    }

    foreach (IDiaSymbol sym in symbolEnum) {
      //Trace.WriteLine($" FuncSym {sym.name}: RVA {sym.relativeVirtualAddress:X}, size {sym.length}");
      var funcInfo = new FunctionDebugInfo(sym.name, sym.relativeVirtualAddress, (long)sym.length);
      yield return funcInfo;
    }

    // Functions also show up as public symbols.
    IDiaEnumSymbols publicSymbolEnum;

    try {
      globalSymbol_.findChildren(SymTagEnum.SymTagPublicSymbol, null, 0, out publicSymbolEnum);
    }
    catch (Exception ex) {
      Trace.TraceError($"Failed to enumerate functions: {ex.Message}");
      yield break;
    }

    foreach (IDiaSymbol sym in publicSymbolEnum) {
      //Trace.WriteLine($" PublicSym {sym.name}: RVA {sym.relativeVirtualAddress:X} size {sym.length}");
      yield return new FunctionDebugInfo(sym.name, sym.relativeVirtualAddress, (long)sym.length);
    }
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

  private IDiaSymbol FindFunctionSymbolImpl(SymTagEnum symbolType, string functionName, string demangledName,
                                            string queryDemangledName) {
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

  protected override HttpRequestMessage ProcessRequest(HttpRequestMessage request, CancellationToken cancellationToken) {
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

  protected override HttpResponseMessage ProcessResponse(HttpResponseMessage response, CancellationToken cancellationToken) {
    return response;
  }
}