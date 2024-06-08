using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text.Json;
using System.IO;
using System.Security;
using System.IO.Compression;
using System.Diagnostics;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace IRExplorerCore.FileFormat;

public sealed class FileArchive : IDisposable {
  private const string HeaderFilePath = "HEADER-4F1B8FB1-DD26-4D60-B275-6C0588668EE6";
  private static readonly Guid FileSignature = new Guid("80BCEE32-5110-4A25-87D3-D359B0C2634C");
  private const int FileBufferSize = 128 * 1024;
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

    public T GetOptionalData<T>() {
      if (OptionalData is JsonElement json) {
        return json.Deserialize<T>();
      }

      return (T)OptionalData;
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

  private FileArchive(Stream stream, bool openForWrite,
                      CompressionLevel level = CompressionLevel.Fastest) {
    try {
      var mode = openForWrite ? ZipArchiveMode.Create : ZipArchiveMode.Read;
      archive_ = new ZipArchive(stream, mode, leaveOpen: false);
    }
    catch {
      stream?.Dispose();
      throw;
    }

    archiveStream_ = stream;
    compressionLevel_ = level;
    CreateHeader();
  }

  public delegate void FileProgressDelegate(int currentFile, int totalFiles);
  public event FileProgressDelegate OnFileAdded; // Notified when adding multiple files only.
  public event FileProgressDelegate OnFileExtracted; // Notified when extracting multiple files only.
  public List<FileEntry> Files => header_.Files;
  public int FileCount => Files.Count;
  public long UncompressedSize => Files.Sum(entry => entry.Size);
  public bool HasOptionalData => header_.OptionalData != null;

  // Optional data that can be serialized together with the header.
  public void SetOptionalData(object dataObject) {
    header_.OptionalData = dataObject;
  }

  public T GetOptionalData<T>() {
    return header_.GetOptionalData<T>();
  }

  public IEnumerable<FileEntry> FindFilesOfKind(int kind) {
    foreach (var entry in Files) {
      if (entry.Kind == kind) {
        yield return entry;
      }
    }
  }

  public bool HasFilesOfKind(int kind) {
    foreach (var entry in FindFilesOfKind(kind)) {
      return true;
    }

    return false;
  }

  public IEnumerable<FileEntry> FindFilesInDirectory(string directoryPath, int optionalKind = 0) {
    foreach (var entry in Files) {
      if (optionalKind != 0 && entry.Kind != optionalKind) {
        continue; // Filter based on kind.
      }

      var entryDir = entry.Directory;

      if (entryDir == directoryPath) {
        yield return entry;
      }

      // For a directoryPath foo accept files in dirs like
      // foo/file.ext and foo/bar/file.ext, but reject
      // foobar/file.ext and bar/foo/file.ext and bar/foofile.ext.
      int prefixIndex = entryDir.IndexOf(directoryPath, StringComparison.Ordinal);

      if (prefixIndex == 0 &&
          (prefixIndex + directoryPath.Length == entryDir.Length - 1 ||
           entryDir[prefixIndex + directoryPath.Length] == Path.DirectorySeparatorChar)) {
        yield return entry;
      }
    }
  }

  public static async Task<FileArchive> LoadAsync(string filePath) {
    try {
      var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                                  bufferSize: FileBufferSize, useAsync: false);
      var archive = new FileArchive(stream, false);

      if (!(await archive.LoadHeader().ConfigureAwait(false))) {
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

  public static async Task<FileArchive> LoadOrCreateAsync(string filePath,
                                                          CompressionLevel compressionLevel =
                                                            CompressionLevel.Fastest) {
    return await CreateAsyncImpl(filePath, compressionLevel, false, true).ConfigureAwait(false);
  }

  public static async Task<FileArchive> CreateAsync(string filePath,
                                                    CompressionLevel compressionLevel = CompressionLevel.Fastest,
                                                    bool overwriteExisting = true) {
    return await CreateAsyncImpl(filePath, compressionLevel, overwriteExisting, false).ConfigureAwait(false);
  }

  private static async Task<FileArchive> CreateAsyncImpl(string filePath,
                                                         CompressionLevel compressionLevel = CompressionLevel.Fastest,
                                                         bool overwriteExisting = true,
                                                         bool loadExisting = false) {
    try {
      Debug.Assert(!(overwriteExisting && loadExisting));

      if (File.Exists(filePath)) {
        if (overwriteExisting) {
          File.Delete(filePath);
        }
        else if (loadExisting) {
          return await LoadAsync(filePath).ConfigureAwait(false);
        }
        else {
          return null;
        }
      }

      var stream = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                                  bufferSize: FileBufferSize, useAsync: false);
      var archive = new FileArchive(stream, true, compressionLevel);
      return archive;
    }
    catch (Exception ex) {
      Trace.WriteLine($"Failed to create archive {filePath}: {ex.Message}");
      return null;
    }
  }

  public async Task<bool> SaveAsync() {
    await SaveHeader().ConfigureAwait(false);
    Close();
    return true;
  }

  private void Close() {
    archive_?.Dispose();
    archive_ = null;
    archiveStream_ = null;
    modified_ = false;
  }

  public async Task<bool> AddFileAsync(string sourceFilePath, int fileKind = 0, string optionalDirectory = null,
                                       bool keepExisting = false) {
    try {
      await using var sourceStream = File.OpenRead(sourceFilePath);
      var archiveFilePath = Path.GetFileName(sourceFilePath);

      if (!string.IsNullOrEmpty(optionalDirectory)) {
        archiveFilePath = Path.Combine(optionalDirectory, archiveFilePath);
      }

      return await AddFileStreamAsync(sourceStream, archiveFilePath, fileKind, keepExisting).ConfigureAwait(false);
    }
    catch (Exception ex) {
      Trace.WriteLine($"Failed to add file {sourceFilePath}: {ex.Message}");
      return false;
    }
  }

  public async Task<bool> AddFilesAsync(IEnumerable<string> sourceFilePaths, int fileKind = 0,
                                        string optionalDirectory = null,
                                        bool keepExisting = false) {
    try {
      var files = sourceFilePaths.ToList();
      int index = 0;
      OnFileAdded?.Invoke(0, files.Count);

      foreach (var filePath in files) {
        await using var sourceStream = File.OpenRead(filePath);
        var archiveFilePath = Path.GetFileName(filePath);

        if (!string.IsNullOrEmpty(optionalDirectory)) {
          archiveFilePath = Path.Combine(optionalDirectory, archiveFilePath);
        }

        if (!(await AddFileStreamAsync(sourceStream, archiveFilePath, fileKind, keepExisting).ConfigureAwait(false))) {
          return false;
        }

        OnFileAdded?.Invoke(++index, files.Count);
      }

      return true;
    }
    catch (Exception ex) {
      Trace.WriteLine($"Failed to add file collection: {ex.Message}");
      return false;
    }
  }

  public async Task<bool> AddDirectoryAsync(string directoryPath, bool includeSubdirs = false,
                                            int fileKind = 0,
                                            string searchPattern = "*", string optionalDirectory = null,
                                            bool keepExisting = false) {
    try {
      var searchOption = includeSubdirs ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
      var files = Directory.EnumerateFiles(directoryPath, searchPattern, searchOption).ToList();
      int index = 0;
      OnFileAdded?.Invoke(0, files.Count);

      foreach (var file in files) {
        // If file is in a directoryPath subdir, create the corresponding
        // subdir in the archive too, combined with the subdir force by client.
        string subdirPath = Path.GetRelativePath(directoryPath, file);
        subdirPath = Path.GetDirectoryName(subdirPath);

        if (!string.IsNullOrEmpty(optionalDirectory)) {
          subdirPath = Path.Combine(optionalDirectory, subdirPath);
        }

        if (!(await AddFileAsync(file, fileKind, subdirPath, keepExisting).ConfigureAwait(false))) {
          return false;
        }

        OnFileAdded?.Invoke(++index, files.Count);
      }

      return true;
    }
    catch (Exception ex) {
      Trace.WriteLine($"Failed to add file collection: {ex.Message}");
      return false;
    }
  }

  public async Task<bool> AddFileStreamAsync(Stream sourceStream,
                                             string archiveFilePath,
                                             int fileKind = 0,
                                             bool keepExisting = false) {
    try {
      var existingEntry = header_.FindFile(archiveFilePath);

      if (existingEntry != null) {
        if (keepExisting) {
          return true;
        }

        await RemoveFile(archiveFilePath).ConfigureAwait(false);
      }

      var newEntry = archive_.CreateEntry(archiveFilePath, compressionLevel_);
      await using var entryStream = newEntry.Open();
      await sourceStream.CopyToAsync(entryStream).ConfigureAwait(false);

      if (archiveFilePath != HeaderFilePath) {
        header_.AddFile(archiveFilePath, fileKind, sourceStream.Length);
      }

      modified_ = true;
      return true;
    }
    catch (Exception ex) {
      Trace.WriteLine($"Failed to add file {archiveFilePath}: {ex.Message}");
      return false;
    }
  }

  public async Task<bool> RemoveFile(string archiveFilePath) {
    var entry = archive_.GetEntry(archiveFilePath);

    if (entry != null) {
      header_.RemoveFile(archiveFilePath);
      entry.Delete();
      modified_ = true;
      return true;
    }

    return false;
  }

  public async Task<bool> ExtractFilesToDirectoryAsync(IEnumerable<string> files, string directoryPath,
                                                       bool preserveArchiveDirs = true,
                                                       bool overwriteExisting = true) {
    int index = 0;
    OnFileExtracted?.Invoke(0, Files.Count);

    foreach (var file in files) {
      if (!await ExtractFileToDirectoryAsync(file, directoryPath,
                                             preserveArchiveDirs, overwriteExisting).ConfigureAwait(false)) {
        return false;
      }

      OnFileExtracted?.Invoke(++index, Files.Count);
    }

    return true;
  }

  public async Task<bool> ExtractFilesToDirectoryAsync(IEnumerable<FileEntry> files, string directoryPath,
                                                       bool preserveArchiveDirs = true,
                                                       bool overwriteExisting = true) {
    int index = 0;
    OnFileExtracted?.Invoke(0, Files.Count);

    foreach (var file in files) {
      if (!await ExtractFileToDirectoryAsync(file, directoryPath,
                                             preserveArchiveDirs, overwriteExisting).ConfigureAwait(false)) {
        return false;
      }

      OnFileExtracted?.Invoke(++index, Files.Count);
    }

    return true;
  }

  public async Task<bool> ExtractAllFilesOfKindToDirectoryAsync(int kind, string directoryPath,
                                                                string optionalArchiveDirectory = null,
                                                                bool preserveArchiveDirs = true,
                                                                bool overwriteExisting = true) {
    var files = !string.IsNullOrEmpty(optionalArchiveDirectory) ?
      FindFilesInDirectory(optionalArchiveDirectory, kind) :
      FindFilesOfKind(kind);
    int index = 0;
    OnFileExtracted?.Invoke(0, Files.Count);

    foreach (var file in files) {
      if (!await ExtractFileToDirectoryAsync(file, directoryPath,
                                             preserveArchiveDirs, overwriteExisting).ConfigureAwait(false)) {
        return false;
      }

      OnFileExtracted?.Invoke(++index, Files.Count);
    }

    return true;
  }

  public async Task<bool> ExtractFileToDirectoryAsync(FileEntry file, string directoryPath,
                                                      bool preserveArchiveDirs = true,
                                                      bool overwriteExisting = true) {
    return await ExtractFileToDirectoryAsync(file.ArchivePath, directoryPath,
                                             preserveArchiveDirs, overwriteExisting).ConfigureAwait(false);
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

      return await ExtractFileAsync(archiveFilePath, outFilePath, overwriteExisting).ConfigureAwait(false);
    }
    catch (Exception ex) {
      Trace.WriteLine($"Failed to add file {archiveFilePath}: {ex.Message}");
      return false;
    }
  }

  public async Task<bool> ExtractAllToDirectoryAsync(string directoryPath,
                                                     bool preserveArchiveDirs = true,
                                                     bool overwriteExisting = true) {
    int index = 0;
    OnFileExtracted?.Invoke(0, Files.Count);

    foreach (var file in Files) {
      if (!(await ExtractFileToDirectoryAsync(file, directoryPath,
                                              preserveArchiveDirs, overwriteExisting).ConfigureAwait(false))) {
        return false;
      }

      OnFileExtracted?.Invoke(++index, Files.Count);
    }

    return true;
  }

  public static async Task<bool> ExtractAllToDirectoryAsync(string archivePath,
                                                            string directoryPath,
                                                            bool preserveArchiveDirs = true,
                                                            bool overwriteExisting = true) {
    using var archive = await LoadAsync(archivePath).ConfigureAwait(false);

    if (archive == null) {
      return false;
    }

    return await archive.ExtractAllToDirectoryAsync(directoryPath, preserveArchiveDirs, overwriteExisting).
      ConfigureAwait(false);
  }

  public static async Task<bool> CreateFromFileAsync(string sourceFilePath,
                                                     string archivePath,
                                                     CompressionLevel compressionLevel = CompressionLevel.Fastest,
                                                     int fileKind = 0,
                                                     bool overwriteExisting = true) {
    using var archive = await CreateAsync(archivePath, compressionLevel, overwriteExisting).ConfigureAwait(false);

    if (archive == null ||
        !(await archive.AddFileAsync(sourceFilePath, fileKind, null, false).ConfigureAwait(false))) {
      return false;
    }

    return await archive.SaveAsync().ConfigureAwait(false);
  }

  public static async Task<bool> CreateFromFilesAsync(IEnumerable<string> sourceFilePaths,
                                                      string archivePath,
                                                      CompressionLevel compressionLevel = CompressionLevel.Fastest,
                                                      int fileKind = 0,
                                                      bool overwriteExisting = true) {
    using var archive = await CreateAsync(archivePath, compressionLevel, overwriteExisting).ConfigureAwait(false);

    if (archive == null ||
        !(await archive.AddFilesAsync(sourceFilePaths, fileKind, null, false).ConfigureAwait(false))) {
      return false;
    }

    return await archive.SaveAsync().ConfigureAwait(false);
  }

  public static async Task<bool> CreateFromStreamAsync(Stream sourceStream,
                                                       string sourceFilePath,
                                                       string archivePath,
                                                       CompressionLevel compressionLevel = CompressionLevel.Fastest,
                                                       int fileKind = 0,
                                                       bool overwriteExisting = true) {
    using var archive = await CreateAsync(archivePath, compressionLevel, overwriteExisting).ConfigureAwait(false);

    if (archive == null ||
        !(await archive.AddFileStreamAsync(sourceStream, sourceFilePath, fileKind, false).ConfigureAwait(false))) {
      return false;
    }

    return await archive.SaveAsync().ConfigureAwait(false);
  }

  public static async Task<bool> CreateFromDirectoryAsync(string directoryPath,
                                                          string archivePath,
                                                          CompressionLevel compressionLevel = CompressionLevel.Fastest,
                                                          bool includeSubdirs = false,
                                                          string searchPattern = "*",
                                                          int fileKind = 0,
                                                          bool overwriteExisting = true) {
    using var archive = await CreateAsync(archivePath, compressionLevel, overwriteExisting).ConfigureAwait(false);

    if (archive == null ||
        !(await archive.AddDirectoryAsync(directoryPath, includeSubdirs, fileKind, searchPattern, null, false).
          ConfigureAwait(false))) {
      return false;
    }

    return await archive.SaveAsync().ConfigureAwait(false);
  }

  public async Task<bool> ExtractFileAsync(FileEntry file, string outFilePath,
                                           bool overwriteExisting = true) {
    return await ExtractFileAsync(file.ArchivePath, outFilePath, overwriteExisting).ConfigureAwait(false);
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

      await using var stream = new FileStream(outFilePath, FileMode.CreateNew, FileAccess.Write,
                                              FileShare.None, bufferSize: FileBufferSize, useAsync: false);
      return await ExtractFileAsync(archiveFilePath, stream).ConfigureAwait(false);
    }
    catch (Exception ex) {
      Trace.WriteLine($"Failed to extract file {archiveFilePath} to {outFilePath}: {ex.Message}");
      return false;
    }
  }

