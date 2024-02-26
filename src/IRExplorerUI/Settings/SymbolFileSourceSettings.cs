using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ProtoBuf;

namespace IRExplorerUI.Compilers;

[ProtoContract(SkipConstructor = true)]
public class SymbolFileSourceSettings : SettingsBase {
  private const string DefaultPrivateSymbolServer = @"https://symweb";
  private const string DefaultPublicSymbolServer = @"https://msdl.microsoft.com/download/symbols";
  private const string DefaultSymbolCachePath = @"C:\Symbols";
  private const string DefaultEnvironmentVarSymbolPath = @"_NT_SYMBOL_PATH";

  public const int LowSampleModuleCutoff = 10;

  public SymbolFileSourceSettings() {
    Reset();
  }

  [ProtoMember(1)]
  public List<string> SymbolPaths { get; set; }
  [ProtoMember(2)]
  public bool SourceServerEnabled { get; set; }
  [ProtoMember(3)]
  public bool AuthorizationTokenEnabled { get; set; }
  [ProtoMember(4)]
  public string AuthorizationToken { get; set; }
  [ProtoMember(5)]
  public bool UseEnvironmentVarSymbolPaths { get; set; }
  [ProtoMember(6)]
  public bool SkipLowSampleModules { get; set; }
  [ProtoMember(7)]
  public string AuthorizationUser { get; set; }
  [ProtoMember(8)]
  public HashSet<BinaryFileDescriptor> RejectedBinaryFiles { get; set; }
  [ProtoMember(9)]
  public HashSet<SymbolFileDescriptor> RejectedSymbolFiles { get; set; }
  [ProtoMember(10)]
  public bool RejectPreviouslyFailedFiles { get; set; }

  public bool HasAuthorizationToken => AuthorizationTokenEnabled && !string.IsNullOrEmpty(AuthorizationToken);

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

  public void AddSymbolServer(bool usePrivateServer) {
    string symbolServer = usePrivateServer ? DefaultPrivateSymbolServer : DefaultPublicSymbolServer;
    var path = $"srv*{DefaultSymbolCachePath}*{symbolServer}";

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
      var dir = Utils.TryGetDirectoryName(p);

      if(!string.IsNullOrEmpty(dir) && !HasSymbolPath(dir)) {
        SymbolPaths.Add(dir);
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
    return RejectPreviouslyFailedFiles &&
           RejectedBinaryFiles.Contains(file);
  }

  public bool IsRejectedSymbolFile(SymbolFileDescriptor file) {
    return RejectPreviouslyFailedFiles &&
           RejectedSymbolFiles.Contains(file);
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

  public static bool ShouldUsePrivateSymbolPath() {
    // Try to detect running as a domain-joined or AAD-joined account,
    // which should have access to a private, internal symbol server.
    // Based on https://stackoverflow.com/questions/926227/how-to-detect-if-machine-is-joined-to-domain
    //? TODO: Domains/emails should not be hardcoded

    try {
      string domain = System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().DomainName;

      if (domain != null &&
          domain.Contains("redmond", StringComparison.OrdinalIgnoreCase) ||
          domain.Contains("ntdev", StringComparison.OrdinalIgnoreCase)) {
        Trace.WriteLine("Set symbol path for domain-joined machine");
        return true;
      }
    }
    catch (Exception ex) {
    }

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

    RejectedSymbolFiles.Clear();
    RejectedBinaryFiles.Clear();
    UseEnvironmentVarSymbolPaths = true;
    SourceServerEnabled = true;
    SkipLowSampleModules = true;
    AddSymbolServer(usePrivateServer: false);

    if (ShouldUsePrivateSymbolPath()) {
      AddSymbolServer(usePrivateServer : true);
    }
  }

  public SymbolFileSourceSettings Clone() {
    byte[] serialized = StateSerializer.Serialize(this);
    return StateSerializer.Deserialize<SymbolFileSourceSettings>(serialized);
  }

  [ProtoAfterDeserialization]
  private void InitializeReferenceMembers() {
    SymbolPaths ??= new List<string>();
    RejectedSymbolFiles ??= new HashSet<SymbolFileDescriptor>();
    RejectedBinaryFiles ??= new HashSet<BinaryFileDescriptor>();
  }

  protected bool Equals(SymbolFileSourceSettings other) {
    return SymbolPaths.AreEqual(other.SymbolPaths) &&
           RejectedSymbolFiles.AreEqual(other.RejectedSymbolFiles) &&
           RejectedBinaryFiles.AreEqual(other.RejectedBinaryFiles) &&
           SourceServerEnabled == other.SourceServerEnabled &&
           AuthorizationTokenEnabled == other.AuthorizationTokenEnabled &&
           AuthorizationToken == other.AuthorizationToken &&
           AuthorizationUser == AuthorizationToken &&
           UseEnvironmentVarSymbolPaths == other.UseEnvironmentVarSymbolPaths &&
           SkipLowSampleModules == other.SkipLowSampleModules &&
           RejectPreviouslyFailedFiles == other.RejectPreviouslyFailedFiles;
  }

  public override bool Equals(object obj) {
    if (ReferenceEquals(null, obj))
      return false;
    if (ReferenceEquals(this, obj))
      return true;
    if (obj.GetType() != this.GetType())
      return false;
    return Equals((SymbolFileSourceSettings)obj);
  }

  public override string ToString() {
    var text = $"SymbolPaths: {SymbolPaths.Count}\n" +
               $"SourceServerEnabled: {SourceServerEnabled}\n" +
               $"AuthorizationTokenEnabled: {AuthorizationTokenEnabled}\n" +
               $"AuthorizationUser: {AuthorizationUser}\n" +
               $"AuthorizationToken: {AuthorizationToken}\n" +
               $"SkipLowSampleModules: {SkipLowSampleModules}\n" +
               $"UseEnvironmentVarSymbolPaths: {UseEnvironmentVarSymbolPaths}\n" +
               $"RejectPreviouslyFailedFiles: {RejectPreviouslyFailedFiles}\n";
    text += $"Rejected binaries: {RejectedBinaryFiles.Count}\n";

    foreach (var file in RejectedBinaryFiles) {
      text += $" - {file}\n";
    }

    text += $"\nRejected symbols: {RejectedSymbolFiles.Count}";

    foreach (var file in RejectedSymbolFiles) {
      text += $" - {file}\n";
    }

    return text;
  }

  class NetAPI32 {
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

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern void NetFreeAadJoinInformation(
      IntPtr pJoinInfo);

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int NetGetAadJoinInformation(
      string pcszTenantId,
      out IntPtr ppJoinInfo);
  }
}