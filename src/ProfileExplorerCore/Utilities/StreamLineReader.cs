// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.IO;
using System.Text;

namespace ProfileExplorer.Core;

public class StreamLineReader : IDisposable {
  private const int BufferLength = 8192;
  private byte[] buffer_ = new byte[BufferLength];
  private int bytesRead_;
  private long currentPosition_;
  private Stream fileStream_;
  private int index_;

  public StreamLineReader(Stream stream) {
    fileStream_ = stream;
  }

  /// <summary>
  ///   CurrentLine number
  /// </summary>
  public long CurrentPosition => currentPosition_;

  public void Seek(long offset) {
    fileStream_.Seek(offset, SeekOrigin.Begin);
    bytesRead_ = 0;
  }

  public string ReadLine() {
    var builder = new StringBuilder();
    bool found = false;

    while (!found) {
      if (bytesRead_ <= 0) {
        index_ = 0;
        bytesRead_ = fileStream_.Read(buffer_, 0, BufferLength);

        if (bytesRead_ == 0) {
          if (builder.Length > 0) {
            break;
          }

          return null;
        }
      }

      for (int max = index_ + bytesRead_; index_ < max;) {
        char ch = (char)buffer_[index_];
        bytesRead_--;
        index_++;
        currentPosition_++;

        if (ch == '\0' || ch == '\n') {
          found = true;
          break;
        }

        if (ch != '\r') {
          builder.Append(ch);
        }
      }
    }

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