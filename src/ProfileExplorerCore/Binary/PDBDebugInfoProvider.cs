// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;
using Dia2Lib;
using Microsoft.Diagnostics.Symbols;
using Microsoft.Diagnostics.Symbols.Authentication;
using ProfileExplorer.Core.IR;
using ProfileExplorer.Core.IR.Tags;
using ProfileExplorer.Core.Providers;
using ProfileExplorer.Core.Settings;
using ProfileExplorer.Core.Utilities;
using StringWriter = System.IO.StringWriter;

namespace ProfileExplorer.Core.Binary;

public sealed class PDBDebugInfoProvider : IDebugInfoProvider {
  private const int MaxDemangledFunctionNameLength = 8192;
  private const int FunctionCacheMissThreshold = 100;
  private const int MaxLogEntryLength = 10240;

  private static ConcurrentDictionary<SymbolFileDescriptor, DebugFileSearchResult> resolvedSymbolsCache_ = new();
  private static readonly StringWriter authLogWriter_;
  private static readonly SymwebHandler authSymwebHandler_;
  private static object undecorateLock_ = new(); // Global lock for undname.
  private ConcurrentDictionary<long, SourceFileDebugInfo> sourceFileByRvaCache_ = new();
  private ConcurrentDictionary<string, SourceFileDebugInfo> sourceFileByNameCache_ = new();
  private ConcurrentDictionary<uint, List<SourceStackFrame>> inlineeByRvaCache_ = new();
  private ConcurrentDictionary<long, bool> loggedRvas_ = new();
  private object cacheLock_ = new();
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
  private bool sortedFuncListOverlapping_;
  private volatile int funcCacheMisses_;
  private bool loadFailed_;
  private bool disposed_;
  private bool hasSourceInfo_;

  // Static error tracking for DIA SDK registration issues
  private static bool diaRegistrationFailed_;
  private static string diaRegistrationError_;

  /// <summary>
  /// Returns true if DIA SDK (msdia140.dll) failed to load due to COM registration issues.
  /// </summary>
  public static bool HasDiaRegistrationError => diaRegistrationFailed_;

  /// <summary>
  /// Gets the DIA registration error message with instructions to fix.
  /// </summary>
  public static string DiaRegistrationError => diaRegistrationError_;

  static PDBDebugInfoProvider() {
    // Create a single instance of the Symweb handler so that
    // when concurrent requests are made and login must be done,
    // the login page is displayed a single time, with other requests waiting for a token.

    // DefaultAzureCredential is not allowed per SFI. Mimic behavior while continuing
    // to exclude the managed identity credential.
    // NOTE: ChainedTokenCredential only falls through on CredentialUnavailableException.
    // Other exceptions (like "needs re-authentication") stop the chain. We wrap each
    // credential to catch auth failures and convert them to CredentialUnavailableException
    // so the chain continues to InteractiveBrowserCredential which prompts the user.
    // Enable token cache persistence for browser credential so tokens survive process restarts.
    // This prevents re-authentication on every trace load if VS credential fails.
    var browserCredentialOptions = new InteractiveBrowserCredentialOptions {
      TokenCachePersistenceOptions = new TokenCachePersistenceOptions {
        Name = "ProfileExplorer" // Unique cache name for this app
      }
    };

    var credentials = new List<TokenCredential>
    {
      WrapCredential(new EnvironmentCredential()),
      WrapCredential(new WorkloadIdentityCredential()),
      WrapCredential(new SharedTokenCacheCredential()),
      WrapCredential(new VisualStudioCredential()),
      WrapCredential(new AzureCliCredential()),
      WrapCredential(new AzurePowerShellCredential()),
      WrapCredential(new AzureDeveloperCliCredential()),
      new InteractiveBrowserCredential(browserCredentialOptions) // Don't wrap - final fallback should show errors
    };

    var authCredential = new ChainedTokenCredential(credentials.ToArray());

    authLogWriter_ = new StringWriter();
    authSymwebHandler_ = new SymwebHandler(authLogWriter_, authCredential);
  }

  /// <summary>
  /// Wraps a credential to catch authentication failures and convert them to
  /// CredentialUnavailableException so ChainedTokenCredential continues to the next credential.
  /// </summary>
  private static TokenCredential WrapCredential(TokenCredential inner) {
    return new FallbackTokenCredential(inner);
  }

  public PDBDebugInfoProvider(SymbolFileSourceSettings settings) {
    settings_ = settings;
  }

  public SymbolFileSourceSettings SymbolSettings { get; set; }
  public Machine? Architecture => null;

