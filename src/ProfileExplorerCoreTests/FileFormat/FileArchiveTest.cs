// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using ProfileExplorer.Core.FileFormat;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ProfileExplorerCoreTests.FileFormat;

[TestClass]
public class FileArchiveTest {
  private const string InPath = @"testFiles";
  private const string OutPath = @"outTestFiles";
  private const string ResultPath = @"resultTestFiles";

  [TestInitialize]
  public void Initialize() {
    Directory.CreateDirectory(InPath);
    Directory.CreateDirectory(OutPath);
    Directory.CreateDirectory(ResultPath);
  }

  [TestCleanup]
  public void Cleanup() {
    RecursiveDelete(new DirectoryInfo(InPath));
    RecursiveDelete(new DirectoryInfo(OutPath));
    RecursiveDelete(new DirectoryInfo(ResultPath));
  }

  [TestMethod]
  public async Task TestCreateAsync() {
    string path = $@"{ResultPath}\archive.zip";
    using var archive = await FileArchive.CreateAsync(path, CompressionLevel.Fastest);
    Assert.IsTrue(await archive.SaveAsync());

    Assert.IsTrue(File.Exists(path));
    Assert.IsTrue(new FileInfo(path).Length > 0);
  }

  [TestMethod]
  public async Task TestAddFileAsync() {
    string path = $@"{ResultPath}\archive.zip";
    string file1 = CreateTestFile($@"{InPath}\file1.txt", 1023);
    string file2 = CreateTestFile($@"{InPath}\file2.txt", 4095);

    using var archive = await FileArchive.CreateAsync(path, CompressionLevel.Fastest);
    Assert.IsTrue(await archive.AddFileAsync(file1));
    Assert.IsTrue(await archive.AddFileAsync(file2));
    Assert.IsTrue(await archive.SaveAsync());
    Assert.IsTrue(File.Exists(path));

    ZipFile.ExtractToDirectory(path, OutPath);
    Assert.IsTrue(AreFilesEqual(file1, $@"{OutPath}\file1.txt"));
    Assert.IsTrue(AreFilesEqual(file2, $@"{OutPath}\file2.txt"));
  }

  [TestMethod]
  public async Task TestAddFileStreamAsync() {
    string path = $@"{ResultPath}\archive.zip";
    var stream = CreateTestStream(1023);
    string file2 = CreateTestFile($@"{InPath}\file2.txt", 4095);

    using var archive = await FileArchive.CreateAsync(path, CompressionLevel.Fastest);
    Assert.IsTrue(await archive.AddFileStreamAsync(stream, @"file1.txt"));
    Assert.IsTrue(await archive.AddFileAsync(file2));
    Assert.IsTrue(await archive.SaveAsync());
    Assert.IsTrue(File.Exists(path));

    ZipFile.ExtractToDirectory(path, OutPath);
    Assert.IsTrue(AreFilesEqual(stream, $@"{OutPath}\file1.txt"));
    Assert.IsTrue(AreFilesEqual(file2, $@"{OutPath}\file2.txt"));
  }

  [TestMethod]
  public async Task TestAddDirectoryAsync() {
    string path = $@"{ResultPath}\archive.zip";
    string file1 = CreateTestFile($@"{InPath}\file1.txt", 1023);
    string file2 = CreateTestFile($@"{InPath}\file2.txt", 4095);

    using var archive = await FileArchive.CreateAsync(path, CompressionLevel.Fastest);
    Assert.IsTrue(await archive.AddDirectoryAsync(InPath));
    Assert.IsTrue(await archive.SaveAsync());
    Assert.IsTrue(File.Exists(path));

    ZipFile.ExtractToDirectory(path, OutPath);
    Assert.IsTrue(AreFilesEqual(file1, $@"{OutPath}\file1.txt"));
    Assert.IsTrue(AreFilesEqual(file2, $@"{OutPath}\file2.txt"));
  }

