// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using IRExplorerCore;
using IRExplorerCore.IR;
using ProtoBuf;

namespace IRExplorerUI.Compilers;

public interface IDebugInfoProvider : IDisposable {
  public Machine? Architecture { get; }
  public SymbolFileSourceOptions SymbolOptions { get; set; }
  bool LoadDebugInfo(string debugFilePath);
  void Unload();
  bool LoadDebugInfo(DebugFileSearchResult debugFile);
  bool AnnotateSourceLocations(FunctionIR function, IRTextFunction textFunc);
  bool AnnotateSourceLocations(FunctionIR function, string functionName);
  IEnumerable<FunctionDebugInfo> EnumerateFunctions();
  List<FunctionDebugInfo> GetSortedFunctions();
  FunctionDebugInfo FindFunction(string functionName);
  FunctionDebugInfo FindFunctionByRVA(long rva);
  SourceFileDebugInfo FindFunctionSourceFilePath(IRTextFunction textFunc);
  SourceFileDebugInfo FindFunctionSourceFilePath(string functionName);
  SourceFileDebugInfo FindSourceFilePathByRVA(long rva);
  SourceLineDebugInfo FindSourceLineByRVA(long rva);
}

[ProtoContract(SkipConstructor = true)]
public class SymbolFileSourceOptions : SettingsBase {
  private const string DefaultSymbolSourcePath = @"https://symweb";
  private const string DefaultPublicSymbolSourcePath = @"https://msdl.microsoft.com/download/symbols";
  private const string DefaultSymbolCachePath = @"C:\Symbols";

  public SymbolFileSourceOptions() {
    Reset();
  }

  [ProtoMember(1)]
  public bool SymbolSourcePathEnabled { get; set; }
  [ProtoMember(2)]
  public string SymbolSourcePath { get; set; }
  [ProtoMember(3)]
  public bool SymbolCachePathEnabled { get; set; }
  [ProtoMember(4)]
  public string SymbolCachePath { get; set; }
  [ProtoMember(5)]
  public bool SymbolSearchPathsEnabled { get; set; }
  [ProtoMember(6)]
  public List<string> SymbolSearchPaths { get; set; }
  [ProtoMember(7)]
  public bool SourceServerEnabled { get; set; }
  [ProtoMember(8)]
  public bool AuthorizationTokenEnabled { get; set; }
  [ProtoMember(9)]
  public string AuthorizationToken { get; set; }
  public bool HasSymbolSourcePath => !string.IsNullOrEmpty(SymbolSourcePath);
  public bool HasSymbolCachePath => !string.IsNullOrEmpty(SymbolCachePath);
  public bool HasAuthorizationToken => AuthorizationTokenEnabled && !string.IsNullOrEmpty(AuthorizationToken);

  public void ResetDefaultSymbolPath(bool privateSymServer) {
    SymbolSourcePath = privateSymServer ? DefaultSymbolSourcePath : DefaultPublicSymbolSourcePath;
  }

  public bool HasSymbolPath(string path) {
    path = Utils.TryGetDirectoryName(path).ToLowerInvariant();
    return SymbolSearchPaths.Find(item => item.ToLowerInvariant() == path) != null;
  }

  public void InsertSymbolPath(string path) {
    if (string.IsNullOrEmpty(path)) {
      return;
    }

    if (path.Contains(";")) {
      InsertSymbolPaths(path.Split(";"));
      return;
    }

    if (!path.Contains("*")) {
      if (HasSymbolPath(path)) {
        return;
      }

      path = Utils.TryGetDirectoryName(path);

      if (!string.IsNullOrEmpty(path)) {
        SymbolSearchPaths.Insert(0, path);
      }

      return;
    }

    string[] tokens = path.Split("*");

    if (tokens[0] == "srv") {
      string srv = tokens[1];

      if (tokens.Length == 3) {
        SymbolCachePath = srv;
        srv = tokens[2];
      }

      SymbolSourcePath = string.IsNullOrEmpty(srv) ? DefaultSymbolSourcePath : srv;
    }
    else if (tokens[0] == "cache") {
      string cache = tokens[1];
      SymbolCachePath = string.IsNullOrEmpty(cache) ? DefaultSymbolCachePath : cache;
    }
  }

  public void InsertSymbolPaths(IEnumerable<string> paths) {
    foreach (string path in paths) {
      InsertSymbolPath(path);
    }
  }

  public SymbolFileSourceOptions WithSymbolPaths(params string[] paths) {
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
    //? TODO: This should not be hardcoded

    try {
      var domain = System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().DomainName;

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
      var ptrJoinInfo = IntPtr.Zero;
      var ptrUserInfo = IntPtr.Zero;
      var ptrJoinCertificate = IntPtr.Zero;
      NetAPI32.DSREG_JOIN_INFO joinInfo = new NetAPI32.DSREG_JOIN_INFO();

      NetAPI32.NetFreeAadJoinInformation(IntPtr.Zero);
      var retValue = NetAPI32.NetGetAadJoinInformation(pcszTenantId, out ptrJoinInfo);

      if (retValue == 0) {
        NetAPI32.DSREG_JOIN_INFO ptrJoinInfoObject = new NetAPI32.DSREG_JOIN_INFO();
        joinInfo = (NetAPI32.DSREG_JOIN_INFO)System.Runtime.InteropServices.Marshal.PtrToStructure(ptrJoinInfo, (System.Type)ptrJoinInfoObject.GetType());

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

    ResetDefaultSymbolPath(ShouldUsePrivateSymbolPath());
    SymbolSourcePathEnabled = true;
    SymbolCachePath = DefaultSymbolCachePath;
    SymbolCachePathEnabled = true;
  }

  public SymbolFileSourceOptions Clone() {
    byte[] serialized = StateSerializer.Serialize(this);
    return StateSerializer.Deserialize<SymbolFileSourceOptions>(serialized);
  }

  [ProtoAfterDeserialization]
  private void InitializeReferenceMembers() {
    SymbolSearchPaths ??= new List<string>();
  }
}

[ProtoContract(SkipConstructor = true)]
public class SymbolFileDescriptor : IEquatable<SymbolFileDescriptor> {
  public SymbolFileDescriptor(string fileName, Guid id, int age) {
    FileName = fileName != null ? string.Intern(fileName) : null;
    Id = id;
    Age = age;
  }

  public SymbolFileDescriptor(string fileName) {
    FileName = fileName != null ? string.Intern(fileName) : null;
  }

  [ProtoMember(1)]
  public string FileName { get; set; }
  [ProtoMember(2)]
  public Guid Id { get; set; }
  [ProtoMember(3)]
  public int Age { get; set; }

  public static bool operator ==(SymbolFileDescriptor left, SymbolFileDescriptor right) {
    return Equals(left, right);
  }

  public static bool operator !=(SymbolFileDescriptor left, SymbolFileDescriptor right) {
    return !Equals(left, right);
  }

  public override string ToString() {
    return $"{Id}:{FileName}";
  }

  public override bool Equals(object obj) {
    if (ReferenceEquals(null, obj)) {
      return false;
    }

    if (ReferenceEquals(this, obj)) {
      return true;
    }

    if (obj.GetType() != GetType()) {
      return false;
    }

    return Equals((SymbolFileDescriptor)obj);
  }

  public override int GetHashCode() {
    return HashCode.Combine(FileName.GetHashCode(StringComparison.OrdinalIgnoreCase), Id, Age);
  }

  public bool Equals(SymbolFileDescriptor other) {
    return FileName.Equals(other.FileName, StringComparison.OrdinalIgnoreCase) &&
           Id == other.Id &&
           Age == other.Age;
  }
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