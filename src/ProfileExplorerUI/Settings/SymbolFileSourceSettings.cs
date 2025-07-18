// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using ProfileExplorer.UI.Binary;
using ProtoBuf;

namespace ProfileExplorer.UI.Compilers;

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
  [ProtoMember(10)][OptionValue(false)]
  public bool RejectPreviouslyFailedFiles { get; set; }
  [ProtoMember(11)][OptionValue(true)]
  public bool IncludeSymbolSubdirectories { get; set; }
  [ProtoMember(12)][OptionValue(true)]
  public bool CacheSymbolFiles { get; set; }
  [ProtoMember(13)][OptionValue("")]
  public string CustomSymbolCacheDirectory { get; set; }
  [ProtoMember(14)][OptionValue(0.002)] // 0.2%
  public double LowSampleModuleCutoff { get; set; }
  public bool HasAuthorizationToken => AuthorizationTokenEnabled && !string.IsNullOrEmpty(AuthorizationToken);
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
      Trace.WriteLine($"Binary file REJECTED - Previously failed: {file?.ImageName} (path: {file?.ImagePath})");
    }
    return isRejected;
  }

  public bool IsRejectedSymbolFile(SymbolFileDescriptor file) {
    bool isRejected = RejectPreviouslyFailedFiles && RejectedSymbolFiles.Contains(file);
    if (isRejected) {
      Trace.WriteLine($"Symbol file REJECTED - Previously failed: {file?.FileName} (ID: {file?.Id})");
    }
    return isRejected;
  }

  public void RejectBinaryFile(BinaryFileDescriptor file) {
    if (RejectPreviouslyFailedFiles) {
      RejectedBinaryFiles.Add(file);
    }
  }

  public void RejectSymbolFile(SymbolFileDescriptor file) {
    if (RejectPreviouslyFailedFiles) {
      RejectedSymbolFiles.Add(file);
    }
  }

  public void ClearRejectedFiles() {
    RejectedSymbolFiles.Clear();
    RejectedBinaryFiles.Clear();
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
  }

  public SymbolFileSourceSettings Clone() {
    byte[] serialized = StateSerializer.Serialize(this);
    return StateSerializer.Deserialize<SymbolFileSourceSettings>(serialized);
  }

  [ProtoAfterDeserialization]
  private void InitializeReferenceMembers() {
    InitializeReferenceOptions(this);
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