  [TestMethod]
  public async Task TestAddDirectoryWithSubdirsAsync() {
    string path = $@"{ResultPath}\archive.zip";

    Directory.CreateDirectory($@"{InPath}\subdir");
    string file1 = CreateTestFile($@"{InPath}\subdir\file1.txt", 1023);
    string file2 = CreateTestFile($@"{InPath}\subdir\file2.txt", 4095);
    string file3 = CreateTestFile($@"{InPath}\file3.txt", 1023);

    using var archive = await FileArchive.CreateAsync(path, CompressionLevel.Fastest);
    Assert.IsTrue(await archive.AddDirectoryAsync(InPath, true, searchPattern: "*", optionalDirectory: "clientSubdir"));
    Assert.IsTrue(await archive.SaveAsync());
    Assert.IsTrue(File.Exists(path));

    ZipFile.ExtractToDirectory(path, OutPath);
    Assert.IsTrue(AreFilesEqual(file1, $@"{OutPath}\clientSubdir\subdir\file1.txt"));
    Assert.IsTrue(AreFilesEqual(file2, $@"{OutPath}\clientSubdir\subdir\file2.txt"));
    Assert.IsTrue(AreFilesEqual(file3, $@"{OutPath}\clientSubdir\file3.txt"));
  }

  [TestMethod]
  public async Task TestAddDirectoryWithoutSubdirsAsync() {
    string path = $@"{ResultPath}\archive.zip";

    Directory.CreateDirectory($@"{InPath}\subdir");
    string file1 = CreateTestFile($@"{InPath}\subdir\file1.txt", 1023);
    string file2 = CreateTestFile($@"{InPath}\subdir\file2.txt", 4095);
    string file3 = CreateTestFile($@"{InPath}\file3.txt", 1023);

    using var archive = await FileArchive.CreateAsync(path, CompressionLevel.Fastest);
    Assert.IsTrue(await archive.AddDirectoryAsync(InPath, false));
    Assert.IsTrue(await archive.SaveAsync());
    Assert.IsTrue(File.Exists(path));

    ZipFile.ExtractToDirectory(path, OutPath);
    Assert.IsFalse(AreFilesEqual(file1, $@"{OutPath}\subdir\file1.txt"));
    Assert.IsFalse(AreFilesEqual(file2, $@"{OutPath}\subdir\file2.txt"));
    Assert.IsTrue(AreFilesEqual(file3, $@"{OutPath}\file3.txt"));
  }

  [TestMethod]
  public async Task TestAddDirectoryFilteringAsync() {
    string path = $@"{ResultPath}\archive.zip";
    string file1 = CreateTestFile($@"{InPath}\file1.txt", 1023);
    string file2 = CreateTestFile($@"{InPath}\file2.dat", 4095);
    string file3 = CreateTestFile($@"{InPath}\file3.dat", 4095);

    using var archive = await FileArchive.CreateAsync(path, CompressionLevel.Fastest);
    Assert.IsTrue(await archive.AddDirectoryAsync(InPath, false, searchPattern: "*.dat"));
    Assert.IsTrue(await archive.SaveAsync());
    Assert.IsTrue(File.Exists(path));

    ZipFile.ExtractToDirectory(path, OutPath);
    Assert.IsFalse(AreFilesEqual(file1, $@"{OutPath}\file1.txt"));
    Assert.IsTrue(AreFilesEqual(file2, $@"{OutPath}\file2.dat"));
    Assert.IsTrue(AreFilesEqual(file3, $@"{OutPath}\file3.dat"));
  }

  [TestMethod]
  public async Task TestLoadAsync() {
    string path = $@"{ResultPath}\archive.zip";
    string file1 = CreateTestFile($@"{InPath}\file1.txt", 1023);
    string file2 = CreateTestFile($@"{InPath}\file2.txt", 4095);

    using var archive = await FileArchive.CreateAsync(path, CompressionLevel.Fastest);
    Assert.IsTrue(await archive.AddFileAsync(file1));
    Assert.IsTrue(await archive.AddFileAsync(file2));
    Assert.IsTrue(await archive.SaveAsync());
    Assert.IsTrue(File.Exists(path));

    using var loadedArchive = await FileArchive.LoadAsync(path);
    Assert.IsNotNull(loadedArchive);
    Assert.IsTrue(loadedArchive.FileCount == 2);
    Assert.IsNotNull(loadedArchive.Files.Find(entry => entry.Name == "file1.txt"));
    Assert.IsNotNull(loadedArchive.Files.Find(entry => entry.Name == "file2.txt"));
  }

