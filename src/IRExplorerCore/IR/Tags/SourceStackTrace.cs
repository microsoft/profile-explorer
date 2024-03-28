// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Text;

namespace IRExplorerCore.IR;

public sealed class SourceStackFrame : IEquatable<SourceStackFrame> {
  public SourceStackFrame(string function, string filePath, int line, int column) {
    Function = function;
    FilePath = filePath;
    Line = line;
    Column = column;
  }

  public string Function { get; set; }
  public string FilePath { get; set; }
  public int Line { get; set; }
  public int Column { get; set; }

  public static bool operator ==(SourceStackFrame left, SourceStackFrame right) {
    return Equals(left, right);
  }

  public static bool operator !=(SourceStackFrame left, SourceStackFrame right) {
    return !Equals(left, right);
  }

  public override bool Equals(object obj) {
    return ReferenceEquals(this, obj) || obj is SourceStackFrame other && Equals(other);
  }

  public override int GetHashCode() {
    return HashCode.Combine(Function, FilePath, Line, Column);
  }

  public bool Equals(SourceStackFrame other) {
    if (ReferenceEquals(null, other))
      return false;
    if (ReferenceEquals(this, other))
      return true;
    return Line == other.Line && Column == other.Column &&
           Function.Equals(other.Function, StringComparison.OrdinalIgnoreCase) &&
           FilePath.Equals(other.FilePath, StringComparison.OrdinalIgnoreCase);
  }

  public bool HasSameFunction(SourceStackFrame inlinee) {
    return Function.Equals(inlinee.Function, StringComparison.OrdinalIgnoreCase) &&
           FilePath.Equals(inlinee.FilePath, StringComparison.OrdinalIgnoreCase);
  }
}

public sealed class SourceStackTrace {
  public SourceStackTrace() {
    Frames = new List<SourceStackFrame>();
  }

  public SourceStackTrace(IEnumerable<SourceStackFrame> frames) : this() {
    AddFrames(frames);
  }

  public List<SourceStackFrame> Frames { get; set; }
  public byte[] Signature { get; set; }

  public void AddFrames(IEnumerable<SourceStackFrame> frames) {
    Frames.AddRange(frames);
    UpdateSignature();
  }

  public override bool Equals(object obj) {
    return obj is SourceStackTrace trace &&
           EqualityComparer<byte[]>.Default.Equals(Signature, trace.Signature);
  }

  public override int GetHashCode() {
    return HashCode.Combine(Signature);
  }

  public void UpdateSignature() {
    // Compute a hash that identifies the stack trace
    // to speed up equality check.
    var bytesList = new List<byte[]>(Frames.Count);

    foreach (var frame in Frames) {
      bytesList.Add(Encoding.UTF8.GetBytes(frame.Function));
      bytesList.Add(Encoding.UTF8.GetBytes(frame.FilePath));
      bytesList.Add(BitConverter.GetBytes(frame.Line));
      bytesList.Add(BitConverter.GetBytes(frame.Column));
    }

    Signature = CompressionUtils.CreateSHA256(bytesList);
  }
}