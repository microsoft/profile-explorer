// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using IRExplorerCore.FileFormat;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IRExplorerCoreTests.FileFormat;

[TestClass]
public class FileArchiveTest {
  private const string InPath = @"testFiles";
  private const string OutPath = @"outTestFiles";
  private const string ResultPath = @"resultTestFiles";

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
      var data1 = File.ReadAllBytes(fileA);
      var data2 = File.ReadAllBytes(fileB);
      return data1.SequenceEqual(data2);
    }
    catch {
      return false;
    }
  }

  private static bool AreFilesEqual(Stream stream, string fileB) {
    try {
      var data1 = new byte[stream.Length];
      stream.Position = 0;
      stream.Read(data1, 0, (int)stream.Length);
      var data2 = File.ReadAllBytes(fileB);
      return data1.SequenceEqual(data2);
    }
    catch {
      return false;
    }
  }

  [TestMethod]
  public async Task TestCreateAsync() {
    var path = $@"{ResultPath}\archive.zip";
    using var archive = await FileArchive.CreateAsync(path, CompressionLevel.Fastest);
    Assert.IsTrue(await archive.SaveAsync());

    Assert.IsTrue(File.Exists(path));
    Assert.IsTrue(new FileInfo(path).Length > 0);
  }

  [TestMethod]
  public async Task TestAddFileAsync() {
    var path = $@"{ResultPath}\archive.zip";
    var file1 = CreateTestFile($@"{InPath}\file1.txt", 1023);
    var file2 = CreateTestFile($@"{InPath}\file2.txt", 4095);

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
    var path = $@"{ResultPath}\archive.zip";
    var stream = CreateTestStream(1023);
    var file2 = CreateTestFile($@"{InPath}\file2.txt", 4095);

    using var archive = await FileArchive.CreateAsync(path, CompressionLevel.Fastest);
    Assert.IsTrue(await archive.AddFileStreamAsync(@"file1.txt", stream));
    Assert.IsTrue(await archive.AddFileAsync(file2));
    Assert.IsTrue(await archive.SaveAsync());
    Assert.IsTrue(File.Exists(path));

    ZipFile.ExtractToDirectory(path, OutPath);
    Assert.IsTrue(AreFilesEqual(stream, $@"{OutPath}\file1.txt"));
    Assert.IsTrue(AreFilesEqual(file2, $@"{OutPath}\file2.txt"));
  }

  [TestMethod]
  public async Task TestAddDirectoryAsync() {
    var path = $@"{ResultPath}\archive.zip";
    var file1 = CreateTestFile($@"{InPath}\file1.txt", 1023);
    var file2 = CreateTestFile($@"{InPath}\file2.txt", 4095);

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
    var path = $@"{ResultPath}\archive.zip";

    Directory.CreateDirectory($@"{InPath}\subdir");
    var file1 = CreateTestFile($@"{InPath}\subdir\file1.txt", 1023);
    var file2 = CreateTestFile($@"{InPath}\subdir\file2.txt", 4095);
    var file3 = CreateTestFile($@"{InPath}\file3.txt", 1023);

    using var archive = await FileArchive.CreateAsync(path, CompressionLevel.Fastest);
    Assert.IsTrue(await archive.AddDirectoryAsync(InPath, true, "*", "clientSubdir"));
    Assert.IsTrue(await archive.SaveAsync());
    Assert.IsTrue(File.Exists(path));

    ZipFile.ExtractToDirectory(path, OutPath);
    Assert.IsTrue(AreFilesEqual(file1, $@"{OutPath}\clientSubdir\subdir\file1.txt"));
    Assert.IsTrue(AreFilesEqual(file2, $@"{OutPath}\clientSubdir\subdir\file2.txt"));
    Assert.IsTrue(AreFilesEqual(file3, $@"{OutPath}\clientSubdir\file3.txt"));
  }

  [TestMethod]
  public async Task TestAddDirectoryWithoutSubdirsAsync() {
    var path = $@"{ResultPath}\archive.zip";

    Directory.CreateDirectory($@"{InPath}\subdir");
    var file1 = CreateTestFile($@"{InPath}\subdir\file1.txt", 1023);
    var file2 = CreateTestFile($@"{InPath}\subdir\file2.txt", 4095);
    var file3 = CreateTestFile($@"{InPath}\file3.txt", 1023);

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
    var path = $@"{ResultPath}\archive.zip";
    var file1 = CreateTestFile($@"{InPath}\file1.txt", 1023);
    var file2 = CreateTestFile($@"{InPath}\file2.dat", 4095);
    var file3 = CreateTestFile($@"{InPath}\file3.dat", 4095);

    using var archive = await FileArchive.CreateAsync(path, CompressionLevel.Fastest);
    Assert.IsTrue(await archive.AddDirectoryAsync(InPath, false, "*.dat"));
    Assert.IsTrue(await archive.SaveAsync());
    Assert.IsTrue(File.Exists(path));

    ZipFile.ExtractToDirectory(path, OutPath);
    Assert.IsFalse(AreFilesEqual(file1, $@"{OutPath}\file1.txt"));
    Assert.IsTrue(AreFilesEqual(file2, $@"{OutPath}\file2.dat"));
    Assert.IsTrue(AreFilesEqual(file3, $@"{OutPath}\file3.dat"));
  }

  [TestMethod]
  public async Task TestExtractFileToDirectoryAsync() {
    var path = $@"{ResultPath}\archive.zip";
    var file1 = CreateTestFile($@"{InPath}\file1.txt", 1023);
    var file2 = CreateTestFile($@"{InPath}\file2.txt", 4095);

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
}