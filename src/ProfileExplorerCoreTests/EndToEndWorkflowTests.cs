// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProfileExplorer.Core.Binary;
using ProfileExplorer.Core.Profile.Data;
using ProfileExplorer.Core.Profile.ETW;
using ProfileExplorer.Core.Profile.CallTree;
using ProfileExplorer.Core.Providers;
using ProfileExplorer.Core.Session;
using ProfileExplorer.Core.Settings;
using ProfileExplorer.Core.Utilities;
using ProfileExplorer.Core.Compilers.ASM;
using ProfileExplorer.Core.Compilers.Architecture;

namespace ProfileExplorer.CoreTests;

[TestClass]
public class EndToEndWorkflowTests {
  
  /// <summary>
  /// List of test cases to run. Add new test case names here to include them in testing.
  /// Each test case should have corresponding directories in TestData/Traces/, TestData/Binaries/, and TestData/Symbols/.
  /// </summary>
  private static readonly List<string> TestCases = new List<string> {
    "MsoTrace"
    // Add new test case names here, e.g.:
    // "WebBrowserTrace",
    // "GameEngineTrace",
    // "DatabaseTrace"
  };

  /// <summary>
  /// Optional: Process IDs for each test case. If not specified, the test will show available processes.
  /// </summary>
  private static readonly Dictionary<string, int> TestCaseProcessIds = new Dictionary<string, int> {
    { "MsoTrace", 34376 }
    // Add process IDs for specific test cases if known, e.g.:
    // { "WebBrowserTrace", 12345 },
    // { "GameEngineTrace", 67890 }
  };

  /// <summary>
  /// Data structure representing a process baseline entry
  /// </summary>
  public class ProcessBaselineEntry {
    public string Name { get; set; }
    public double WeightPercentage { get; set; }
    public double DurationMs { get; set; }
    public int ProcessId { get; set; }
    public string CommandLine { get; set; }
  }

  /// <summary>
  /// Data structure representing a module baseline entry
  /// </summary>
  public class ModuleBaselineEntry {
    public string Name { get; set; }
    public double WeightPercentage { get; set; }
    public double TimeMs { get; set; }
  }

  /// <summary>
  /// Data structure representing a function baseline entry
  /// </summary>
  public class FunctionBaselineEntry {
    public string Name { get; set; }
    public string Address { get; set; }
    public string Module { get; set; }
    public double SelfTimePercentage { get; set; }
    public double SelfTimeMs { get; set; }
    public double TotalTimePercentage { get; set; }
    public double TotalTimeMs { get; set; }
  }

  [DataTestMethod]
  [DynamicData(nameof(GetTestCaseData), DynamicDataSourceType.Method)]
  public async Task TestEndToEndWorkflow_ForTestCase(string testCaseName) {
    Console.WriteLine($"\n=== Running End-to-End Workflow Test for: {testCaseName} ===");
    
    // Execute common workflow steps
    var (processBaselineData, moduleBaselineData, functionBaselineData) = await ExecuteCommonWorkflowSteps(testCaseName);

    // Step 6: Compare with baselines
    Console.WriteLine($"\n=== Step 6: Baseline Validation ===");
    
    var baselineDir = GetBaselineDirectory(testCaseName);
    var processBaselinePath = Path.Combine(baselineDir, "processes_baseline.csv");
    var moduleBaselinePath = Path.Combine(baselineDir, "modules_baseline.csv");

    bool baselinesExist = File.Exists(processBaselinePath) && 
                          File.Exists(moduleBaselinePath);

    // Check if function baseline files exist for all modules
    if (baselinesExist) {
      foreach (var moduleName in functionBaselineData.Keys) {
        var functionBaselinePath = Path.Combine(baselineDir, $"functions_{SanitizeFileName(moduleName)}_baseline.csv");
        if (!File.Exists(functionBaselinePath)) {
          baselinesExist = false;
          break;
        }
      }
    }

    if (baselinesExist) {
      // Save current results to temporary CSV files in system temp directory
      var tempDir = Path.Combine(Path.GetTempPath(), "ProfileExplorerTests", $"Baseline_{testCaseName}_{Guid.NewGuid():N}");
      Directory.CreateDirectory(tempDir);
      
      try {
        var currentProcessFile = Path.Combine(tempDir, "processes_baseline.csv");
        var currentModulesFile = Path.Combine(tempDir, "modules_baseline.csv");
        
        SaveProcessBaseline(processBaselineData, currentProcessFile);
        SaveModuleBaseline(moduleBaselineData, currentModulesFile);

        // Save function baselines grouped by module
        foreach (var moduleGroup in functionBaselineData) {
          var currentFunctionFile = Path.Combine(tempDir, $"functions_{SanitizeFileName(moduleGroup.Key)}_baseline.csv");
          SaveFunctionBaseline(moduleGroup.Value, currentFunctionFile);
        }

        // Compare CSV files directly
        CompareBaselineFiles(baselineDir, tempDir, testCaseName);
        
        Console.WriteLine($"✓ All baselines match for test case '{testCaseName}'");
      } finally {
        // Clean up temporary files
        if (Directory.Exists(tempDir)) {
          Directory.Delete(tempDir, true);
        }
      }
    } else {
      Assert.Inconclusive($"Baselines not found for test case '{testCaseName}'. Run the GenerateBaseline test first to create them.");
    }

    Console.WriteLine($"\n✓ Workflow completed successfully for test case '{testCaseName}'");
  }