  public bool LoadDebugInfo(DebugFileSearchResult debugFile, IDebugInfoProvider other = null) {
    if (debugFile == null || !debugFile.Found) {
      return false;
    }

    symbolFile_ = debugFile.SymbolFile;
    return LoadDebugInfo(debugFile.FilePath, other);
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
    var funcSymbol = FindFunctionSymbolByRVA(funcDebugInfo.RVA, true);

    if (funcSymbol == null) {
      return false;
    }

    return AnnotateSourceLocationsImpl(function, funcSymbol);
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
    DiagnosticLogger.LogInfo($"[SourceFile] FindFunctionSourceFilePath called for: {functionName}");

    if (sourceFileByNameCache_.TryGetValue(functionName, out var fileInfo)) {
      DiagnosticLogger.LogInfo($"[SourceFile] Cache hit for {functionName}: {fileInfo.FilePath}");
      return fileInfo;
    }

    var funcSymbol = FindFunctionSymbol(functionName);

    if (funcSymbol == null) {
      DiagnosticLogger.LogWarning($"[SourceFile] Function symbol not found for: {functionName}");
      return SourceFileDebugInfo.Unknown;
    }

    DiagnosticLogger.LogInfo($"[SourceFile] Found function symbol for {functionName}, RVA: 0x{funcSymbol.relativeVirtualAddress:X}");

    // Find the first line in the function.
    var (lineInfo, sourceFile) = FindSourceLineByRVAImpl(funcSymbol.relativeVirtualAddress);
    DiagnosticLogger.LogInfo($"[SourceFile] Line info for {functionName}: FilePath={lineInfo.FilePath}, Line={lineInfo.Line}, IsUnknown={lineInfo.IsUnknown}");

    fileInfo = FindFunctionSourceFilePathImpl(lineInfo, sourceFile, funcSymbol.relativeVirtualAddress);
    DiagnosticLogger.LogInfo($"[SourceFile] Result for {functionName}: FilePath={fileInfo.FilePath}, OriginalFilePath={fileInfo.OriginalFilePath}, HasChecksumMismatch={fileInfo.HasChecksumMismatch}");

    sourceFileByNameCache_.TryAdd(functionName, fileInfo);
    return fileInfo;
  }

  public SourceLineDebugInfo FindSourceLineByRVA(long rva, bool includeInlinees) {
    return FindSourceLineByRVAImpl(rva, includeInlinees).Item1;
  }

  public FunctionDebugInfo FindFunctionByRVA(long rva) {
    bool shouldLog = loggedRvas_.TryAdd(rva, true); // Returns true if newly added
    string binaryName = symbolFile_?.FileName ?? "Unknown";
    
    try {
      if (sortedFuncList_ != null) {
        // Query the function list first. If not found, then still query the actual PDB
        // because DIA has special lookup for functions split into multiple chunks by PGO for ex.
        var result = FunctionDebugInfo.BinarySearch(sortedFuncList_, rva, sortedFuncListOverlapping_);

        if (result != null) {
          if (shouldLog) {
            DiagnosticLogger.LogInfo($"[PDBDebugInfo] Binary: {binaryName}, RVA: 0x{rva:X}, Function: {result.Name} (found in cache)");
          }
          return result;
        }
      }

      // Preload the function list only when there are enough queries
      // to justify the time spent in reading the entire PDB.
      if (sortedFuncList_ == null &&
          Interlocked.Increment(ref funcCacheMisses_) >= FunctionCacheMissThreshold) {
        if (shouldLog) {
          DiagnosticLogger.LogInfo($"[PDBDebugInfo] Binary: {binaryName}, cache miss threshold reached, loading function list");
        }
        GetSortedFunctions();

        if (sortedFuncList_ != null) {
          var result = FunctionDebugInfo.BinarySearch(sortedFuncList_, rva, sortedFuncListOverlapping_);

          if (result != null) {
            if (shouldLog) {
              DiagnosticLogger.LogInfo($"[PDBDebugInfo] Binary: {binaryName}, RVA: 0x{rva:X}, Function: {result.Name} (found after cache load)");
            }
            return result;
          }
        } else if (shouldLog) {
          DiagnosticLogger.LogWarning($"[PDBDebugInfo] Binary: {binaryName}, failed to load function list");
        }
      }

      // Query the PDB file.
      var symbol = FindFunctionSymbolByRVA(rva);

      if (symbol != null) {
        if (shouldLog) {
          DiagnosticLogger.LogInfo($"[PDBDebugInfo] Binary: {binaryName}, RVA: 0x{rva:X}, Function: {symbol.name} (found via PDB query)");
        }
        return new FunctionDebugInfo(symbol.name, symbol.relativeVirtualAddress, (uint)symbol.length);
      } else if (shouldLog) {
        DiagnosticLogger.LogWarning($"[PDBDebugInfo] Binary: {binaryName}, RVA: 0x{rva:X}, Function: NOT_RESOLVED (no symbol found)");
      }
    }
    catch (Exception ex) {
      if (shouldLog) {
        DiagnosticLogger.LogError($"[PDBDebugInfo] Binary: {binaryName}, RVA: 0x{rva:X}, Function: ERROR ({ex.Message})", ex);
      }
      Trace.TraceError($"Failed to find function for RVA {rva}: {ex.Message}");
    }

    return null;
  }

