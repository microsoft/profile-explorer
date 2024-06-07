using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text.Json;
using System.IO;
using System.Security;
using System.IO.Compression;
using System.Diagnostics;
using System.ComponentModel.DataAnnotations;

namespace IRExplorerCore.FileFormat;

public sealed class FileArchive : IDisposable {
  private static string HeaderFilePath = "HEADER-4F1B8FB1-DD26-4D60-B275-6C0588668EE6";
  private static readonly Guid FileSignature = new Guid("80BCEE32-5110-4A25-87D3-D359B0C2634C");
  private const int FileFormatVersion = 1;
  private const int MinFileFormatVersion = 1;

  private struct Header {
    public Guid Signature { get; init; }
    public int Version { get; init; }
    public List<FileEntry> Files { get; init; }
    public object OptionalData { get; set; }

    public void AddFile(string path, int kind, long size) {
      Files.Add(new FileEntry(path, kind, size));
    }

    public FileEntry FindFile(string path) {
      return Files.Find(entry => entry.Name.Equals(path, StringComparison.Ordinal));
    }

    internal void RemoveFile(string path) {
      Files.RemoveAll(entry => entry.Name.Equals(path, StringComparison.Ordinal));
    }
  }

  public class FileEntry {
    public FileEntry(string archivePath, int kind, long size) {
      ArchivePath = archivePath;
      Kind = kind;
      Size = size;
    }

    public string ArchivePath { get; set; } // Path in archive, can have subdirs.
    public long Size { get; set; } // Uncompressed size.
    public int Kind { get; set; } // Optional kind set by client.
    public string Name => Path.GetFileName(ArchivePath);
    public string Extension => Path.GetExtension(ArchivePath);
    public string Directory => Path.GetDirectoryName(ArchivePath);
    public bool HasDirectory => !string.IsNullOrEmpty(Directory);
  }

  private Header header_;
  private ZipArchive archive_;
  private Stream archiveStream_;
  private CompressionLevel compressionLevel_;
  private bool modified_;
  private bool disposed_;

  private FileArchive(Stream stream, bool createNew,
                      CompressionLevel level = CompressionLevel.Fastest) {
    archive_ = new ZipArchive(stream, ZipArchiveMode.Update);
    archiveStream_ = stream;
    compressionLevel_ = level;
    CreateHeader();
  }

  public List<FileEntry> Files => header_.Files;
  public int FileCount => Files.Count;

  public object OptionalData {
    get => header_.OptionalData;
    set => header_.OptionalData = value;
  }

  public IEnumerable<FileEntry> GetFilesOfKind(int kind) {
    foreach (var entry in Files) {
      if (entry.Kind == kind) {
        yield return entry;
      }
    }
  }

  public bool HasFilesOfKind(int kind) {
    foreach (var entry in GetFilesOfKind(kind)) {
      return true;
    }

    return false;
  }

