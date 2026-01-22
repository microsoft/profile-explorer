// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using ProfileExplorer.Core.Binary;
using ProfileExplorer.Core.Session;
using ProfileExplorer.Core.Utilities;
using ProtoBuf;

namespace ProfileExplorer.Core.Settings;

public enum SymbolFileRejectionReason {
  Unknown = 0,           // Unknown/legacy
  PermanentNotFound,     // 404 - safe to cache (3 days)
  InvalidFormat,         // Parse error - safe to cache
  NetworkTimeout,        // Transient - DON'T cache
  AuthenticationFailure, // Transient - DON'T cache
  ServerError           // 5xx - transient - DON'T cache
}

[ProtoContract(SkipConstructor = true)]
public class SymbolFileSourceSettings : SettingsBase {
  private const string DefaultPrivateSymbolServer = @"https://symweb.azurefd.net";
  private const string DefaultPublicSymbolServer = @"https://msdl.microsoft.com/download/symbols";
  private const string DefaultSymbolCachePath = @"C:\Symbols";
  private const string DefaultEnvironmentVarSymbolPath = @"_NT_SYMBOL_PATH";
  public const double DefaultLowSampleModuleCutoff = 0.002; // 0.2%

  public SymbolFileSourceSettings() {
    Reset();
  }

  public static string DefaultCacheDirectoryPath => Path.Combine(Path.GetTempPath(), "ProfileExplorer", "symcache");
  [ProtoMember(1)][OptionValue()]
  public List<string> SymbolPaths { get; set; }
  [ProtoMember(2)][OptionValue(true)]
  public bool SourceServerEnabled { get; set; }
  [ProtoMember(3)][OptionValue(false)]
  public bool AuthorizationTokenEnabled { get; set; }
  [ProtoMember(4)][OptionValue("")]
  public string AuthorizationToken { get; set; }
  [ProtoMember(5)][OptionValue(false)]
  public bool UseEnvironmentVarSymbolPaths { get; set; }
  [ProtoMember(6)][OptionValue(true)]
  public bool SkipLowSampleModules { get; set; }
  [ProtoMember(7)][OptionValue("")]
  public string AuthorizationUser { get; set; }
  [ProtoMember(8)][OptionValue()]
  public HashSet<BinaryFileDescriptor> RejectedBinaryFiles { get; set; }
  [ProtoMember(9)][OptionValue()]
  public HashSet<SymbolFileDescriptor> RejectedSymbolFiles { get; set; }
  [ProtoMember(10)][OptionValue(true)]
  public bool RejectPreviouslyFailedFiles { get; set; }
  [ProtoMember(11)][OptionValue(true)]
  public bool IncludeSymbolSubdirectories { get; set; }
  [ProtoMember(12)][OptionValue(true)]
  public bool CacheSymbolFiles { get; set; }
  [ProtoMember(13)][OptionValue("")]
  public string CustomSymbolCacheDirectory { get; set; }
  [ProtoMember(14)][OptionValue(0.002)] // 0.2%
  public double LowSampleModuleCutoff { get; set; }
  [ProtoMember(15)][OptionValue(true)]
  public bool CompanyFilterEnabled { get; set; }
  [ProtoMember(16)][OptionValue()]
  public List<string> CompanyFilterStrings { get; set; }
  [ProtoMember(17)][OptionValue(30)] // 30 seconds normal timeout (after auth validated)
  public int SymbolServerTimeoutSeconds { get; set; }
  [ProtoMember(18)][OptionValue(true)]
  public bool BellwetherTestEnabled { get; set; }
  [ProtoMember(19)][OptionValue(60)] // 60 seconds for bellwether/initial requests (after auth validated)
  public int BellwetherTimeoutSeconds { get; set; }
  [ProtoMember(20)][OptionValue(10)] // 10 seconds when symbol server is degraded
  public int DegradedTimeoutSeconds { get; set; }
  [ProtoMember(24)][OptionValue(600)] // 10 minutes pre-auth timeout (user may need to interact with auth dialog)
  public int PreAuthTimeoutSeconds { get; set; }
  [ProtoMember(21)][OptionValue(true)]
  public bool WindowsPathFilterEnabled { get; set; }
  [ProtoMember(22)]
  public DateTime RejectedFilesCacheTime { get; set; }
  [ProtoMember(23)][OptionValue(3)] // 3 days default expiration
  public int RejectedFilesCacheExpirationDays { get; set; }
  public bool HasAuthorizationToken => AuthorizationTokenEnabled && !string.IsNullOrEmpty(AuthorizationToken);
  public bool HasCompanyFilter => CompanyFilterEnabled;