  public FunctionDebugInfo FindFunction(string functionName) {
    var funcSym = FindFunctionSymbol(functionName);
    return funcSym != null ? new FunctionDebugInfo(funcSym.name, funcSym.relativeVirtualAddress, (uint)funcSym.length)
      : null;
  }

  public IEnumerable<FunctionDebugInfo> EnumerateFunctions() {
    return GetSortedFunctions();
  }

  public List<FunctionDebugInfo> GetSortedFunctions() {
    if (sortedFuncList_ != null) {
      return sortedFuncList_;
    }

    lock (cacheLock_) {
      var result = Utils.RunSync(GetSortedFunctionsAsync);
#if DEBUG
      ValidateSortedList(result);
#endif
      return result;
    }
  }

  public void Dispose() {
    Dispose(true);
    GC.SuppressFinalize(this);
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

  ~PDBDebugInfoProvider() {
    Dispose(false);
  }

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

  public static SymbolReaderAuthenticationHandler CreateAuthHandler(SymbolFileSourceSettings settings) {
    var authHandler = new SymbolReaderAuthenticationHandler();
    authHandler.AddHandler(authSymwebHandler_);

    if (settings.AuthorizationTokenEnabled) {
      authHandler.AddHandler(new BasicAuthenticationHandler(settings, authLogWriter_));
    }

    return authHandler;
  }

  public static DebugFileSearchResult
    LocateDebugInfoFile(SymbolFileDescriptor symbolFile,
                        SymbolFileSourceSettings settings) {
    if (symbolFile == null) {
      return DebugFileSearchResult.None;
    }

    DiagnosticLogger.LogInfo($"[SymbolSearch] Starting symbol file search for {symbolFile.FileName} (ID: {symbolFile.Id}, Age: {symbolFile.Age})");

    if (resolvedSymbolsCache_.TryGetValue(symbolFile, out var searchResult)) {
      DiagnosticLogger.LogDebug($"[SymbolSearch] Found cached result for {symbolFile.FileName}: {(searchResult.Found ? "Found" : "Not Found")}");
      return searchResult;
    }

    // Check if this symbol file was previously rejected (failed lookup in a prior session).
    if (settings.IsRejectedSymbolFile(symbolFile)) {
      DiagnosticLogger.LogInfo($"[SymbolSearch] SKIPPED - previously rejected: {symbolFile.FileName}");
      searchResult = DebugFileSearchResult.Failure(symbolFile, "Previously rejected");
      resolvedSymbolsCache_.TryAdd(symbolFile, searchResult);
      return searchResult;
    }

    string result = null;
    using var logWriter = new StringWriter();

    // In case there is a timeout downloading the symbols, try again.
    string symbolSearchPath = ConstructSymbolSearchPath(settings);
    DiagnosticLogger.LogInfo($"[SymbolSearch] Symbol search path: {symbolSearchPath}");

    using var symbolReader = new SymbolReader(logWriter, symbolSearchPath, CreateAuthHandler(settings));
    symbolReader.SecurityCheck += s => true; // Allow symbols from "unsafe" locations.

    // Set symbol server timeout from settings (default 10 seconds).
    // Use EffectiveTimeoutSeconds which is reduced if bellwether test marked server as degraded.
    int timeoutSeconds = settings.EffectiveTimeoutSeconds > 0 ? settings.EffectiveTimeoutSeconds : 10;
    symbolReader.ServerTimeout = TimeSpan.FromSeconds(timeoutSeconds);
    DiagnosticLogger.LogInfo($"[SymbolSearch] ServerTimeout={timeoutSeconds}s for {symbolFile.FileName}");

    try {
      DiagnosticLogger.LogInfo($"[SymbolSearch] Starting PDB download/search for {symbolFile.FileName}, {symbolFile.Id}, {symbolFile.Age}");
      Trace.WriteLine($"Start PDB download for {symbolFile.FileName}, {symbolFile.Id}, {symbolFile.Age}");
      result = symbolReader.FindSymbolFilePath(symbolFile.FileName, symbolFile.Id, symbolFile.Age);
      DiagnosticLogger.LogInfo($"[SymbolSearch] FindSymbolFilePath result: {result ?? "null"}");
    }
    catch (Exception ex) {
      DiagnosticLogger.LogError($"[SymbolSearch] Exception in FindSymbolFilePath for {symbolFile.FileName}: {ex.Message}", ex);
      Trace.TraceError($"Failed FindSymbolFilePath for {symbolFile.FileName}: {ex.Message}");
    }

    // Log the detailed search information - at INFO level to help debug symbol server issues
    string searchLog = logWriter.ToString();
    if (!string.IsNullOrWhiteSpace(searchLog)) {
      DiagnosticLogger.LogInfo($"[SymbolSearch] TraceEvent log for {symbolFile.FileName}:\n{searchLog}");
    }

    // Check for auth failure on primary server, but ONLY if primary has never been verified working.
    // Once we've successfully downloaded from symweb, we NEVER fall back to msdl (would get worse symbols).
    if (!settings.PrimaryServerVerified && !settings.PrimaryServerAuthFailed && DetectPrimaryServerAuthFailure(searchLog)) {
      settings.PrimaryServerAuthFailed = true;
      DiagnosticLogger.LogWarning($"[SymbolSearch] Primary server (symweb) auth FAILED - switching to secondary (public) server for remaining downloads");
    }

#if DEBUG
    Trace.WriteLine($">> TraceEvent FindSymbolFilePath for {symbolFile.FileName}");
    Trace.IndentLevel = 1;
    Trace.WriteLine(searchLog);
    Trace.IndentLevel = 0;
    Trace.WriteLine("<< TraceEvent");
#endif

    if (!string.IsNullOrEmpty(result) && File.Exists(result)) {
      DiagnosticLogger.LogInfo($"[SymbolSearch] Successfully found symbol file for {symbolFile.FileName}: {result}");
      searchResult = DebugFileSearchResult.Success(symbolFile, result, searchLog);

      // If download succeeded and log shows symweb was used, mark primary as verified
      if (!settings.PrimaryServerVerified &&
          searchLog.Contains("symweb", StringComparison.OrdinalIgnoreCase) &&
          !DetectPrimaryServerAuthFailure(searchLog)) {
        settings.PrimaryServerVerified = true;
        DiagnosticLogger.LogInfo($"[SymbolSearch] Primary server (symweb) auth VERIFIED - will continue using primary server");
      }
    }
    else {
      DiagnosticLogger.LogWarning($"[SymbolSearch] Failed to find symbol file for {symbolFile.FileName}. Result: {result ?? "null"}");
      searchResult = DebugFileSearchResult.Failure(symbolFile, searchLog);

      // Record failed lookup to avoid retrying in future sessions.
      settings.RejectSymbolFile(symbolFile);
    }

    resolvedSymbolsCache_.TryAdd(symbolFile, searchResult);
    DiagnosticLogger.LogInfo($"[SymbolSearch] Cached search result for {symbolFile.FileName}: {(searchResult.Found ? "Success" : "Failure")}");
    return searchResult;
  }

  // Track if we've logged detailed symbol path info (only log once per session unless auth state changes)
  private static bool loggedSymbolPathDetails_;
  private static bool lastLoggedAuthFailedState_;

  public static string ConstructSymbolSearchPath(SymbolFileSourceSettings settings, bool logPath = false) {
    string symbolPath = "";

    // Only log detailed info on first call or when auth state changes
    bool authStateChanged = lastLoggedAuthFailedState_ != settings.PrimaryServerAuthFailed;
    bool shouldLogDetails = !loggedSymbolPathDetails_ || authStateChanged;

    if (shouldLogDetails) {
      DiagnosticLogger.LogInfo($"[SymbolPath] ConstructSymbolSearchPath - PrimaryServerAuthFailed={settings.PrimaryServerAuthFailed}, PrimaryServerVerified={settings.PrimaryServerVerified}");
      DiagnosticLogger.LogInfo($"[SymbolPath] Input SymbolPaths ({settings.SymbolPaths?.Count ?? 0} entries): {string.Join("; ", settings.SymbolPaths ?? [])}");
      if (authStateChanged && loggedSymbolPathDetails_) {
        DiagnosticLogger.LogWarning($"[SymbolPath] Auth state changed from {lastLoggedAuthFailedState_} to {settings.PrimaryServerAuthFailed} - switching symbol servers");
      }
      loggedSymbolPathDetails_ = true;
      lastLoggedAuthFailedState_ = settings.PrimaryServerAuthFailed;
    }

    if (settings.UseEnvironmentVarSymbolPaths) {
      symbolPath += $"{settings.EnvironmentVarSymbolPath};";
    }

    foreach (string path in settings.SymbolPaths) {
      if (!string.IsNullOrEmpty(path)) {
        // Skip primary (private) server if auth has failed
        if (settings.PrimaryServerAuthFailed && path.Contains("symweb", StringComparison.OrdinalIgnoreCase)) {
          if (shouldLogDetails) {
            DiagnosticLogger.LogInfo($"[SymbolPath] Skipping primary server (auth failed): {path}");
          }
          continue;
        }

        // Skip secondary (public) server UNLESS primary auth has explicitly failed.
        // msdl only has stripped/public PDBs - we want private symbols from symweb.
        // Only fall back to msdl if symweb auth fails (401/403).
        if (!settings.PrimaryServerAuthFailed && path.Contains("msdl.microsoft.com", StringComparison.OrdinalIgnoreCase)) {
          if (shouldLogDetails) {
            DiagnosticLogger.LogInfo($"[SymbolPath] Skipping secondary server (primary not failed): {path}");
          }
          continue;
        }

        if (shouldLogDetails) {
          DiagnosticLogger.LogInfo($"[SymbolPath] Including path: {path}");
        }
        symbolPath += $"{path};";
      }
    }

    // Always log the final path on first call or auth state change
    if (shouldLogDetails) {
      DiagnosticLogger.LogInfo($"[SymbolPath] Final constructed path: {symbolPath}");
    }

    return symbolPath;
  }

  /// <summary>
  /// Checks if the search log indicates an auth failure (401/403) on the primary server.
  /// Uses specific patterns to avoid false positives from GUIDs/paths that contain "401" or "403".
  /// </summary>
  private static bool DetectPrimaryServerAuthFailure(string searchLog) {
    if (string.IsNullOrEmpty(searchLog)) {
      return false;
    }

    // Must involve symweb to be a primary server auth failure
    if (!searchLog.Contains("symweb", StringComparison.OrdinalIgnoreCase)) {
      return false;
    }

    // Look for specific auth failure patterns - not just "401"/"403" which can match GUIDs/paths
    // TraceEvent typically outputs HTTP errors with context like "Response: 401" or "401 Unauthorized"
    // Use regex with word boundaries to avoid matching 401/403 within hex GUIDs
    var authFailurePatterns = new[] {
      @"\b401\b",           // 401 at word boundary (not in hex GUIDs like A3401B)
      @"\b403\b",           // 403 at word boundary
      "Unauthorized",       // HTTP 401 description
      "Forbidden"           // HTTP 403 description
    };

    foreach (var pattern in authFailurePatterns) {
      if (pattern.StartsWith(@"\b")) {
        // Regex pattern - check for word boundaries
        if (System.Text.RegularExpressions.Regex.IsMatch(searchLog, pattern)) {
          return true;
        }
      }
      else {
        // Simple string search for descriptive terms
        if (searchLog.Contains(pattern, StringComparison.OrdinalIgnoreCase)) {
          return true;
        }
      }
    }

    return false;
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
      // Check for DIA SDK COM registration issue - common in dev builds
      bool isDiaRegistrationError = ex.Message.Contains("E6756135-1E65-4D17-8576-610761398C3C") ||
                                    ex.Message.Contains("8007007E") ||
                                    ex.Message.Contains("class factory");
      if (isDiaRegistrationError) {
        string errorMsg = $"DIA SDK (msdia140.dll) is not registered! " +
                          $"Run as Admin: regsvr32 \"{AppContext.BaseDirectory}msdia140.dll\" " +
                          $"or use the installed version of Profile Explorer.";
        DiagnosticLogger.LogError($"[PDBLoad] [CRITICAL] {errorMsg}");
        Trace.TraceError($"DIA SDK not registered. Run: regsvr32 \"{AppContext.BaseDirectory}msdia140.dll\"");

        // Set static flag so UI can detect and display error
        if (!diaRegistrationFailed_) {
          diaRegistrationFailed_ = true;
          diaRegistrationError_ = errorMsg;
        }
      }
      else {
        DiagnosticLogger.LogError($"[PDBLoad] EXCEPTION loading PDB {debugFilePath}: {ex.GetType().Name}: {ex.Message}");
      }
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
      symbolCache_ = otherPdb.symbolCache_;
      sortedFuncList_ = otherPdb.sortedFuncList_;
    }

    // Check if PDB has source file information (not stripped)
    CheckForSourceInfo();
    return true;
  }

