// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace ProfileExplorer.CoreTests;

/// <summary>
/// Helper class for accessing test data files in a consistent manner.
/// </summary>
public static class TestDataHelper {
  private static readonly string TestDataRoot;

  static TestDataHelper() {
    // Get the directory where the test assembly is located
    var assemblyLocation = Assembly.GetExecutingAssembly().Location;
    var assemblyDirectory = Path.GetDirectoryName(assemblyLocation);
    
    // Navigate up to find the TestData directory
    // The build output is typically in bin/Debug/net8.0/, so we need to go up several levels
    var currentDir = assemblyDirectory;
    while (currentDir != null && !Directory.Exists(Path.Combine(currentDir, "TestData"))) {
      currentDir = Directory.GetParent(currentDir)?.FullName;
    }
    
    if (currentDir != null) {
      TestDataRoot = Path.Combine(currentDir, "TestData");
    } else {
      throw new DirectoryNotFoundException("Could not locate TestData directory relative to test assembly");
    }
  }

  /// <summary>
  /// Gets the root path for all test data.
  /// </summary>
  public static string GetTestDataRoot() => TestDataRoot;

  /// <summary>
  /// Gets the path to a specific trace file.
  /// </summary>
  /// <param name="testCaseName">Name of the test case (e.g., "MsoTrace")</param>
  /// <param name="fileName">Name of the trace file (e.g., "trace.etl")</param>
  /// <returns>Full path to the trace file</returns>
  public static string GetTracePath(string testCaseName, string fileName = "trace.etl") {
    return Path.Combine(TestDataRoot, "Traces", testCaseName, fileName);
  }

  /// <summary>
  /// Gets the path to the symbols directory for a test case.
  /// </summary>
  /// <param name="testCaseName">Name of the test case (e.g., "MsoTrace")</param>
  /// <returns>Full path to the symbols directory</returns>
  public static string GetSymbolsPath(string testCaseName) {
    return Path.Combine(TestDataRoot, "Symbols", testCaseName);
  }

  /// <summary>
  /// Gets the path to a specific symbol file.
  /// </summary>
  /// <param name="testCaseName">Name of the test case (e.g., "MsoTrace")</param>
  /// <param name="fileName">Name of the PDB file (e.g., "Mso20Win32Client.pdb")</param>
  /// <returns>Full path to the symbol file</returns>
  public static string GetSymbolFilePath(string testCaseName, string fileName) {
    return Path.Combine(TestDataRoot, "Symbols", testCaseName, fileName);
  }

  /// <summary>
  /// Gets the path to the binaries directory for a test case.
  /// </summary>
  /// <param name="testCaseName">Name of the test case (e.g., "MsoTrace")</param>
  /// <returns>Full path to the binaries directory</returns>
  public static string GetBinariesPath(string testCaseName) {
    return Path.Combine(TestDataRoot, "Binaries", testCaseName);
  }

  /// <summary>
  /// Gets the path to a specific binary file.
  /// </summary>
  /// <param name="testCaseName">Name of the test case (e.g., "MsoTrace")</param>
  /// <param name="fileName">Name of the binary file (e.g., "Mso20win32client.dll")</param>
  /// <returns>Full path to the binary file</returns>
  public static string GetBinaryFilePath(string testCaseName, string fileName) {
    return Path.Combine(TestDataRoot, "Binaries", testCaseName, fileName);
  }

  /// <summary>
  /// Checks if all required files exist for a test case.
  /// </summary>
  /// <param name="testCaseName">Name of the test case</param>
  /// <param name="traceFileName">Expected trace file name</param>
  /// <param name="requiredSymbolFiles">List of required symbol files</param>
  /// <param name="requiredBinaryFiles">List of required binary files</param>
  /// <returns>True if all files exist, false otherwise</returns>
  public static bool ValidateTestCase(string testCaseName, string traceFileName = "trace.etl", 
                                     string[]? requiredSymbolFiles = null, 
                                     string[]? requiredBinaryFiles = null) {
    // Check trace file
    var tracePath = GetTracePath(testCaseName, traceFileName);
    if (!File.Exists(tracePath)) {
      return false;
    }

    // Check symbol files if specified
    if (requiredSymbolFiles != null) {
      foreach (var symbolFile in requiredSymbolFiles) {
        var symbolPath = GetSymbolFilePath(testCaseName, symbolFile);
        if (!File.Exists(symbolPath)) {
          return false;
        }
      }
    }

    // Check binary files if specified
    if (requiredBinaryFiles != null) {
      foreach (var binaryFile in requiredBinaryFiles) {
        var binaryPath = GetBinaryFilePath(testCaseName, binaryFile);
        if (!File.Exists(binaryPath)) {
          return false;
        }
      }
    }

    return true;
  }