  // Runtime state - not persisted. Set when bellwether test fails.
  public bool SymbolServerDegraded { get; set; }

  // Runtime state - tracks if we've had our first successful network request.
  // Used to know when to reduce timeout from initial 30s to normal 10s.
  public bool HadFirstSuccessfulNetworkRequest { get; set; }

  // Runtime state - tracks if we've had any timeout, triggering reduced timeout.
  public bool HadFirstTimeout { get; set; }

  // Runtime state - tracks if primary (private) server auth failed.
  // When true, subsequent downloads skip primary and go directly to secondary (public) server.
  public bool PrimaryServerAuthFailed { get; set; }

  // Runtime state - tracks if primary server has been verified working.
  // When true, we know primary server works and should be used.
  public bool PrimaryServerVerified { get; set; }

  // Runtime state - session-level tracking for negative cache safety checks.
  // These are NOT persisted and reset on each session.
  public int SessionSymbolSearchCount { get; set; }
  public int SessionSymbolFailureCount { get; set; }
  public int SessionSymbolSuccessCount { get; set; }

  /// <summary>
  /// Returns the effective timeout based on current state:
  /// - If auth not yet validated: use PreAuthTimeoutSeconds (600s/10min) to allow for interactive auth
  /// - If degraded (bellwether failed): use DegradedTimeoutSeconds (10s)
  /// - If we've had a timeout: use SymbolServerTimeoutSeconds (30s)
  /// - Otherwise (initial post-auth state): use BellwetherTimeoutSeconds (60s)
  /// </summary>
  public int EffectiveTimeoutSeconds {
    get {
      // Before auth is validated, use very long timeout to allow for interactive auth dialog
      // The user might not see the auth dialog immediately, so we give them 10 minutes
      if (!HadFirstSuccessfulNetworkRequest && !SymbolServerDegraded) {
        return PreAuthTimeoutSeconds;
      }
      if (SymbolServerDegraded) {
        return DegradedTimeoutSeconds;
      }
      if (HadFirstTimeout) {
        return SymbolServerTimeoutSeconds;
      }
      // Auth validated, no timeouts yet - use generous initial timeout
      return BellwetherTimeoutSeconds;
    }
  }

  /// <summary>
  /// Returns the effective company filter strings. Uses "Microsoft" as default if enabled but list is empty.
  /// </summary>
  public IReadOnlyList<string> EffectiveCompanyFilterStrings {
    get {
      if (CompanyFilterStrings?.Count > 0) {
        return CompanyFilterStrings;
      }
      // Default to "Microsoft" when filter is enabled but no custom strings specified
      return CompanyFilterEnabled ? new[] { "Microsoft" } : Array.Empty<string>();
    }
  }
  public string SymbolCacheDirectoryPath => !string.IsNullOrEmpty(CustomSymbolCacheDirectory) ?
    CustomSymbolCacheDirectory :
    DefaultCacheDirectoryPath;
  public long SymbolCacheDirectorySizeMB => Utils.ComputeDirectorySize(SymbolCacheDirectoryPath) / (1024 * 1024);

  public string EnvironmentVarSymbolPath {
    get {
      try {
        return Environment.GetEnvironmentVariable(DefaultEnvironmentVarSymbolPath);
      }
      catch (Exception ex) {
        Trace.WriteLine($"Failed to read symbol env var: {ex.Message}");
        return null;
      }
    }
  }

