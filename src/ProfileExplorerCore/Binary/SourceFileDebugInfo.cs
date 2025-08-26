// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using ProtoBuf;

namespace ProfileExplorerCore.Binary;

[ProtoContract(SkipConstructor = true)]
public struct SourceFileDebugInfo : IEquatable<SourceFileDebugInfo> {
  public SourceFileDebugInfo(string filePath, string originalFilePath, int startLine = 0,
                             bool hasChecksumMismatch = false) {
    FilePath = filePath != null ? string.Intern(filePath) : null;
    OriginalFilePath = originalFilePath != null ? string.Intern(originalFilePath) : null;
    StartLine = startLine;
    HasChecksumMismatch = hasChecksumMismatch;
  }

  [ProtoMember(1)]
  public string FilePath { get; set; }
  [ProtoMember(2)]
  public string OriginalFilePath { get; set; }
  [ProtoMember(3)]
  public int StartLine { get; set; }
  [ProtoMember(4)]
  public bool HasChecksumMismatch { get; set; }
  public static readonly SourceFileDebugInfo Unknown = new(null, null, -1);
  public bool IsUnknown => FilePath == null;
  public bool HasFilePath => !string.IsNullOrEmpty(FilePath);
  public bool HasOriginalFilePath => !string.IsNullOrEmpty(OriginalFilePath);

  public bool Equals(SourceFileDebugInfo other) {
    return FilePath.Equals(other.FilePath, StringComparison.Ordinal) &&
           OriginalFilePath.Equals(other.OriginalFilePath, StringComparison.Ordinal) &&
           StartLine == other.StartLine &&
           HasChecksumMismatch == other.HasChecksumMismatch;
  }

  public override bool Equals(object obj) {
    return obj is SourceFileDebugInfo other && Equals(other);
  }

  public override int GetHashCode() {
    return HashCode.Combine(FilePath, OriginalFilePath, StartLine, HasChecksumMismatch);
  }
}