  /// <summary>
  /// Test method to generate or regenerate baseline CSV files for a test case.
  /// This should be run whenever you need to create new baselines or update existing ones.
  /// </summary>
  [Ignore] // Remove to save baselines
  [DataTestMethod]
  [DynamicData(nameof(GetTestCaseData), DynamicDataSourceType.Method)]
  public async Task GenerateBaseline_ForTestCase(string testCaseName) {
    Console.WriteLine($"\n=== Generating Baselines for: {testCaseName} ===");
    
    // Execute common workflow steps
    var (processBaselineData, moduleBaselineData, functionBaselineData) = await ExecuteCommonWorkflowSteps(testCaseName);

    // Step 6: Save baselines to TestData folder
    Console.WriteLine($"\n=== Step 6: Saving Baselines ===");
    
    var baselineDir = GetBaselineDirectory(testCaseName);
    Directory.CreateDirectory(baselineDir);

    var processBaselinePath = Path.Combine(baselineDir, "processes_baseline.csv");
    var moduleBaselinePath = Path.Combine(baselineDir, "modules_baseline.csv");

    SaveProcessBaseline(processBaselineData, processBaselinePath);
    SaveModuleBaseline(moduleBaselineData, moduleBaselinePath);

    // Save function baselines for each module
    var totalFunctions = 0;
    foreach (var moduleEntry in functionBaselineData) {
      var moduleName = moduleEntry.Key;
      var functions = moduleEntry.Value;
      var functionBaselinePath = Path.Combine(baselineDir, $"functions_{SanitizeFileName(moduleName)}_baseline.csv");
      SaveFunctionBaseline(functions, functionBaselinePath);
      totalFunctions += functions.Count;
      Console.WriteLine($"  - Functions for {moduleName}: {functions.Count} entries -> {functionBaselinePath}");
    }

    Console.WriteLine($"✓ Baselines saved to TestData folder for test case '{testCaseName}'");
    Console.WriteLine($"  - Processes: {processBaselineData.Count} entries -> {processBaselinePath}");
    Console.WriteLine($"  - Modules: {moduleBaselineData.Count} entries -> {moduleBaselinePath}");
    Console.WriteLine($"  - Functions: {totalFunctions} entries across {functionBaselineData.Count} modules");
    Console.WriteLine($"\n✓ Baseline generation completed successfully for test case '{testCaseName}'");
  }