  public void ClearSymbolFileCache() {
    try {
      if (Directory.Exists(SymbolCacheDirectoryPath)) {
        Directory.Delete(SymbolCacheDirectoryPath, true);
      }
    }
    catch (Exception ex) {
      Trace.WriteLine($"Failed to empty cache dir {SymbolCacheDirectoryPath}: {ex.Message}");
    }
  }

  public void AddSymbolServer(bool usePrivateServer) {
    string symbolServer = usePrivateServer ? DefaultPrivateSymbolServer : DefaultPublicSymbolServer;
    string path = $"srv*{DefaultSymbolCachePath}*{symbolServer}";

    if (!SymbolPaths.Contains(path)) {
      SymbolPaths.Add(path);
    }
  }

  public bool HasSymbolPath(string path) {
    path = Utils.TryGetDirectoryName(path).ToLowerInvariant();
    return SymbolPaths.Find(item => item.ToLowerInvariant() == path) != null;
  }

  public void InsertSymbolPath(string path) {
    if (string.IsNullOrEmpty(path)) {
      return;
    }

    foreach (string p in path.Split(";")) {
      string dir = Utils.TryGetDirectoryName(p);

      if (!string.IsNullOrEmpty(dir) && !HasSymbolPath(dir)) {
        SymbolPaths.Insert(0, dir); // Prepend path.
      }
    }
  }

  public void InsertSymbolPaths(IEnumerable<string> paths) {
    foreach (string path in paths) {
      InsertSymbolPath(path);
    }
  }

  public SymbolFileSourceSettings WithSymbolPaths(params string[] paths) {
    var options = Clone();

    foreach (string path in paths) {
      options.InsertSymbolPath(path);
    }

    return options;
  }

  public bool IsRejectedBinaryFile(BinaryFileDescriptor file) {
    bool isRejected = RejectPreviouslyFailedFiles && RejectedBinaryFiles.Contains(file);
    if (isRejected) {
      Trace.WriteLine($"BINARY_FILTER_DEBUG: Binary file REJECTED - Previously failed: {file?.ImageName} (path: {file?.ImagePath})");
    }
    return isRejected;
  }

  public bool IsRejectedSymbolFile(SymbolFileDescriptor file) {
    bool isRejected = RejectPreviouslyFailedFiles && RejectedSymbolFiles.Contains(file);
    if (isRejected) {
      Trace.WriteLine($"DEBUG_FILTER_DEBUG: Symbol file REJECTED - Previously failed: {file?.FileName} (ID: {file?.Id}, Age: {file?.Age})");
    }
    return isRejected;
  }

  /// <summary>
  /// Checks if a rejection reason represents a transient failure that should NOT be cached.
  /// Transient failures include auth failures, timeouts, and server errors.
  /// </summary>
  private bool IsTransientFailure(SymbolFileRejectionReason reason) {
    return reason == SymbolFileRejectionReason.AuthenticationFailure ||
           reason == SymbolFileRejectionReason.NetworkTimeout ||
           reason == SymbolFileRejectionReason.ServerError ||
           reason == SymbolFileRejectionReason.Unknown; // Treat unknown conservatively - don't cache
  }