  /// <summary>
  /// Gets information about a specific test case by name.
  /// </summary>
  /// <param name="testCaseName">Name of the test case directory</param>
  /// <returns>TestCaseInfo for the specified test case</returns>
  public static TestCaseInfo GetTestCase(string testCaseName) {
    return new TestCaseInfo(testCaseName);
  }

  /// <summary>
  /// Gets all available test cases by scanning the TestData directory.
  /// </summary>
  /// <returns>List of available test case names</returns>
  public static List<string> GetAvailableTestCases() {
    var testCases = new List<string>();
    var tracesDir = Path.Combine(TestDataRoot, "Traces");
    
    if (Directory.Exists(tracesDir)) {
      var directories = Directory.GetDirectories(tracesDir);
      foreach (var dir in directories) {
        var testCaseName = Path.GetFileName(dir);
        testCases.Add(testCaseName);
      }
    }
    
    return testCases;
  }

  /// <summary>
  /// Validates that all available test cases have complete data.
  /// </summary>
  /// <returns>Dictionary mapping test case names to their validation status</returns>
  public static Dictionary<string, bool> ValidateAllTestCases() {
    var results = new Dictionary<string, bool>();
    var testCases = GetAvailableTestCases();
    
    foreach (var testCase in testCases) {
      var info = GetTestCase(testCase);
      results[testCase] = info.IsValid;
    }
    
    return results;
  }

  /// <summary>
  /// Represents information about a test case.
  /// </summary>
  public class TestCaseInfo {
    public string TestCaseName { get; }
    
    public TestCaseInfo(string testCaseName) {
      TestCaseName = testCaseName;
    }

    public string TracePath => GetTracePath(TestCaseName, "trace.etl");
    public string SymbolsPath => GetSymbolsPath(TestCaseName);
    public string BinariesPath => GetBinariesPath(TestCaseName);

    /// <summary>
    /// Gets all PDB files for this test case.
    /// </summary>
    public List<string> PdbFiles {
      get {
        var pdbFiles = new List<string>();
        var symbolsDir = SymbolsPath;
        if (Directory.Exists(symbolsDir)) {
          pdbFiles.AddRange(Directory.GetFiles(symbolsDir, "*.pdb"));
        }
        return pdbFiles;
      }
    }

    /// <summary>
    /// Gets all binary files for this test case.
    /// </summary>
    public List<string> BinaryFiles {
      get {
        var binaryFiles = new List<string>();
        var binariesDir = BinariesPath;
        if (Directory.Exists(binariesDir)) {
          binaryFiles.AddRange(Directory.GetFiles(binariesDir, "*.*"));
        }
        return binaryFiles;
      }
    }

    /// <summary>
    /// Checks if this test case has all required files.
    /// </summary>
    public bool IsValid {
      get {
        // Must have trace file
        if (!File.Exists(TracePath)) {
          return false;
        }

        // Must have symbols directory (but PDB files are optional)
        if (!Directory.Exists(SymbolsPath)) {
          return false;
        }

        // Must have binaries directory (but binary files are optional)
        if (!Directory.Exists(BinariesPath)) {
          return false;
        }

        return true;
      }
    }

    /// <summary>
    /// Gets a summary of this test case.
    /// </summary>
    public string GetSummary() {
      var summary = $"Test Case: {TestCaseName}\n";
      summary += $"  Trace: {(File.Exists(TracePath) ? "✓" : "✗")} {TracePath}\n";
      summary += $"  Symbols: {PdbFiles.Count} PDB file(s)\n";
      summary += $"  Binaries: {BinaryFiles.Count} binary file(s)\n";
      summary += $"  Valid: {(IsValid ? "✓" : "✗")}";
      return summary;
    }
  }
}