  [TestMethod]
  public async Task TestExtractFileToDirectoryAsync() {
    string path = $@"{ResultPath}\archive.zip";
    string file1 = CreateTestFile($@"{InPath}\file1.txt", 1023);
    string file2 = CreateTestFile($@"{InPath}\file2.txt", 4095);

    using var archive = await FileArchive.CreateAsync(path, CompressionLevel.Fastest);
    Assert.IsTrue(await archive.AddFileAsync(file1));
    Assert.IsTrue(await archive.AddFileAsync(file2));
    Assert.IsTrue(await archive.SaveAsync());
    Assert.IsTrue(File.Exists(path));

    using var loadedArchive = await FileArchive.LoadAsync(path);
    Assert.IsNotNull(loadedArchive);
    Assert.IsTrue(await loadedArchive.ExtractFileToDirectoryAsync("file1.txt", OutPath));
    Assert.IsTrue(await loadedArchive.ExtractFileToDirectoryAsync("file2.txt", OutPath));

    Assert.IsTrue(AreFilesEqual(file1, $@"{OutPath}\file1.txt"));
    Assert.IsTrue(AreFilesEqual(file2, $@"{OutPath}\file2.txt"));
  }

  [TestMethod]
  public async Task TestExtractAllToDirectoryAsync() {
    string path = $@"{ResultPath}\archive.zip";
    Directory.CreateDirectory($@"{InPath}\subdir");
    string file1 = CreateTestFile($@"{InPath}\subdir\file1.txt", 1023);
    string file2 = CreateTestFile($@"{InPath}\subdir\file2.txt", 4095);
    string file3 = CreateTestFile($@"{InPath}\file3.txt", 1023);

    using var archive = await FileArchive.CreateAsync(path, CompressionLevel.Fastest);
    Assert.IsTrue(await archive.AddDirectoryAsync(InPath, false));
    Assert.IsTrue(await archive.SaveAsync());
    Assert.IsTrue(File.Exists(path));

    await FileArchive.ExtractAllToDirectoryAsync(path, OutPath);
    Assert.IsFalse(AreFilesEqual(file1, $@"{OutPath}\subdir\file1.txt"));
    Assert.IsFalse(AreFilesEqual(file2, $@"{OutPath}\subdir\file2.txt"));
    Assert.IsTrue(AreFilesEqual(file3, $@"{OutPath}\file3.txt"));
  }

  [TestMethod]
  public async Task TestExtractFileToStreamAsync() {
    string path = $@"{ResultPath}\archive.zip";
    string file1 = CreateTestFile($@"{InPath}\file1.txt", 1023);

    await FileArchive.CreateFromFileAsync(file1, path, CompressionLevel.Fastest);
    Assert.IsTrue(File.Exists(path));

    using var loadedArchive = await FileArchive.LoadAsync(path);
    Assert.IsNotNull(loadedArchive);
    Assert.IsTrue(loadedArchive.FileCount == 1);

    using var outStream = await loadedArchive.ExtractFileToMemoryAsync("file1.txt");
    Assert.IsNotNull(outStream);
    Assert.IsTrue(AreFilesEqual(outStream, file1));
  }

  [TestMethod]
  public async Task TestCreateFromFileAsync() {
    string path = $@"{ResultPath}\archive.zip";
    string file1 = CreateTestFile($@"{InPath}\file1.txt", 1023);

    await FileArchive.CreateFromFileAsync(file1, path, CompressionLevel.Fastest);
    Assert.IsTrue(File.Exists(path));

    ZipFile.ExtractToDirectory(path, OutPath);
    Assert.IsTrue(AreFilesEqual(file1, $@"{OutPath}\file1.txt"));
  }

  [TestMethod]
  public async Task TestCreateFromStreamAsync() {
    string path = $@"{ResultPath}\archive.zip";
    var stream = CreateTestStream(1023);

    await FileArchive.CreateFromStreamAsync(stream, "file1.txt", path, CompressionLevel.Fastest);
    Assert.IsTrue(File.Exists(path));

    ZipFile.ExtractToDirectory(path, OutPath);
    Assert.IsTrue(AreFilesEqual(stream, $@"{OutPath}\file1.txt"));
  }