  /// <summary>
  /// Checks if a symbol file exists in the local symbol cache directories.
  /// Searches all paths from _NT_SYMBOL_PATH including structured (foo.pdb/GUID/foo.pdb) and flat directories.
  /// </summary>
  private bool IsSymbolFileInLocalCache(SymbolFileDescriptor file) {
    if (file == null || string.IsNullOrEmpty(file.FileName)) {
      return false;
    }

    // Check all symbol paths configured
    foreach (string path in SymbolPaths) {
      if (string.IsNullOrEmpty(path)) continue;

      // Skip symbol server entries (srv*)
      if (path.StartsWith("srv*", StringComparison.OrdinalIgnoreCase)) {
        // Extract local cache path from srv*C:\Symbols*https://...
        var parts = path.Split('*');
        if (parts.Length >= 2) {
          string localCachePath = parts[1];
          if (CheckSymbolFileInDirectory(localCachePath, file)) {
            return true;
          }
        }
        continue;
      }

      // Check regular directory paths
      if (CheckSymbolFileInDirectory(path, file)) {
        return true;
      }
    }

    // Check environment variable symbol paths if enabled
    if (UseEnvironmentVarSymbolPaths && !string.IsNullOrEmpty(EnvironmentVarSymbolPath)) {
      foreach (string path in EnvironmentVarSymbolPath.Split(';')) {
        if (string.IsNullOrEmpty(path)) continue;

        if (path.StartsWith("srv*", StringComparison.OrdinalIgnoreCase)) {
          var parts = path.Split('*');
          if (parts.Length >= 2) {
            string localCachePath = parts[1];
            if (CheckSymbolFileInDirectory(localCachePath, file)) {
              return true;
            }
          }
        }
        else if (CheckSymbolFileInDirectory(path, file)) {
          return true;
        }
      }
    }

    return false;
  }

  /// <summary>
  /// Checks if a symbol file exists in a specific directory, looking for both structured
  /// symbol server format (foo.pdb/GUID/foo.pdb) and flat format (foo.pdb).
  /// </summary>
  private bool CheckSymbolFileInDirectory(string directory, SymbolFileDescriptor file) {
    if (!Directory.Exists(directory)) {
      return false;
    }

    try {
      // Check structured symbol server format: basePath\fileName\GUID\fileName
      string structuredPath = Path.Combine(directory, file.FileName, file.Id.ToString("N").ToUpperInvariant(), file.FileName);
      if (File.Exists(structuredPath)) {
        DiagnosticLogger.LogInfo($"[NegativeCache] Found symbol file in local cache (structured): {structuredPath}");
        return true;
      }

      // Check flat format: basePath\fileName
      string flatPath = Path.Combine(directory, file.FileName);
      if (File.Exists(flatPath)) {
        DiagnosticLogger.LogInfo($"[NegativeCache] Found symbol file in local cache (flat): {flatPath}");
        return true;
      }
    }
    catch (Exception ex) {
      Trace.WriteLine($"[NegativeCache] Error checking symbol file in {directory}: {ex.Message}");
    }

    return false;
  }

  /// <summary>
  /// Session-level safety check: If we have a suspicious failure rate (>80% failures with at least 10 searches),
  /// something is systematically wrong (network down, auth broken, etc.) and we should NOT add to negative cache.
  /// This prevents mass rejection when there's an infrastructure issue.
  /// </summary>
  private bool ShouldSkipNegativeCachingDueToSuspiciousFailureRate() {
    const int MinSearchCount = 10;
    const double MaxFailureRate = 0.80; // 80%

    if (SessionSymbolSearchCount < MinSearchCount) {
      return false; // Not enough data yet
    }

    double failureRate = (double)SessionSymbolFailureCount / SessionSymbolSearchCount;

    if (failureRate > MaxFailureRate) {
      DiagnosticLogger.LogWarning($"[NegativeCache] Suspicious failure rate detected: {SessionSymbolFailureCount}/{SessionSymbolSearchCount} ({failureRate:P0}) - Disabling negative caching for this session");
      return true;
    }

    return false;
  }

  public void RejectBinaryFile(BinaryFileDescriptor file) {
    if (RejectPreviouslyFailedFiles) {
      RejectedBinaryFiles.Add(file);
      // Update cache time on first rejection
      if (RejectedFilesCacheTime == default) {
        RejectedFilesCacheTime = DateTime.UtcNow;
      }
    }
  }

