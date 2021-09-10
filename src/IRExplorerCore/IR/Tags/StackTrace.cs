// // Copyright (c) Microsoft Corporation. All rights reserved.
// // Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Text;

namespace IRExplorerCore.IR {
    public class StackFrame {
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
            return obj is StackFrame frame &&
                   Function == frame.Function &&
                   FilePath == frame.FilePath &&
                   Line == frame.Line &&
                   Column == frame.Column;
        }

        public override int GetHashCode() {
            return HashCode.Combine(Function, FilePath, Line, Column);
        }
    }

    public class StackTrace {
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