  [TestMethod]
  public async Task TestGetFilesOfKind() {
    string path = $@"{ResultPath}\archive.zip";
    string file1 = CreateTestFile($@"{InPath}\file1.txt", 1023);
    string file2 = CreateTestFile($@"{InPath}\file2.txt", 4095);
    string file3 = CreateTestFile($@"{InPath}\file3.txt", 4095);

    using var archive = await FileArchive.CreateAsync(path, CompressionLevel.Fastest);
    Assert.IsTrue(await archive.AddFileAsync(file1, 123, null, false));
    Assert.IsTrue(await archive.AddFileAsync(file2, 456, null, false));
    Assert.IsTrue(await archive.AddFileAsync(file3, 123, null, false));
    Assert.IsTrue(await archive.SaveAsync());
    Assert.IsTrue(File.Exists(path));

    using var loadedArchive = await FileArchive.LoadAsync(path);
    Assert.IsNotNull(loadedArchive);
    Assert.IsTrue(loadedArchive.FileCount == 3);
    Assert.IsTrue(loadedArchive.HasFilesOfKind(123));
    Assert.IsTrue(loadedArchive.HasFilesOfKind(456));
    Assert.IsTrue(loadedArchive.FindFilesOfKind(123).Count() == 2);
    Assert.IsTrue(loadedArchive.FindFilesOfKind(456).Count() == 1);
  }

  [TestMethod]
  public async Task TestFindFilesInDirectory() {
    string path = $@"{ResultPath}\archive.zip";
    string file1 = CreateTestFile($@"{InPath}\file1.txt", 1023);
    string file2 = CreateTestFile($@"{InPath}\file2.txt", 4095);
    string file3 = CreateTestFile($@"{InPath}\file3.txt", 4095);

    using var archive = await FileArchive.CreateAsync(path, CompressionLevel.Fastest);
    Assert.IsTrue(await archive.AddFileAsync(file1, 123, "foo", false));
    Assert.IsTrue(await archive.AddFileAsync(file2, 456, "bar", false));
    Assert.IsTrue(await archive.AddFileAsync(file3, 123, "foobar", false));
    Assert.IsTrue(await archive.AddFileAsync(file1, 123, "foo\\bar", false));
    Assert.IsTrue(await archive.AddFileAsync(file2, 456, "foo\\bar", false));
    Assert.IsTrue(await archive.SaveAsync());
    Assert.IsTrue(File.Exists(path));

    using var loadedArchive = await FileArchive.LoadAsync(path);
    Assert.IsNotNull(loadedArchive);

    var result1 = loadedArchive.FindFilesInDirectory("bar");
    Assert.IsTrue(result1.Count() == 1);
    Assert.IsTrue(result1.Count(entry => entry.ArchivePath == "bar\\file2.txt") == 1);

    var result2 = loadedArchive.FindFilesInDirectory("foo");
    Assert.IsTrue(result2.Count() == 3);
    Assert.IsTrue(result2.Count(entry => entry.ArchivePath == "foo\\file1.txt") == 1);
    Assert.IsTrue(result2.Count(entry => entry.ArchivePath == "foo\\bar\\file1.txt") == 1);
    Assert.IsTrue(result2.Count(entry => entry.ArchivePath == "foo\\bar\\file2.txt") == 1);

    var result3 = loadedArchive.FindFilesInDirectory("foo\\bar");
    Assert.IsTrue(result3.Count() == 2);
    Assert.IsTrue(result3.Count(entry => entry.ArchivePath == "foo\\bar\\file1.txt") == 1);
    Assert.IsTrue(result3.Count(entry => entry.ArchivePath == "foo\\bar\\file2.txt") == 1);

    await loadedArchive.ExtractFilesToDirectoryAsync(result3, OutPath);
    Assert.IsTrue(AreFilesEqual(file1, $@"{OutPath}\foo\bar\file1.txt"));
    Assert.IsTrue(AreFilesEqual(file2, $@"{OutPath}\foo\bar\file2.txt"));
  }

  [TestMethod]
  public async Task TestExtractAllFilesOfKindToDirectoryAsync() {
    string path = $@"{ResultPath}\archive.zip";
    string file1 = CreateTestFile($@"{InPath}\file1.txt", 1023);
    string file2 = CreateTestFile($@"{InPath}\file2.txt", 4095);
    string file3 = CreateTestFile($@"{InPath}\file3.txt", 4095);

    using var archive = await FileArchive.CreateAsync(path, CompressionLevel.Fastest);
    Assert.IsTrue(await archive.AddFileAsync(file1, 123, null, false));
    Assert.IsTrue(await archive.AddFileAsync(file2, 456, null, false));
    Assert.IsTrue(await archive.AddFileAsync(file3, 456, "foo", false));
    Assert.IsTrue(await archive.SaveAsync());
    Assert.IsTrue(File.Exists(path));

    using var loadedArchive = await FileArchive.LoadAsync(path);
    Assert.IsNotNull(loadedArchive);

    await loadedArchive.ExtractAllFilesOfKindToDirectoryAsync(456, OutPath);
    Assert.IsTrue(AreFilesEqual(file2, $@"{OutPath}\file2.txt"));
    Assert.IsTrue(AreFilesEqual(file3, $@"{OutPath}\foo\file3.txt"));
  }