  /// <summary>
  /// Attempts to add a symbol file to the negative cache (list of previously failed symbols).
  /// Includes multiple safeguards to prevent caching transient failures:
  /// - Skips transient failures (auth, timeout, server errors)
  /// - Skips if auth failure detected this session
  /// - Skips if file exists in local cache
  /// - Skips if suspicious failure rate detected (>80%)
  /// Returns true if the file was added to negative cache, false if rejected or skipped.
  /// </summary>
  public bool RejectSymbolFile(SymbolFileDescriptor file,
                                SymbolFileRejectionReason reason = SymbolFileRejectionReason.Unknown,
                                string searchLog = null) {
    if (!RejectPreviouslyFailedFiles) return false;

    // SAFEGUARD 1: Skip transient failures
    if (IsTransientFailure(reason)) {
      DiagnosticLogger.LogInfo($"[NegativeCache] Skipping transient failure: {file.FileName} - {reason}");
      return false;
    }

    // SAFEGUARD 2: Check if auth failure detected this session
    if (PrimaryServerAuthFailed) {
      DiagnosticLogger.LogWarning($"[NegativeCache] Auth failure detected - rejecting rejection for {file.FileName}");
      return false;
    }

    // SAFEGUARD 3: Check if file exists in local cache
    if (IsSymbolFileInLocalCache(file)) {
      DiagnosticLogger.LogInfo($"[NegativeCache] Symbol exists in local cache: {file.FileName}");
      return false;
    }

    // SAFEGUARD 4: Session-level safety check - suspicious failure rate
    if (ShouldSkipNegativeCachingDueToSuspiciousFailureRate()) {
      DiagnosticLogger.LogWarning($"[NegativeCache] Suspicious failure rate - skipping ALL negative caching");
      return false;
    }

    // All checks passed - safe to add to negative cache
    RejectedSymbolFiles.Add(file);
    if (RejectedFilesCacheTime == default) {
      RejectedFilesCacheTime = DateTime.UtcNow;
    }

    DiagnosticLogger.LogInfo($"[NegativeCache] Added: {file.FileName} - Reason: {reason}");
    return true;
  }

  public void ClearRejectedFiles() {
    RejectedSymbolFiles.Clear();
    RejectedBinaryFiles.Clear();
  }