  public async Task<bool> ExtractFileAsync(FileEntry file, Stream outStream) {
    return await ExtractFileAsync(file.ArchivePath, outStream).ConfigureAwait(false);
  }

  public async Task<bool> ExtractFileAsync(string archiveFilePath, Stream outStream) {
    try {
      var entry = archive_.GetEntry(archiveFilePath);

      if (entry == null) {
        Trace.WriteLine($"File not found in archive: {archiveFilePath}");
        return false;
      }

      await using var entryStream = entry.Open();
      await entryStream.CopyToAsync(outStream).ConfigureAwait(false);
      return true;
    }
    catch (Exception ex) {
      Trace.WriteLine($"Failed to extract file {archiveFilePath}: {ex.Message}");
      return false;
    }
  }

  public async Task<MemoryStream> ExtractFileToMemoryAsync(string archiveFilePath) {
    var outStream = new MemoryStream();

    if (await ExtractFileAsync(archiveFilePath, outStream)) {
      return outStream;
    }
    else {
      outStream.Dispose();
      return null;
    }
  }

  public async Task<MemoryStream> ExtractFileToMemoryAsync(FileEntry file) {
    return await ExtractFileToMemoryAsync(file.ArchivePath);
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
    options.WriteIndented = true;
    await JsonSerializer.SerializeAsync<Header>(stream, header_, options).ConfigureAwait(false);
    stream.Position = 0;
    return await AddFileStreamAsync(stream, HeaderFilePath).ConfigureAwait(false);
  }