  private void CheckForSourceInfo() {
    // Log file size to help diagnose public vs private PDB
    try {
      var fileInfo = new FileInfo(debugFilePath_);
      DiagnosticLogger.LogInfo($"[PDBDebugInfo] PDB file size: {fileInfo.Length:N0} bytes ({fileInfo.Length / 1024.0 / 1024.0:F2} MB) - {debugFilePath_}");
    }
    catch { }

    try {
      // Try to enumerate source files in the PDB
      session_.findFile(null, null, 0, out var sourceFileEnum);
      sourceFileEnum.Next(1, out var sourceFile, out uint retrieved);

      if (retrieved > 0 && sourceFile != null) {
        string fileName = sourceFile.fileName;
        hasSourceInfo_ = true;
        DiagnosticLogger.LogInfo($"[PDBDebugInfo] PDB has source info. Sample source file: {fileName}");
      }
      else {
        hasSourceInfo_ = false;
        DiagnosticLogger.LogWarning($"[PDBDebugInfo] PDB appears to be STRIPPED (no source file info). Source file viewing will not work for: {debugFilePath_}");
        DiagnosticLogger.LogWarning($"[PDBDebugInfo] If you expected private symbols with source info:");
        DiagnosticLogger.LogWarning($"[PDBDebugInfo]   1. Delete the cached PDB: {debugFilePath_}");
        DiagnosticLogger.LogWarning($"[PDBDebugInfo]   2. Ensure symweb (private server) is configured as PRIMARY in symbol paths");
        DiagnosticLogger.LogWarning($"[PDBDebugInfo]   3. Reload the trace to re-download from the private server");
      }
    }
    catch (Exception ex) {
      hasSourceInfo_ = false;
      DiagnosticLogger.LogWarning($"[PDBDebugInfo] Could not enumerate source files in PDB: {ex.Message}. PDB may be stripped.");
    }
  }

