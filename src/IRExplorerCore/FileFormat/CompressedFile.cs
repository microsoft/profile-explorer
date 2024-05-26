// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text.Json;

namespace IRExplorerCore.FileFormat;

public class CompressedFile {
  private static string HeaderFilePath = "header.json";
  private static readonly Guid FileSignature = new Guid("80BCEE32-5110-4A25-87D3-D359B0C2634C");
  private const int FileFormatVersion = 1;
  private const int MinFileFormatVersion = 1;

  private struct Header {
    public Guid Signature { get; init; }
    public int Version { get; init; }
    public List<FileEntry> Files { get; init; }
}

  private struct FileEntry {
    public Guid Kind { get; set; }
    public string Path { get; set; }
  }

  private Header header_;

  public CompressedFile() {
    header_ = new();
  }

  public bool AddFile(string filePath, string sourceFilePath, Guid kind) {
    return true;
  }

  public async Task<bool> Load(string filePath) {
    return true;
  }

  public async Task<bool> Save(string filePath) {
    return true;
  }

  public async Task<bool> SaveEncrypted(string filePath, string password) {
    return true;
  }

  private Header CreateHeader() {
    return new Header() {
      Signature = FileSignature,
      Version = FileFormatVersion,
      Files = new()
    };
  }

  private bool ValidateHeader(Header header) {
    return header.Signature == FileSignature &&
           header.Version is >= MinFileFormatVersion and <= FileFormatVersion;
  }
}