  private class ExtraData {
    public int Value1 { get; set; }
    public int Value2 { get; set; }
    public string Value3 { get; set; }
  }

  [TestMethod]
  public async Task TestOptionalData() {
    string path = $@"{ResultPath}\archive.zip";
    using var archive = await FileArchive.CreateAsync(path, CompressionLevel.Fastest);
    var data = new ExtraData() {
      Value1 = 123,
      Value2 = 456,
      Value3 = "foo"
    };
    archive.SetOptionalData(data);
    Assert.IsTrue(await archive.SaveAsync());
    Assert.IsTrue(File.Exists(path));

    using var loadedArchive = await FileArchive.LoadAsync(path);
    Assert.IsNotNull(loadedArchive);
    Assert.IsTrue(loadedArchive.HasOptionalData);

    var loadedData = loadedArchive.GetOptionalData<ExtraData>();
    Assert.IsTrue(loadedData.Value1 == 123);
    Assert.IsTrue(loadedData.Value2 == 456);
    Assert.IsTrue(loadedData.Value3 == "foo");
  }

  [TestMethod]
  public async Task TestPlainZipFile() {
    string path = $@"{ResultPath}\archive.zip";
    Directory.CreateDirectory($@"{InPath}\subdir");
    string file1 = CreateTestFile($@"{InPath}\subdir\file1.txt", 1023);
    string file2 = CreateTestFile($@"{InPath}\subdir\file2.txt", 4095);
    string file3 = CreateTestFile($@"{InPath}\file3.txt", 1023);

    // Create zip file without included header,
    // extracting files should still work.
    ZipFile.CreateFromDirectory(InPath, path, CompressionLevel.Fastest, false);
    Assert.IsTrue(File.Exists(path));

    await FileArchive.ExtractAllToDirectoryAsync(path, OutPath);
    Assert.IsTrue(AreFilesEqual(file1, $@"{OutPath}\subdir\file1.txt"));
    Assert.IsTrue(AreFilesEqual(file2, $@"{OutPath}\subdir\file2.txt"));
    Assert.IsTrue(AreFilesEqual(file3, $@"{OutPath}\file3.txt"));
  }

  private MemoryStream CreateTestStream(int size) {
    var stream = new MemoryStream();
    var writer = new StreamWriter(stream);
    var rand = new Random(21);

    for (int i = 0; i < size; i++) {
      writer.Write((char)rand.Next(127));
    }

    writer.Flush();
    stream.Flush();
    stream.Position = 0;
    return stream;
  }

  private string CreateTestFile(string name, int size) {
    using var fileStream = new FileStream(name, FileMode.Create);
    using var stream = CreateTestStream(size);
    stream.CopyTo(fileStream);
    fileStream.Flush();
    fileStream.Close();
    return name;
  }

  private static void RecursiveDelete(DirectoryInfo baseDir) {
    if (!baseDir.Exists)
      return;

    foreach (var dir in baseDir.EnumerateDirectories()) {
      RecursiveDelete(dir);
    }

    baseDir.Delete(true);
  }

  private static bool AreFilesEqual(string fileA, string fileB) {
    try {
      byte[] data1 = File.ReadAllBytes(fileA);
      byte[] data2 = File.ReadAllBytes(fileB);
      return data1.SequenceEqual(data2);
    }
    catch {
      return false;
    }
  }

  private static bool AreFilesEqual(Stream stream, string fileB) {
    try {
      byte[] data1 = new byte[stream.Length];
      stream.Position = 0;
      stream.Read(data1, 0, (int)stream.Length);
      byte[] data2 = File.ReadAllBytes(fileB);
      return data1.SequenceEqual(data2);
    }
    catch {
      return false;
    }
  }
}