  /// <summary>
  /// Returns true if the PDB contains source file information (line numbers, file paths).
  /// Returns false for stripped/public PDBs that only contain symbol names.
  /// </summary>
  public bool HasSourceInfo => hasSourceInfo_;

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

  private bool ValidateSortedList(List<FunctionDebugInfo> list) {
    for (int i = 1; i < list.Count; i++) {
      if (list[i].StartRVA < list[i - 1].StartRVA &&
          list[i].StartRVA != 0 &&
          list[i - 1].StartRVA != 0) {
        Debug.Assert(false, "Function list is not sorted by RVA");
        return false;
      }
    }

    return true;
  }

  private async Task<List<FunctionDebugInfo>> GetSortedFunctionsAsync() {
    // This method assumes lock is taken by caller.
    if (sortedFuncList_ != null) {
      return sortedFuncList_;
    }

    if (settings_.CacheSymbolFiles) {
      // Try to load a previous cached function list file.
      symbolCache_ = await SymbolFileCache.DeserializeAsync(symbolFile_, settings_.SymbolCacheDirectoryPath).
        ConfigureAwait(false);
    }

    if (symbolCache_ != null) {
      Trace.WriteLine($"PDB cache loaded for {symbolFile_.FileName}");
      sortedFuncList_ = symbolCache_.FunctionList;
    }
    else {
      // Create sorted list of functions and public symbols.
      sortedFuncList_ = CollectFunctionDebugInfo();

      if (sortedFuncList_ == null) {
        return null;
      }

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

    // Sorting needed for binary search later.
    sortedFuncList_.Sort();
    sortedFuncListOverlapping_ = HasOverlappingFunctions(sortedFuncList_);
    return sortedFuncList_;
  }

  private bool HasOverlappingFunctions(List<FunctionDebugInfo> sortedFuncList) {
    if (sortedFuncList == null || sortedFuncList.Count < 2) {
      return false;
    }

    for (int i = 1; i < sortedFuncList.Count; i++) {
      if (sortedFuncList[i].StartRVA == 0) {
        continue;
      }

      for (int k = i - 1; k >= 0 && i - k < 10; k--) {
        if (sortedFuncList[k].StartRVA != 0 &&
            sortedFuncList[k].StartRVA <= sortedFuncList[i].StartRVA &&
            sortedFuncList[k].EndRVA > sortedFuncList[i].EndRVA) {
          return true;
        }
      }
    }

    return false;
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
      DiagnosticLogger.LogDebug($"[PDBDebugInfo] PDB session already loaded for {symbolFile_?.FileName ?? debugFilePath_ ?? "Unknown"}");
      return true;
    }

    DiagnosticLogger.LogInfo($"[PDBDebugInfo] Loading PDB file: {debugFilePath_}");
    bool loaded = LoadDebugInfo(debugFilePath_);
    
    if (loaded) {
      DiagnosticLogger.LogInfo($"[PDBDebugInfo] Successfully loaded PDB file: {debugFilePath_}");
    } else {
      DiagnosticLogger.LogError($"[PDBDebugInfo] Failed to load PDB file: {debugFilePath_}");
    }
    
    return loaded;
  }