  /// <summary>
  /// Checks if a file path is in a Windows/Microsoft directory (likely Microsoft binary).
  /// Used as a heuristic when PE version info isn't available yet.
  /// </summary>
  public static bool IsWindowsSystemPath(string filePath) {
    if (string.IsNullOrEmpty(filePath)) {
      return false;
    }

    // Common Windows system paths that contain Microsoft binaries
    string windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    string systemRoot = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
    string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

    return filePath.StartsWith(windowsDir, StringComparison.OrdinalIgnoreCase) ||
           filePath.StartsWith(systemRoot, StringComparison.OrdinalIgnoreCase) ||
           filePath.StartsWith(@"C:\Windows", StringComparison.OrdinalIgnoreCase) ||
           filePath.StartsWith(@"\SystemRoot", StringComparison.OrdinalIgnoreCase) ||
           filePath.Contains(@"\Windows\System32\", StringComparison.OrdinalIgnoreCase) ||
           filePath.Contains(@"\Windows\SysWOW64\", StringComparison.OrdinalIgnoreCase) ||
           filePath.Contains(@"\Windows\WinSxS\", StringComparison.OrdinalIgnoreCase) ||
           // WindowsApps contains Microsoft Store apps and system components
           filePath.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase) ||
           filePath.Contains(@"\Program Files\WindowsApps\", StringComparison.OrdinalIgnoreCase) ||
           // Device paths from ETW traces (e.g., \Device\HarddiskVolume2\Windows\...)
           (filePath.StartsWith(@"\Device\", StringComparison.OrdinalIgnoreCase) &&
            (filePath.Contains(@"\Windows\", StringComparison.OrdinalIgnoreCase) ||
             filePath.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase))) ||
           // Microsoft.* and CoreMessaging* DLLs are typically Microsoft binaries regardless of path
           System.IO.Path.GetFileName(filePath).StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase) ||
           System.IO.Path.GetFileName(filePath).StartsWith("CoreMessaging", StringComparison.OrdinalIgnoreCase);
  }

  public void ExpandSymbolPathsSubdirectories(string[] symbolExtensions) {
    // For symbol paths that are local directories, add any subdirectory
    // that also contains symbols to the path list, otherwise the symbol reader
    // will not find them since it doesn't search rerursively.
    var symbolPathSet = new HashSet<string>();

    foreach (string path in SymbolPaths) {
      symbolPathSet.Add(path);

      if (path.StartsWith("srv*", StringComparison.OrdinalIgnoreCase)) {
        continue; // Skip over symbol servers.
      }

      //? TODO: Option for max level
      BuildSymbolsDirectoriesSet(path, symbolPathSet, symbolExtensions, 0, 3);
    }

    SymbolPaths = symbolPathSet.ToList();
  }

  private void BuildSymbolsDirectoriesSet(string path, HashSet<string> symbolDirs,
                                          string[] symbolExtensions,
                                          int level = 0, int maxLevel = 0) {
    if (!Directory.Exists(path) || level > maxLevel) {
      return;
    }

    try {
      if (level == 0) {
        // Check the drive type for the top-level directory
        // and accept only local paths (exclude mapped network paths).
        var driveInfo = new DriveInfo(path);

        if (driveInfo.DriveType != DriveType.Fixed) {
          return;
        }
      }

      foreach (string file in Directory.EnumerateFileSystemEntries(path)) {
        if (File.GetAttributes(file).HasFlag(FileAttributes.Directory)) {
          BuildSymbolsDirectoriesSet(file, symbolDirs, symbolExtensions, level + 1, maxLevel);
        }
        else if (level > 0) {
          // Top-level directory already included in set,
          // check files only for subdirectories.
          string extension = Path.GetExtension(file);

          foreach (string symbolExt in symbolExtensions) {
            if (extension.Equals(symbolExt, StringComparison.OrdinalIgnoreCase)) {
              symbolDirs.Add(path);
              break;
            }
          }
        }
      }
    }
    catch (Exception ex) {
      Trace.WriteLine($"Failed to expand symbols dir set: {ex.Message}");
    }
  }

  public static bool ShouldUsePrivateSymbolPath() {
    // Try to detect running as a domain-joined or AAD-joined account,
    // which should have access to a private, internal symbol server.
    // Based on https://stackoverflow.com/questions/926227/how-to-detect-if-machine-is-joined-to-domain
    // try {
    //   string domain = IPGlobalProperties.GetIPGlobalProperties().DomainName;
    //
    //   if (domain != null &&
    //       domain.Contains("DOMANIN_NAME", StringComparison.OrdinalIgnoreCase)) {
    //     Trace.WriteLine("Set symbol path for domain-joined machine");
    //     return true;
    //   }
    // }
    // catch (Exception ex) {
    // }

    try {
      string pcszTenantId = null;
      IntPtr ptrJoinInfo = IntPtr.Zero;
      IntPtr ptrUserInfo = IntPtr.Zero;
      IntPtr ptrJoinCertificate = IntPtr.Zero;
      var joinInfo = new NetAPI32.DSREG_JOIN_INFO();

      NetAPI32.NetFreeAadJoinInformation(IntPtr.Zero);
      int retValue = NetAPI32.NetGetAadJoinInformation(pcszTenantId, out ptrJoinInfo);

      if (retValue == 0) {
        var ptrJoinInfoObject = new NetAPI32.DSREG_JOIN_INFO();
        joinInfo = (NetAPI32.DSREG_JOIN_INFO)Marshal.PtrToStructure(ptrJoinInfo, (Type)ptrJoinInfoObject.GetType());

        if (joinInfo.JoinUserEmail.Contains("@microsoft.com", StringComparison.OrdinalIgnoreCase) ||
            joinInfo.TenantDisplayName.Contains("microsoft", StringComparison.OrdinalIgnoreCase)) {
          Trace.WriteLine("Set symbol path for AAD-joined machine");
          return true;
        }
      }
    }
    catch (Exception ex) {
    }

    Trace.WriteLine("Set symbol path for non-domain-joined machine");
    return false;
  }

  public override void Reset() {
    InitializeReferenceMembers();
    ResetAllOptions(this);

    if (ShouldUsePrivateSymbolPath()) {
      AddSymbolServer(usePrivateServer: true);
    }

    AddSymbolServer(usePrivateServer: false);

    // Default company filter to "Microsoft" since the tool is primarily for Microsoft code.
    // Users can modify this in the UI settings.
    if (CompanyFilterStrings.Count == 0) {
      CompanyFilterStrings.Add("Microsoft");
    }
  }

  public SymbolFileSourceSettings Clone() {
    byte[] serialized = StateSerializer.Serialize(this);
    return StateSerializer.Deserialize<SymbolFileSourceSettings>(serialized);
  }

  [ProtoAfterDeserialization]
  private void InitializeReferenceMembers() {
    InitializeReferenceOptions(this);

    // Migrate old settings: ensure negative caching is enabled by default.
    // Old settings may have this as false before the feature was fully implemented.
    if (!RejectPreviouslyFailedFiles) {
      RejectPreviouslyFailedFiles = true;
    }

    // Check if rejected files cache has expired (default 3 days).
    // This allows retrying symbols that may have become available.
    int expirationDays = RejectedFilesCacheExpirationDays > 0 ? RejectedFilesCacheExpirationDays : 3;
    if (RejectedFilesCacheTime != default &&
        DateTime.UtcNow - RejectedFilesCacheTime > TimeSpan.FromDays(expirationDays)) {
      Trace.WriteLine($"[SymbolSettings] Rejected files cache expired (>{expirationDays} days old), clearing {RejectedBinaryFiles?.Count ?? 0} binaries and {RejectedSymbolFiles?.Count ?? 0} symbols");
      ClearRejectedFiles();
      RejectedFilesCacheTime = DateTime.UtcNow;
    }

    // TODO: TEMPORARY - clear negative cache for testing. Remove this after testing.
    // Trace.WriteLine($"[SymbolSettings] TEMP: Clearing negative cache for testing ({RejectedBinaryFiles?.Count ?? 0} binaries, {RejectedSymbolFiles?.Count ?? 0} symbols)");
    // ClearRejectedFiles();
    // RejectedFilesCacheTime = DateTime.UtcNow;
  }

  public override bool Equals(object obj) {
    return AreOptionsEqual(this, obj);
  }

  public override string ToString() {
    return PrintOptions(this);
  }

  private class NetAPI32 {
    [DllImport("netapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern void NetFreeAadJoinInformation(
      IntPtr pJoinInfo);

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int NetGetAadJoinInformation(
      string pcszTenantId,
      out IntPtr ppJoinInfo);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DSREG_JOIN_INFO {
      public int joinType;
      public IntPtr pJoinCertificate;
      [MarshalAs(UnmanagedType.LPWStr)] public string DeviceId;
      [MarshalAs(UnmanagedType.LPWStr)] public string IdpDomain;
      [MarshalAs(UnmanagedType.LPWStr)] public string TenantId;
      [MarshalAs(UnmanagedType.LPWStr)] public string JoinUserEmail;
      [MarshalAs(UnmanagedType.LPWStr)] public string TenantDisplayName;
      [MarshalAs(UnmanagedType.LPWStr)] public string MdmEnrollmentUrl;
      [MarshalAs(UnmanagedType.LPWStr)] public string MdmTermsOfUseUrl;
      [MarshalAs(UnmanagedType.LPWStr)] public string MdmComplianceUrl;
      [MarshalAs(UnmanagedType.LPWStr)] public string UserSettingSyncUrl;
      public IntPtr pUserInfo;
    }
  }
}