  /// <summary>
  /// Executes the common workflow steps (1-5) shared by both test methods
  /// </summary>
  private async Task<(List<ProcessBaselineEntry> processBaselineData, List<ModuleBaselineEntry> moduleBaselineData, Dictionary<string, List<FunctionBaselineEntry>> functionBaselineData)> ExecuteCommonWorkflowSteps(string testCaseName) {
    // Get test case information
    var testCase = TestDataHelper.GetTestCase(testCaseName);
    
    if (!testCase.IsValid) {
      Assert.Inconclusive($"Test case '{testCaseName}' is not valid:\n{testCase.GetSummary()}");
    }

    var options = new ProfileDataProviderOptions();
    var cancelableTask = new CancelableTask();

    // Step 1: Load list of processes from trace
    Console.WriteLine("=== Step 1: Loading process list from trace ===");
    
    var processList = await ETWProfileDataProvider.FindTraceProcesses(
      testCase.TracePath, 
      options, 
      progress => {
        Console.WriteLine($"Process discovery: {progress.Current}/{progress.Total}");
      }, 
      cancelableTask);

    Assert.IsNotNull(processList, "Failed to load process list from trace");
    Assert.IsTrue(processList.Count > 0, "No processes found in trace");

    // Extract process baseline data
    var processBaselineData = ExtractProcessBaselineData(processList);

    // Determine target process ID
    int targetProcessId;
    if (TestCaseProcessIds.TryGetValue(testCaseName, out targetProcessId)) {
      var targetProcess = processList.FirstOrDefault(p => p.Process.ProcessId == targetProcessId);
      if (targetProcess == null) {
        Console.WriteLine($"WARNING: Configured process ID {targetProcessId} not found in trace.");
        Console.WriteLine($"Available process IDs: {string.Join(", ", processList.Select(p => p.Process.ProcessId))}");
        Assert.Inconclusive($"Target process ID {targetProcessId} not found in trace for test case '{testCaseName}'");
      }
    } else {
      // Use the process with the highest weight
      var sortedProcesses = processList.OrderByDescending(p => p.Weight).ToList();
      targetProcessId = sortedProcesses.First().Process.ProcessId;
      Console.WriteLine($"No specific process ID configured for '{testCaseName}', using process with highest weight: {targetProcessId}");
    }

    // Step 2: Configure symbol settings
    Console.WriteLine($"\n=== Step 2: Configuring symbol settings ===");
    
    var symbolSettings = new SymbolFileSourceSettings();
    symbolSettings.SymbolPaths.Clear(); // Clear any default paths
    symbolSettings.InsertSymbolPath(testCase.SymbolsPath); // Add our symbols directory
    symbolSettings.SourceServerEnabled = false; // Disable symbol servers
    symbolSettings.UseEnvironmentVarSymbolPaths = false; // Disable environment symbol paths
    
    Console.WriteLine($"Symbol paths configured: {string.Join("; ", symbolSettings.SymbolPaths)}");
    Console.WriteLine($"Available PDB files: {testCase.PdbFiles.Count}");
    foreach (var pdb in testCase.PdbFiles) {
      Console.WriteLine($"  - {Path.GetFileName(pdb)}");
    }

    // Step 3: Load trace data for the target process
    Console.WriteLine($"\n=== Step 3: Loading trace data for process {targetProcessId} ===");

  var session = new BaseSession();
    var processIds = new List<int> { targetProcessId };
    var report = new ProfileDataReport();

    

  bool loadResult = await session.LoadProfileData(
      testCase.TracePath, 
      processIds, 
      options, 
      symbolSettings, 
      report, 
      progress => {
        Console.WriteLine($"Data processing: {progress.Stage} - {progress.Current}/{progress.Total} {progress.Optional}");
      }, 
      cancelableTask);

    Assert.IsTrue(loadResult, "Failed to load profile data");
    
    var profileData = session.ProfileData;
    Assert.IsNotNull(profileData, "Profile data is null after successful load");
    Console.WriteLine("Data processing completed successfully");

    // Step 4: Get module baseline data
    Console.WriteLine($"\n=== Step 4: Extracting Module Information ===");
    var moduleBaselineData = ExtractModuleBaselineData(profileData);

    // Step 5: Get function baseline data
    Console.WriteLine($"\n=== Step 5: Extracting Function Information ===");
    var functionBaselineData = ExtractFunctionBaselineData(profileData);

    return (processBaselineData, moduleBaselineData, functionBaselineData);
  }

  /// <summary>
  /// Gets the baseline directory path in the source TestData folder
  /// </summary>
  private string GetBaselineDirectory(string testCaseName) {
    // Get the source TestData directory, not the runtime one
    var testProjectDir = Path.GetDirectoryName(typeof(EndToEndWorkflowTests).Assembly.Location);
    var sourceTestDataDir = Path.GetFullPath(Path.Combine(testProjectDir!, "..", "..", "..", "TestData"));
    return Path.Combine(sourceTestDataDir, testCaseName);
  }