  private SourceFileDebugInfo FindFunctionSourceFilePathImpl(SourceLineDebugInfo lineInfo,
                                                             IDiaSourceFile sourceFile, uint rva) {
    if (lineInfo.IsUnknown) {
      DiagnosticLogger.LogWarning($"[SourceFile] lineInfo is Unknown for RVA 0x{rva:X}");
      return SourceFileDebugInfo.Unknown;
    }

    string originalFilePath = lineInfo.FilePath;
    string localFilePath = lineInfo.FilePath;
    bool localFileFound = File.Exists(localFilePath);
    bool hasChecksumMismatch = false;

    DiagnosticLogger.LogInfo($"[SourceFile] Checking source file: {originalFilePath}, localFileFound={localFileFound}");

    if (localFileFound) {
      // Check if the PDB file checksum matches the one of the local file.
      hasChecksumMismatch = !SourceFileChecksumMatchesPDB(sourceFile, localFilePath);
      DiagnosticLogger.LogInfo($"[SourceFile] Local file exists, checksumMismatch={hasChecksumMismatch}");
    }

    // Try to use the source server if no exact local file found.
    DiagnosticLogger.LogInfo($"[SourceFile] SourceServerEnabled={settings_.SourceServerEnabled}, needsServerLookup={!localFileFound || hasChecksumMismatch}");

    if ((!localFileFound || hasChecksumMismatch) && settings_.SourceServerEnabled) {
      DiagnosticLogger.LogInfo($"[SourceFile] Attempting source server lookup for RVA 0x{rva:X}");
      try {
        lock (this) {
          if (symbolReaderPDB_ == null) {
            DiagnosticLogger.LogInfo($"[SourceFile] Initializing SymbolReader for {debugFilePath_}");
            symbolReaderLog_ = new StringWriter();
            symbolReader_ = new SymbolReader(symbolReaderLog_, null, CreateAuthHandler(settings_));
            symbolReader_.SecurityCheck += s => true; // Allow symbols from "unsafe" locations.
            symbolReaderPDB_ = symbolReader_.OpenNativeSymbolFile(debugFilePath_);

            if (symbolReaderPDB_ == null) {
              DiagnosticLogger.LogError($"[SourceFile] Failed to initialize SymbolReader for {debugFilePath_}");
              Trace.WriteLine($"Failed to initialize SymbolReader for {lineInfo.FilePath}");
              return new SourceFileDebugInfo(localFilePath, originalFilePath, lineInfo.Line, hasChecksumMismatch);
            }
            DiagnosticLogger.LogInfo($"[SourceFile] SymbolReader initialized successfully");
          }

          // Query for the source file location on the server.
          DiagnosticLogger.LogInfo($"[SourceFile] Querying SourceLocationForRva(0x{rva:X})");
          var sourceLine = symbolReaderPDB_.SourceLocationForRva(rva);

          if (sourceLine?.SourceFile != null) {
            // Try to download the source file.
            // The checksum should match, but do a check just in case.
            DiagnosticLogger.LogInfo($"[SourceFile] Source server has file: {sourceLine.SourceFile.BuildTimeFilePath}");
            Trace.WriteLine($"Query source server for {sourceLine?.SourceFile?.BuildTimeFilePath}");
            string filePath = sourceLine.SourceFile.GetSourceFile();
            DiagnosticLogger.LogInfo($"[SourceFile] GetSourceFile returned: {filePath ?? "null"}");

            if (!string.IsNullOrEmpty(filePath) && SourceFileChecksumMatchesPDB(sourceFile, filePath)) {
              DiagnosticLogger.LogInfo($"[SourceFile] Downloaded and verified source file: {filePath}");
              Trace.WriteLine($"Downloaded source file {filePath}");
              localFilePath = filePath;
              hasChecksumMismatch = !SourceFileChecksumMatchesPDB(sourceFile, localFilePath);
            }
            else {
              DiagnosticLogger.LogWarning($"[SourceFile] Failed to download or verify source file. filePath={filePath ?? "null"}");
              Trace.WriteLine($"Failed to download source file {localFilePath}");
              string logContent = symbolReaderLog_.ToString().TrimToLength(MaxLogEntryLength);
              DiagnosticLogger.LogWarning($"[SourceFile] SymbolReader log: {logContent}");
              Trace.WriteLine(logContent);
              symbolReaderLog_.GetStringBuilder().Clear();
              Trace.WriteLine("---------------------------------");
            }
          }
          else {
            DiagnosticLogger.LogWarning($"[SourceFile] SourceLocationForRva returned null or no SourceFile for RVA 0x{rva:X}");
          }
        }
      }
      catch (Exception ex) {
        DiagnosticLogger.LogError($"[SourceFile] Exception during source server lookup: {ex.Message}", ex);
        Trace.TraceError($"Failed to locate source file for {debugFilePath_}: {ex.Message}");
      }
    }

    return new SourceFileDebugInfo(localFilePath, originalFilePath, lineInfo.Line, hasChecksumMismatch);
  }

