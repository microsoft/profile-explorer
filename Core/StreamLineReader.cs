// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Text;

namespace Core {
    public class StreamLineReader : IDisposable {
        const int BufferLength = 8192;

        Stream fileStream_;
        int bytesRead_ = 0;
        int index_ = 0;
        byte[] buffer_ = new byte[BufferLength];

        long currentPosition_ = 0;
        int currentLine_ = 0;

        /// <summary>
        /// CurrentLine number
        /// </summary>
        public long CurrentPosition { get { return currentPosition_; } }

        public StreamLineReader(Stream stream) { fileStream_ = stream; }


        public void Seek(long offset) {
            fileStream_.Seek(offset, SeekOrigin.Begin);
            bytesRead_ = 0;
        }

        public string ReadLine() {
            StringBuilder builder = new StringBuilder();
            bool found = false;

            while (!found) {
                if (bytesRead_ <= 0) {
                    index_ = 0;
                    bytesRead_ = fileStream_.Read(buffer_, 0, BufferLength);

                    if (bytesRead_ == 0) {
                        if (builder.Length > 0) break;
                        return null;
                    }
                }

                for (int max = index_ + bytesRead_; index_ < max;) {
                    char ch = (char)buffer_[index_];
                    bytesRead_--; index_++;
                    currentPosition_++;

                    if (ch == '\0' || ch == '\n') {
                        found = true;
                        break;
                    }
                    else if (ch == '\r') continue;
                    else builder.Append(ch);
                }
            }

            currentLine_++;
            return builder.ToString();
        }

        public void Dispose() {
            if (fileStream_ != null) {
                fileStream_.Close();
                fileStream_.Dispose();
                fileStream_ = null;
            }
        }
    }
}