  private async Task<bool> LoadHeader() {
    using var stream = new MemoryStream();

    if (!await ExtractFileAsync(HeaderFilePath, stream).ConfigureAwait(false)) {
      // When opening a plain ZIP file, accept it by
      // populating the header with the existing files.
      stream.Position = 0;
      return CreateHeaderFromArchive();
    }

    stream.Position = 0;
    header_ = await JsonSerializer.DeserializeAsync<Header>(stream).ConfigureAwait(false);
    return ValidateHeader(header_);
  }

  private bool CreateHeaderFromArchive() {
    try {
      CreateHeader();

      foreach (var entry in archive_.Entries) {
        header_.AddFile(entry.FullName, 0, entry.Length);
      }

      return true;
    }
    catch (Exception ex) {
      Trace.WriteLine($"Failed to create header from archive contents");
      return false;
    }
  }

  private bool ValidateHeader(Header header) {
    return header.Signature == FileSignature &&
           header.Version is >= MinFileFormatVersion and <= FileFormatVersion;
  }

  private void Dispose(bool disposing) {
    if (!disposed_) {
      if (disposing) {
        if (modified_) {
          // Force saving the header if SaveAsync was not used.
          SaveAsync().ConfigureAwait(false).GetAwaiter().GetResult();
        }

        Close();
      }

      disposed_ = true;
    }
  }

  public void Dispose() {
    Dispose(disposing: true);
    GC.SuppressFinalize(this);
  }
}