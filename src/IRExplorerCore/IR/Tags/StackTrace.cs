// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Text;

namespace IRExplorerCore.IR {
    public sealed class StackFrame : IEquatable<StackFrame> {
        public StackFrame(string function, string filePath, int line, int column) {
            Function = function;
            FilePath = filePath;
            Line = line;
            Column = column;
        }

        public string Function { get; set; }
        public string FilePath { get; set; }
        public int Line { get; set; }
        public int Column { get; set; }

        public override bool Equals(object obj) {
            return ReferenceEquals(this, obj) || obj is StackFrame other && Equals(other);
        }

        public bool Equals(StackFrame other) {
            if (ReferenceEquals(null, other))
                return false;
            if (ReferenceEquals(this, other))
                return true;
            return Function == other.Function && FilePath == other.FilePath && 
                   Line == other.Line && Column == other.Column;
        }

        public static bool operator ==(StackFrame left, StackFrame right) {
            return Equals(left, right);
        }

        public static bool operator !=(StackFrame left, StackFrame right) {
            return !Equals(left, right);
        }

        public override int GetHashCode() {
            return HashCode.Combine(Function, FilePath, Line, Column);
        }
    }

    public sealed class StackTrace {
        public List<StackFrame> Frames { get; set; }
        public byte[] Signature { get; set; }

        public StackTrace() {
            Frames = new List<StackFrame>();
        }

        public StackTrace(IEnumerable<StackFrame> frames) : this() {
            AddFrames(frames);
        }

        public void AddFrames(IEnumerable<StackFrame> frames) {
            Frames.AddRange(frames);
            UpdateSignature();
        }

        public override bool Equals(object obj) {
            return obj is StackTrace trace &&
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
}