  /// <summary>
  /// Sanitizes a filename by removing invalid characters
  /// </summary>
  private string SanitizeFileName(string fileName) {
    var invalidChars = Path.GetInvalidFileNameChars();
    return string.Join("_", fileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
  }

  /// <summary>
  /// Extracts process baseline data from the process list
  /// </summary>
  private List<ProcessBaselineEntry> ExtractProcessBaselineData(List<ProcessSummary> processList) {
    var sortedProcesses = processList.OrderByDescending(p => p.Weight).ToList();
    var baselineData = new List<ProcessBaselineEntry>();

    foreach (var process in sortedProcesses) {
      baselineData.Add(new ProcessBaselineEntry {
        Name = process.Process.Name ?? "Unknown",
        WeightPercentage = process.WeightPercentage,
        DurationMs = process.Duration.TotalMilliseconds,
        ProcessId = process.Process.ProcessId,
        CommandLine = process.Process.CommandLine ?? "N/A"
      });
    }

    return baselineData;
  }

  /// <summary>
  /// Extracts module baseline data from the profile data
  /// </summary>
  private List<ModuleBaselineEntry> ExtractModuleBaselineData(ProfileData profileData) {
    var moduleInfoList = new List<ModuleBaselineEntry>();

    foreach (var moduleKvp in profileData.Modules) {
      var moduleId = moduleKvp.Key;
      var module = moduleKvp.Value;
      var moduleName = module.ModuleName ?? $"Module_{moduleId}";

      // Get weight for this module
      TimeSpan moduleWeight = TimeSpan.Zero;
      if (profileData.ModuleWeights.TryGetValue(moduleId, out var weight)) {
        moduleWeight = weight;
      }

      // Skip modules with zero time
      if (moduleWeight.TotalMilliseconds <= 0) {
        continue;
      }

      // Calculate percentage
      double weightPercentage = profileData.ScaleModuleWeight(moduleWeight) * 100;
      double timeMs = moduleWeight.TotalMilliseconds;

      moduleInfoList.Add(new ModuleBaselineEntry {
        Name = moduleName,
        WeightPercentage = weightPercentage,
        TimeMs = timeMs
      });
    }

    // Sort by descending weight percentage
    return moduleInfoList.OrderByDescending(m => m.WeightPercentage).ToList();
  }

  /// <summary>
  /// Extracts function baseline data from the profile data using call tree, grouped by module
  /// </summary>
  private Dictionary<string, List<FunctionBaselineEntry>> ExtractFunctionBaselineData(ProfileData profileData) {
    var functionsByModule = new Dictionary<string, List<FunctionBaselineEntry>>();
    
    if (profileData.CallTree == null) {
      return functionsByModule;
    }

    // Get all functions from the call tree by collecting from all root nodes
    var allFunctionNodes = new List<ProfileCallTreeNode>();
    foreach (var rootNode in profileData.CallTree.RootNodes) {
      var functions = profileData.CallTree.GetTopFunctions(rootNode);
      allFunctionNodes.AddRange(functions);
    }
    
    // Group by function to avoid duplicates and combine weights
    var functionGroups = allFunctionNodes
      .GroupBy(node => node.Function)
      .ToList();
    
    foreach (var group in functionGroups) {
      var function = group.Key;
      if (function == null) continue;

      // Sum up weights from all instances of this function
      var totalWeight = TimeSpan.Zero;
      var totalExclusiveWeight = TimeSpan.Zero;
      var firstNode = group.First();
      
      foreach (var node in group) {
        totalWeight += node.Weight;
        totalExclusiveWeight += node.ExclusiveWeight;
      }

      // Skip functions with zero self time
      if (totalExclusiveWeight.TotalMilliseconds <= 0) {
        continue;
      }

      // Calculate percentages
      double selfTimePercentage = profileData.ScaleFunctionWeight(totalExclusiveWeight) * 100;
      double totalTimePercentage = profileData.ScaleFunctionWeight(totalWeight) * 100;
      
      var functionEntry = new FunctionBaselineEntry {
        Name = function.Name ?? "Unknown",
        Address = firstNode.FunctionDebugInfo?.RVA.ToString("X") ?? "Unknown",
        Module = function.ModuleName ?? "Unknown",
        SelfTimePercentage = selfTimePercentage,
        SelfTimeMs = totalExclusiveWeight.TotalMilliseconds,
        TotalTimePercentage = totalTimePercentage,
        TotalTimeMs = totalWeight.TotalMilliseconds
      };

      // Group by module
      var moduleName = functionEntry.Module;
      if (!functionsByModule.ContainsKey(moduleName)) {
        functionsByModule[moduleName] = new List<FunctionBaselineEntry>();
      }
      functionsByModule[moduleName].Add(functionEntry);
    }

    // Sort functions within each module by descending self time percentage
    foreach (var moduleEntry in functionsByModule) {
      moduleEntry.Value.Sort((a, b) => b.SelfTimePercentage.CompareTo(a.SelfTimePercentage));
    }

    return functionsByModule;
  }

  /// <summary>
  /// Saves process baseline data to CSV file
  /// </summary>
  private void SaveProcessBaseline(List<ProcessBaselineEntry> data, string filePath) {
    var csv = new StringBuilder();
    csv.AppendLine("Name,WeightPercentage,DurationMs,ProcessId,CommandLine");
    
    foreach (var entry in data.OrderByDescending(x => x.WeightPercentage).ThenBy(x => x.Name)) {
      csv.AppendLine($"\"{EscapeCsvValue(entry.Name)}\"," +
                     $"{entry.WeightPercentage.ToString("F4", CultureInfo.InvariantCulture)}," +
                     $"{entry.DurationMs.ToString("F4", CultureInfo.InvariantCulture)}," +
                     $"{entry.ProcessId}," +
                     $"\"{EscapeCsvValue(entry.CommandLine)}\"");
    }
    
    File.WriteAllText(filePath, csv.ToString());
  }

  /// <summary>
  /// Saves module baseline data to CSV file
  /// </summary>
  private void SaveModuleBaseline(List<ModuleBaselineEntry> data, string filePath) {
    var csv = new StringBuilder();
    csv.AppendLine("Name,WeightPercentage,TimeMs");
    
    foreach (var entry in data.OrderByDescending(x => x.TimeMs).ThenBy(x => x.Name)) {
      csv.AppendLine($"\"{EscapeCsvValue(entry.Name)}\"," +
                     $"{entry.WeightPercentage.ToString("F4", CultureInfo.InvariantCulture)}," +
                     $"{entry.TimeMs.ToString("F4", CultureInfo.InvariantCulture)}");
    }
    
    File.WriteAllText(filePath, csv.ToString());
  }

  /// <summary>
  /// Saves function baseline data to CSV file
  /// </summary>
  private void SaveFunctionBaseline(List<FunctionBaselineEntry> data, string filePath) {
    var csv = new StringBuilder();
    csv.AppendLine("Name,Address,Module,SelfTimePercentage,SelfTimeMs,TotalTimePercentage,TotalTimeMs");
    
    foreach (var entry in data.OrderByDescending(x => x.SelfTimeMs).ThenBy(x => x.Name)) {
      csv.AppendLine($"\"{EscapeCsvValue(entry.Name)}\"," +
                     $"\"{EscapeCsvValue(entry.Address)}\"," +
                     $"\"{EscapeCsvValue(entry.Module)}\"," +
                     $"{entry.SelfTimePercentage.ToString("F4", CultureInfo.InvariantCulture)}," +
                     $"{entry.SelfTimeMs.ToString("F4", CultureInfo.InvariantCulture)}," +
                     $"{entry.TotalTimePercentage.ToString("F4", CultureInfo.InvariantCulture)}," +
                     $"{entry.TotalTimeMs.ToString("F4", CultureInfo.InvariantCulture)}");
    }
    
    File.WriteAllText(filePath, csv.ToString());
  }

  /// <summary>
  /// Compares current process data with baseline
  /// </summary>
  private void CompareBaselineFiles(string baselineDir, string tempDir, string testCaseName) {
    // Get all baseline CSV files
    var baselineFiles = Directory.GetFiles(baselineDir, "*_baseline.csv");
    
    foreach (var baselineFile in baselineFiles) {
      var fileName = Path.GetFileName(baselineFile);
      var currentFile = Path.Combine(tempDir, fileName);
      
      if (!File.Exists(currentFile)) {
        Assert.Fail($"Current results file not found: {fileName}");
      }
      
      var baselineContent = File.ReadAllText(baselineFile);
      var currentContent = File.ReadAllText(currentFile);
      
      if (baselineContent != currentContent) {
        var fileType = GetFileTypeFromName(fileName);
        Assert.Fail($"Baseline mismatch in {fileType} for test case '{testCaseName}'. " +
                   $"File: {fileName}\n" +
                   $"Expected content length: {baselineContent.Length}\n" +
                   $"Actual content length: {currentContent.Length}\n" +
                   $"First difference at character: {FindFirstDifference(baselineContent, currentContent)}");
      }
    }
  }
  
  private string GetFileTypeFromName(string fileName) {
    if (fileName.StartsWith("processes_")) return "processes";
    if (fileName.StartsWith("modules_")) return "modules";
    if (fileName.StartsWith("functions_")) return "functions";
    return "unknown";
  }
  
  private int FindFirstDifference(string expected, string actual) {
    int minLength = Math.Min(expected.Length, actual.Length);
    for (int i = 0; i < minLength; i++) {
      if (expected[i] != actual[i]) {
        return i;
      }
    }
    return minLength; // Difference is in length
  }

  /// <summary>
  /// Helper method to escape CSV values
  /// </summary>
  private string EscapeCsvValue(string value) {
    if (string.IsNullOrEmpty(value)) return "";
    return value.Replace("\"", "\"\"");
  }

  /// <summary>
  /// Provides test case data for parameterized tests.
  /// </summary>
  /// <returns>Test case data for MSTest DataTestMethod</returns>
  public static IEnumerable<object[]> GetTestCaseData() {
    foreach (var testCase in TestCases) {
      yield return new object[] { testCase };
    }
  }
}
