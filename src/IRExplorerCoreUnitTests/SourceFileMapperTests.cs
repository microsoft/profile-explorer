// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using IRExplorerCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IRExplorerCoreTests;

[TestClass]
public class SourceFileMapperTests {
  [TestMethod]
  public void Map_FirstTime_ReturnsResultFromLookup() {
    var mapper = new SourceFileMapper();
    bool called = false;
    const string expectedResult = @"c:\mapped\path\to\file.txt";
    Assert.AreEqual(expectedResult, mapper.Map(@"c:\path\to\file.txt", () => {
      called = true;
      return expectedResult;
    }));
    Assert.IsTrue(called);
  }

  [TestMethod]
  public void Map_SecondTimeForSamePath_DoesNotUseLookup() {
    var mapper = new SourceFileMapper();
    const string expectedResult = @"c:\mapped\path\to\file.txt";
    const string source = @"c:\path\to\file.txt";

    // prime it
    mapper.Map(source, () => expectedResult);

    Assert.AreEqual(expectedResult, mapper.Map(source, () => {
      Assert.Fail();
      return "";
    }));
  }

  [TestMethod]
  public void OtherFilesInTheSameDirectoryDontUseLookup() {
    var mapper = new SourceFileMapper();
    const string source = @"c:\path\to\file.txt";
    const string sourceInSameDir = @"c:\path\to\otherFile.txt";
    // prime it
    mapper.Map(source, () => @"c:\mapped\path\to\file.txt");

    const string expectedResult = @"c:\mapped\path\to\otherFile.txt";
    Assert.AreEqual(expectedResult, mapper.Map(sourceInSameDir, () => {
      Assert.Fail();
      return "";
    }));
  }

  [TestMethod]
  public void OtherFilesWithTheSamePrefixAreMapped() {
    string file1 = @"c:\path\to\file.txt";
    string file2 = @"c:\path\for\other\file.txt";
    string mappedFile1 = @"c:\mapped\to\file.txt";
    string mappedFile2 = @"c:\mapped\for\other\file.txt";

    var mapper = new SourceFileMapper();

    // prime it
    mapper.Map(file1, () => mappedFile1);

    Assert.AreEqual(mappedFile2, mapper.Map(file2, () => {
      Assert.Fail();
      return "";
    }));
  }

  [TestMethod]
  public void OtherFilesWithDeeperHierarchyAreMatched() {
    string file1 = @"c:\path\to\file.txt";
    string file2 = @"c:\path\to\deeper\file.txt";
    string mappedFile1 = @"c:\mapped\to\file.txt";
    string mappedFile2 = @"c:\mapped\to\deeper\file.txt";

    var mapper = new SourceFileMapper();

    // prime it
    mapper.Map(file1, () => mappedFile1);

    Assert.AreEqual(mappedFile2, mapper.Map(file2, () => {
      Assert.Fail();
      return "";
    }));
  }

  [TestMethod]
  public void ResultIsNullWhenLookupIsCanceled() {
    var mapper = new SourceFileMapper();
    Assert.IsNull(mapper.Map(@"c:\path\to\file.txt", () => null));
  }

  [TestMethod]
  public void Map_RetriesNextTimeWhenLookupWasCanceled() {
    var mapper = new SourceFileMapper();
    bool called = false;
    const string path = @"c:\path\to\file.txt";
    const string expectedResult = @"c:\mapped\path\to\file.txt";

    // prime with canceled result
    mapper.Map(path, () => null);

    Assert.AreEqual(expectedResult, mapper.Map(path, () => {
      called = true;
      return expectedResult;
    }));
    Assert.IsTrue(called);
  }
}
