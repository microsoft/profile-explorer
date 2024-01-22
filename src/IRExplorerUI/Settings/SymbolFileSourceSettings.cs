using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ProtoBuf;

namespace IRExplorerUI.Compilers;

[ProtoContract(SkipConstructor = true)]
public class SymbolFileSourceSettings : SettingsBase {
  protected bool Equals(SymbolFileSourceSettings other) {
    return Equals(SymbolPaths, other.SymbolPaths) && SourceServerEnabled == other.SourceServerEnabled && AuthorizationTokenEnabled == other.AuthorizationTokenEnabled && AuthorizationToken == other.AuthorizationToken && UseEnvironmentVarSymbolPaths == other.UseEnvironmentVarSymbolPaths;
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

  public override int GetHashCode() {
    unchecked {
      int hashCode = (SymbolPaths != null ? SymbolPaths.GetHashCode() : 0);
      hashCode = (hashCode * 397) ^ SourceServerEnabled.GetHashCode();
      hashCode = (hashCode * 397) ^ AuthorizationTokenEnabled.GetHashCode();
      hashCode = (hashCode * 397) ^ (AuthorizationToken != null ? AuthorizationToken.GetHashCode() : 0);
      hashCode = (hashCode * 397) ^ UseEnvironmentVarSymbolPaths.GetHashCode();
      return hashCode;
    }
  }

  private const string DefaultPrivateSymbolServer = @"https://symweb";
  private const string DefaultPublicSymbolServer = @"https://msdl.microsoft.com/download/symbols";
  private const string DefaultSymbolCachePath = @"C:\Symbols";

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
  public bool HasAuthorizationToken => AuthorizationTokenEnabled && !string.IsNullOrEmpty(AuthorizationToken);

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

    UseEnvironmentVarSymbolPaths = true;
    SourceServerEnabled = true;
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
  }
}