  public static async Task<FileArchive> LoadAsync(string filePath) {
    try {
      var stream = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite);
      var archive = new FileArchive(stream, false);

      if (!(await archive.LoadHeader())) {
        Trace.WriteLine($"Failed to validate archive header for {filePath}");
        return null;
      }

      return archive;
    }
    catch (Exception ex) {
      Trace.WriteLine($"Failed to load archive {filePath}: {ex.Message}");
      return null;
    }
  }

  public static async Task<FileArchive> LoadOrCreateAsync(string filePath, CompressionLevel compressionLevel) {
    return await CreateAsyncImpl(filePath, compressionLevel, false, true);
  }

  public static async Task<FileArchive> CreateAsync(string filePath, CompressionLevel compressionLevel,
                                                    bool overwriteExisting = true) {
    return await CreateAsyncImpl(filePath, compressionLevel, overwriteExisting, false);
  }

  private static async Task<FileArchive> CreateAsyncImpl(string filePath, CompressionLevel compressionLevel,
                                                         bool overwriteExisting = true,
                                                         bool loadExisting = false) {
    try {
      Debug.Assert(!(overwriteExisting && loadExisting));

      if (File.Exists(filePath)) {
        if (overwriteExisting) {
          File.Delete(filePath);
        }
        else if (loadExisting) {
          return await LoadAsync(filePath);
        }
        else {
          return null;
        }
      }

      var stream = new FileStream(filePath, FileMode.CreateNew, FileAccess.ReadWrite);
      var archive = new FileArchive(stream, true, compressionLevel);
      return archive;
    }
    catch (Exception ex) {
      Trace.WriteLine($"Failed to create archive {filePath}: {ex.Message}");
      return null;
    }
  }

  public async Task<bool> SaveAsync() {
    await SaveHeader();
    archive_.Dispose();
    archive_ = null;
    archiveStream_ = null;
    return true;
  }

  public async Task<bool> AddFileAsync(string sourceFilePath, string optionalDirectory = null,
                                       bool keepExisting = false, int fileKind = 0) {
    try {
      await using var sourceStream = File.OpenRead(sourceFilePath);
      var archiveFilePath = Path.GetFileName(sourceFilePath);

      if (!string.IsNullOrEmpty(optionalDirectory)) {
        archiveFilePath = Path.Combine(optionalDirectory, archiveFilePath);
      }

      return await AddFileStreamAsync(archiveFilePath, sourceStream, keepExisting, fileKind);
    }
    catch (Exception ex) {
      Trace.WriteLine($"Failed to add file {sourceFilePath}: {ex.Message}");
      return false;
    }
  }

  public async Task<bool> AddFilesAsync(IEnumerable<string> sourceFilePaths, string optionalDirectory = null,
                                        bool keepExisting = false, int fileKind = 0) {
    try {
      foreach (var filePath in sourceFilePaths) {
        await using var sourceStream = File.OpenRead(filePath);
        var archiveFilePath = Path.GetFileName(filePath);

        if (!string.IsNullOrEmpty(optionalDirectory)) {
          archiveFilePath = Path.Combine(optionalDirectory, archiveFilePath);
        }

        if (!(await AddFileStreamAsync(archiveFilePath, sourceStream, keepExisting, fileKind))) {
          return false;
        }
      }

      return true;
    }
    catch (Exception ex) {
      Trace.WriteLine($"Failed to add file collection: {ex.Message}");
      return false;
    }
  }

  public async Task<bool> AddDirectoryAsync(string directoryPath, bool includeSubdirs = false,
                                            string searchPattern = "*", string optionalDirectory = null,
                                            bool keepExisting = false, int fileKind = 0) {
    try {
      var searchOption = includeSubdirs ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
      var files = Directory.EnumerateFiles(directoryPath, searchPattern, searchOption);

      foreach (var file in files) {
        // If file is in a directoryPath subdir, create the correspondng
        // subdir in the archive too, combined with the subdir force by client.
        string subdirPath = Path.GetRelativePath(directoryPath, file);
        subdirPath = Path.GetDirectoryName(subdirPath);

        if (!string.IsNullOrEmpty(optionalDirectory)) {
          subdirPath = Path.Combine(optionalDirectory, subdirPath);
        }

        if (!(await AddFileAsync(file, subdirPath, keepExisting, fileKind))) {
          return false;
        }
      }

      return true;
    }
    catch (Exception ex) {
      Trace.WriteLine($"Failed to add file collection: {ex.Message}");
      return false;
    }
  }

  public async Task<bool> AddFileStreamAsync(string archiveFilePath, Stream sourceStream,
                                             bool keepExisting = false, int fileKind = 0) {
    try {
      var existingEntry = header_.FindFile(archiveFilePath);

      if (existingEntry != null) {
        if (keepExisting) {
          return true;
        }

        var entry = archive_.GetEntry(archiveFilePath);

        if (entry != null) {
          entry.Delete();
        }

        header_.RemoveFile(archiveFilePath);
      }

      var newEntry = archive_.CreateEntry(archiveFilePath, compressionLevel_);
      await using var entryStream = newEntry.Open();
      await sourceStream.CopyToAsync(entryStream);

      header_.AddFile(archiveFilePath, fileKind, sourceStream.Length);
      modified_ = true;
      return true;
    }
    catch (Exception ex) {
      Trace.WriteLine($"Failed to add file {archiveFilePath}: {ex.Message}");
      return false;
    }
  }

  public async Task<bool> ExtractFileToDirectoryAsync(FileEntry file, string directoryPath,
                                                      bool preserveArchiveDirs = true,
                                                      bool overwriteExisting = true) {
    return await ExtractFileToDirectoryAsync(file.ArchivePath, directoryPath, preserveArchiveDirs, overwriteExisting);
  }

  public async Task<bool> ExtractFileToDirectoryAsync(string archiveFilePath, string directoryPath,
                                                      bool preserveArchiveDirs = true,
                                                      bool overwriteExisting = true) {
    try {
      string outFilePath = preserveArchiveDirs
        ? Path.Combine(directoryPath, archiveFilePath)
        : Path.Combine(directoryPath, Path.GetFileName(archiveFilePath));

      var outFileDir = Path.GetDirectoryName(outFilePath);

      if (!string.IsNullOrEmpty(outFileDir) &&
          !Directory.Exists(outFileDir)) {
        Directory.CreateDirectory(outFileDir);
      }

      return await ExtractFileAsync(archiveFilePath, outFilePath, overwriteExisting);
    }
    catch (Exception ex) {
      Trace.WriteLine($"Failed to add file {archiveFilePath}: {ex.Message}");
      return false;
    }
  }

  public async Task<bool> ExtractAllToDirectoryAsync() {
    // Skip over header file
    return true;
  }

  public async Task<bool> ExtractFileAsync(FileEntry file, string outFilePath,
                                           bool overwriteExisting = true) {
    return await ExtractFileAsync(file.ArchivePath, outFilePath, overwriteExisting);
  }

  public async Task<bool> ExtractFileAsync(string archiveFilePath, string outFilePath,
                                           bool overwriteExisting = true) {
    try {
      if (File.Exists(outFilePath)) {
        if (overwriteExisting) {
          File.Delete(outFilePath);
        }
        else {
          return false;
        }
      }

      await using var stream = new FileStream(outFilePath, FileMode.CreateNew, FileAccess.Write);
      return await ExtractFileAsync(archiveFilePath, stream);
    }
    catch (Exception ex) {
      Trace.WriteLine($"Failed to extract file {archiveFilePath} to {outFilePath}: {ex.Message}");
      return false;
    }
  }

  public async Task<bool> ExtractFileAsync(FileEntry file, Stream outStream) {
    return await ExtractFileAsync(file.ArchivePath, outStream);
  }

  public async Task<bool> ExtractFileAsync(string archiveFilePath, Stream outStream) {
    try {
      var entry = archive_.GetEntry(archiveFilePath);

      if (entry == null) {
        Trace.WriteLine($"File not found in archive: {archiveFilePath}");
        return false;
      }

      await using var entryStream = entry.Open();
      await entryStream.CopyToAsync(outStream);
      return true;
    }
    catch (Exception ex) {
      Trace.WriteLine($"Failed to extract file {archiveFilePath}: {ex.Message}");
      return false;
    }
  }

  public FileEntry FindFile(string archiveFilePath) {
    try {
      var fileEntry = header_.FindFile(archiveFilePath);

      if (fileEntry != null && archive_.GetEntry(archiveFilePath) == null) {
        throw new InvalidDataException($"File missing for archive: {archiveFilePath}");
      }

      return fileEntry;
    }
    catch (Exception ex) {
      Trace.WriteLine($"Failed to find file {archiveFilePath}: {ex.Message}");
      return null;
    }
  }

  public bool HasFile(string archiveFilePath) {
    return FindFile(archiveFilePath) != null;
  }

  private void CreateHeader() {
    header_ = new Header() {
      Signature = FileSignature,
      Version = FileFormatVersion,
      Files = new()
    };
  }

  private async Task<bool> SaveHeader() {
    await using var stream = new MemoryStream();
    var options = new JsonSerializerOptions();
    options.IgnoreReadOnlyFields = true;
    options.IgnoreReadOnlyProperties = true;
    await JsonSerializer.SerializeAsync<Header>(stream, header_, options);
    stream.Position = 0;
    return await AddFileStreamAsync(HeaderFilePath, stream);
  }

  private async Task<bool> LoadHeader() {
    using var stream = new MemoryStream();

    if (await ExtractFileAsync(HeaderFilePath, stream)) {
      stream.Position = 0;
      header_ = await JsonSerializer.DeserializeAsync<Header>(stream);
      return ValidateHeader(header_);
    }

    return false;
  }

  private bool ValidateHeader(Header header) {
    return header.Signature == FileSignature &&
           header.Version is >= MinFileFormatVersion and <= FileFormatVersion;
  }

  private void Dispose(bool disposing) {
    if (!disposed_) {
      if (disposing) {
        //if (modified_) {
          archive_?.Dispose();
        //}

        archive_ = null;
        archiveStream_ = null;
      }

      disposed_ = true;
    }
  }

  public void Dispose() {
    Dispose(disposing: true);
    GC.SuppressFinalize(this);
  }
}