  private bool SourceFileChecksumMatchesPDB(IDiaSourceFile sourceFile, string filePath) {
    if (string.IsNullOrEmpty(filePath)) {
      return false;
    }

    var hashAlgo = GetSourceFileChecksumHashAlgorithm(sourceFile);
    if (hashAlgo == null) {
      return false;
    }

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
          DiagnosticLogger.LogWarning($"[SourceFile] No line info found in PDB for RVA 0x{rva:X} - PDB may be stripped (public symbols only)");
          break;
        }

        var sourceFile = lineNumber.sourceFile;
        var sourceLine = new SourceLineDebugInfo((int)lineNumber.addressOffset,
                                                 (int)lineNumber.lineNumber,
                                                 (int)lineNumber.columnNumber,
                                                 sourceFile.fileName);

        if (includeInlinees) {
          var funcSymbol = FindFunctionSymbolByRVA(rva, true);

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
      3 => SHA256.Create(),
      _ => null
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

  private List<FunctionDebugInfo> CollectFunctionDebugInfo() {
    if (!EnsureLoaded()) {
      return null;
    }

    IDiaEnumSymbols symbolEnum = null;
    IDiaEnumSymbols publicSymbolEnum = null;

    try {
      var symbolList = new List<FunctionDebugInfo>();
      var symbolMap = new Dictionary<long, FunctionDebugInfo>();
      globalSymbol_.findChildren(SymTagEnum.SymTagFunction, null, 0, out symbolEnum);
      globalSymbol_.findChildren(SymTagEnum.SymTagPublicSymbol, null, 0, out publicSymbolEnum);

      foreach (IDiaSymbol sym in symbolEnum) {
        //Trace.WriteLine($" FuncSym {sym.name}: RVA {sym.relativeVirtualAddress:X}, size {sym.length}");
        var funcInfo = new FunctionDebugInfo(sym.name, sym.relativeVirtualAddress, (uint)sym.length);
        symbolList.Add(funcInfo);
        symbolMap[funcInfo.RVA] = funcInfo;
      }

      foreach (IDiaSymbol sym in publicSymbolEnum) {
        //Trace.WriteLine($" PublicSym {sym.name}: RVA {sym.relativeVirtualAddress:X} size {sym.length}");
        var funcInfo = new FunctionDebugInfo(sym.name, sym.relativeVirtualAddress, (uint)sym.length);

        // Public symbols are preferred over function symbols if they have the same RVA and size.
        // This ensures that the mangled name is saved, set only of public symbols.
        // This tries to mirror the behavior from FindFunctionSymbolByRVA.
        if (symbolMap.TryGetValue(funcInfo.RVA, out var existingFuncInfo)) {
          if (existingFuncInfo.Size == funcInfo.Size) {
            existingFuncInfo.Name = funcInfo.Name;
          }
        }
        else {
          // Consider the public sym if it doesn't overlap a function sym.
          symbolList.Add(funcInfo);
        }
      }

      return symbolList;
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

    return null;
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

  private IDiaSymbol FindFunctionSymbolByRVA(long rva, bool preferFunctionSym = false) {
    if (!EnsureLoaded()) {
      return null;
    }

    try {
      session_.findSymbolByRVA((uint)rva, SymTagEnum.SymTagFunction, out var funcSym);

      if (preferFunctionSym && funcSym != null) {
        return funcSym;
      }

      // Public symbols are preferred over function symbols if they have the same RVA and size.
      // This ensures that the mangled name is saved, set only of public symbols.
      session_.findSymbolByRVA((uint)rva, SymTagEnum.SymTagPublicSymbol, out var pubSym);

      if (pubSym != null) {
        if (funcSym == null ||
            funcSym.relativeVirtualAddress == pubSym.relativeVirtualAddress &&
            funcSym.length == pubSym.length) {
          return pubSym;
        }
      }

      return funcSym;
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

sealed class BasicAuthenticationHandler : SymbolReaderAuthHandler {
  private SymbolFileSourceSettings settings_;

  public BasicAuthenticationHandler(SymbolFileSourceSettings settings, TextWriter log) :
    base(log, "HTTP Auth") {
    settings_ = settings;
  }

  protected override bool TryGetAuthority(Uri requestUri, out Uri authority) {
    if (!settings_.AuthorizationTokenEnabled) {
      authority = null;
      return false;
    }

    authority = requestUri;
    return true;
  }

  protected override Task<AuthToken?> GetAuthTokenAsync(RequestContext context, SymbolReaderHandlerDelegate next,
                                                        Uri authority, CancellationToken cancellationToken) {
    if (settings_.AuthorizationTokenEnabled) {
      string username = settings_.AuthorizationUser;
      string pat = settings_.AuthorizationToken;
      var token = AuthToken.CreateBasicFromUsernameAndPassword(username, pat);
      return Task.FromResult<AuthToken?>(token);
    }

    return null;
  }
}

/// <summary>
/// Wraps a TokenCredential to catch authentication failures and convert them to
/// CredentialUnavailableException so ChainedTokenCredential continues to the next credential.
/// This ensures that if VS auth needs re-authentication, we fall through to browser auth.
/// </summary>
sealed class FallbackTokenCredential : TokenCredential {
  private readonly TokenCredential inner_;

  public FallbackTokenCredential(TokenCredential inner) {
    inner_ = inner;
  }

  public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) {
    try {
      return inner_.GetToken(requestContext, cancellationToken);
    }
    catch (CredentialUnavailableException) {
      throw; // Let this pass through - it's expected
    }
    catch (Exception ex) {
      // Convert other auth failures to CredentialUnavailableException so chain continues
      throw new CredentialUnavailableException($"{inner_.GetType().Name} failed: {ex.Message}", ex);
    }
  }

  public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) {
    return GetTokenAsyncImpl(requestContext, cancellationToken);
  }

  private async ValueTask<AccessToken> GetTokenAsyncImpl(TokenRequestContext requestContext, CancellationToken cancellationToken) {
    try {
      return await inner_.GetTokenAsync(requestContext, cancellationToken).ConfigureAwait(false);
    }
    catch (CredentialUnavailableException) {
      throw; // Let this pass through - it's expected
    }
    catch (Exception ex) {
      // Convert other auth failures to CredentialUnavailableException so chain continues
      throw new CredentialUnavailableException($"{inner_.GetType().Name} failed: {ex.Message}", ex);
    }
  }
}