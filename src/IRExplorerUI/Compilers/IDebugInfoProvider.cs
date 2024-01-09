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
  bool PopulateSourceLines(FunctionDebugInfo funcInfo);
  SourceFileDebugInfo FindFunctionSourceFilePath(IRTextFunction textFunc);
  SourceFileDebugInfo FindFunctionSourceFilePath(string functionName);
  SourceFileDebugInfo FindSourceFilePathByRVA(long rva);
  SourceLineDebugInfo FindSourceLineByRVA(long rva);
}

[ProtoContract(SkipConstructor = true)]
public class SymbolFileSourceOptions : SettingsBase {
  private const string DefaultPrivateSymbolServer = @"https://symweb";
  private const string DefaultPublicSymbolServer = @"https://msdl.microsoft.com/download/symbols";
  private const string DefaultSymbolCachePath = @"C:\Symbols";

  public SymbolFileSourceOptions() {
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
  public bool HasAuthorizationToken => AuthorizationTokenEnabled && !string.IsNullOrEmpty(AuthorizationToken);

  public void AddSymbolServer(bool usePrivateServer) {
    string symbolServer = usePrivateServer ? DefaultPrivateSymbolServer : DefaultPublicSymbolServer;
    SymbolPaths.Add($"srv*{DefaultSymbolCachePath}*{symbolServer}");
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
      SymbolPaths.Add(p);
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

  public override void Reset() {
    InitializeReferenceMembers();

    AddSymbolServer(usePrivateServer : true);
    AddSymbolServer(usePrivateServer: false);
  }

  public SymbolFileSourceOptions Clone() {
    byte[] serialized = StateSerializer.Serialize(this);
    return StateSerializer.Deserialize<SymbolFileSourceOptions>(serialized);
  }

  [ProtoAfterDeserialization]
  private void InitializeReferenceMembers() {
    SymbolPaths ??= new List<string>();
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
