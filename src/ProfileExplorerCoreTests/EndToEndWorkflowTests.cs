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
using ProfileExplorer.Core;
using ProfileExplorer.Core.Binary;
using ProfileExplorer.Core.Compilers.Architecture;
using ProfileExplorer.Core.Compilers.ASM;
using ProfileExplorer.Core.Profile.CallTree;
using ProfileExplorer.Core.Profile.Data;
using ProfileExplorer.Core.Profile.ETW;
using ProfileExplorer.Core.Providers;
using ProfileExplorer.Core.Session;
using ProfileExplorer.Core.Settings;
using ProfileExplorer.Core.Utilities;

namespace ProfileExplorer.CoreTests;

/// <summary>
/// End-to-end workflow tests for Profile Explorer functionality.
/// 
/// These tests validate the complete pipeline from trace loading to assembly analysis by comparing
/// extracted data against known baselines. Four types of baselines are validated:
/// 
/// 1. Process Baselines: CPU usage, duration, and process information
/// 2. Module Baselines: Time spent per module/DLL  
/// 3. Function Baselines: Performance data per function
/// 4. Assembly Baselines: Assembly/disassembly text for specific binary-function pairs
/// 
/// ADDING NEW TEST CASES:
/// 
/// 1. Add test case name to TestCases list
/// 2. (Optional) Add process ID to TestCaseProcessIds if known
/// 3. (Optional) Configure assembly baselines in TestCaseAssemblyBaselines
/// 4. Create TestData/<testcase>/ directory with:
///    - Traces/ subdirectory containing ETW trace files
///    - Binaries/ subdirectory containing executable/library files
///    - Symbols/ subdirectory containing PDB symbol files
/// 5. Run GenerateBaseline_ForTestCase (remove [Ignore] attribute) to create baselines
/// 6. Re-enable [Ignore] attribute and run TestEndToEndWorkflow_ForTestCase
/// 
/// ADDING ASSEMBLY BASELINES:
/// 
/// Assembly baselines capture the disassembly text for specific functions, which helps detect
/// when compiler optimizations or code changes affect the generated assembly. To add new ones:
/// 
/// 1. Add (binary name, function name) pairs to TestCaseAssemblyBaselines for your test case:
///    { "MyTestCase", new List<(string, string)> {
///        ("MyApp.exe", "main"),
///        ("MyLibrary.dll", "CriticalFunction")
///      }
///    }
/// 
/// 2. Run GenerateBaseline to create assembly_<binary>_baseline.csv files
/// 3. Each CSV contains function names and their complete assembly text
/// 
/// BASELINE FILE STRUCTURE:
/// 
/// TestData/<testcase>/
/// ├── processes_baseline.csv       (process CPU usage data)
/// ├── modules_baseline.csv         (module timing data)  
/// ├── functions_<module>_baseline.csv (function performance per module)
/// └── assembly_<binary>_baseline.csv  (assembly text per binary)
/// 
/// Example assembly baseline entry:
/// FunctionName,AssemblyText
/// "main","push rbp\nmov rbp,rsp\nsub rsp,20h\n..."
/// 
/// MAINTENANCE:
/// 
/// - Baselines may need regeneration when:
///   * Compiler versions change
///   * Build configurations change  
///   * Code optimizations are modified
///   * Symbol resolution improves
/// 
/// - Use GenerateBaseline_ForTestCase to update baselines after intentional changes
/// - Review baseline diffs carefully to ensure changes are expected
/// </summary>
[TestClass]
public class EndToEndWorkflowTests {
  
  /// <summary>
  /// List of test cases to run. Add new test case names here to include them in testing.
  /// Each test case should have corresponding directories in TestData/Traces/, TestData/Binaries/, and TestData/Symbols/.
  /// 
  /// The test infrastructure validates multiple types of baselines:
  /// - Process baselines: CPU usage and process information
  /// - Module baselines: Time spent per module/DLL
  /// - Function baselines: Performance data per function
  /// - Assembly baselines: Assembly/disassembly text for specific binary-function pairs
  /// 
  /// Assembly baselines are configured in TestCaseAssemblyBaselines and only generated for explicitly
  /// specified (binary, function) pairs to avoid creating excessive baseline files.
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
  /// Configuration for assembly baseline generation. Each test case maps to a list of (binary name, function name) pairs
  /// for which assembly/disassembly text should be retrieved and baselined.
  /// 
  /// To add assembly baselines for a new test case:
  /// 1. Add the test case name as a key in this dictionary
  /// 2. Provide a list of (binary, function) tuples for the functions you want to baseline
  /// 3. Run the GenerateBaseline test to create the baseline CSV files
  /// 4. The test will create assembly_<binary>.csv files in the TestData/<testcase>/ directory
  /// 
  /// Example:
  /// { "MyTestCase", new List<(string, string)> {
  ///     ("MyApp.exe", "main"),
  ///     ("MyLibrary.dll", "ImportantFunction"),
  ///     ("MyLibrary.dll", "AnotherFunction")
  ///   }
  /// }
  /// </summary>
  private static readonly Dictionary<string, List<(string binaryName, string functionName)>> TestCaseAssemblyBaselines = 
    new Dictionary<string, List<(string, string)>> {
      { "MsoTrace", new List<(string, string)> {
          ("Mso20win32client.dll", "Mso::Experiment::EcsNS::Private::SortByParameterGroups")
          // Add more (binary, function) pairs here as needed:
          // ("Mso20win32client.dll", "AnotherFunction"),
          // ("OtherBinary.dll", "SomeFunction")
        }
      }
      // Add assembly baseline configurations for other test cases here, e.g.:
      // { "WebBrowserTrace", new List<(string, string)> {
      //     ("browser.exe", "RenderPage"),
      //     ("engine.dll", "ProcessHTML")
      //   }
      // }
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

  /// <summary>
  /// Data structure representing an assembly baseline entry for a specific binary and function pair.
  /// Contains the assembly/disassembly text that should be consistent across test runs.
  /// </summary>
  public class AssemblyBaselineEntry {
    public string BinaryName { get; set; }
    public string FunctionName { get; set; }
    public string AssemblyText { get; set; }
  }

  [DataTestMethod]
  [DynamicData(nameof(GetTestCaseData), DynamicDataSourceType.Method)]
  public async Task TestEndToEndWorkflow_ForTestCase(string testCaseName) {
    Console.WriteLine($"\n=== Running End-to-End Workflow Test for: {testCaseName} ===");
    
    // Execute common workflow steps
    var (processBaselineData, moduleBaselineData, functionBaselineData, assemblyBaselineData) = await ExecuteCommonWorkflowSteps(testCaseName);

    // Step 7: Compare with baselines
    Console.WriteLine($"\n=== Step 7: Baseline Validation ===");
    
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

    // Check if assembly baseline files exist for all binaries
    if (baselinesExist) {
      foreach (var binaryName in assemblyBaselineData.Keys) {
        var assemblyBaselinePath = Path.Combine(baselineDir, $"assembly_{SanitizeFileName(binaryName)}_baseline.csv");
        if (!File.Exists(assemblyBaselinePath)) {
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

        // Save assembly baselines grouped by binary
        foreach (var binaryGroup in assemblyBaselineData) {
          var currentAssemblyFile = Path.Combine(tempDir, $"assembly_{SanitizeFileName(binaryGroup.Key)}_baseline.csv");
          SaveAssemblyBaseline(binaryGroup.Value, currentAssemblyFile);
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
    var (processBaselineData, moduleBaselineData, functionBaselineData, assemblyBaselineData) = await ExecuteCommonWorkflowSteps(testCaseName);

    // Step 7: Save baselines to TestData folder
    Console.WriteLine($"\n=== Step 7: Saving Baselines ===");
    
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

    // Save assembly baselines for each binary
    var totalAssemblyFunctions = 0;
    foreach (var binaryEntry in assemblyBaselineData) {
      var binaryName = binaryEntry.Key;
      var assemblyFunctions = binaryEntry.Value;
      var assemblyBaselinePath = Path.Combine(baselineDir, $"assembly_{SanitizeFileName(binaryName)}_baseline.csv");
      SaveAssemblyBaseline(assemblyFunctions, assemblyBaselinePath);
      totalAssemblyFunctions += assemblyFunctions.Count;
      Console.WriteLine($"  - Assembly for {binaryName}: {assemblyFunctions.Count} functions -> {assemblyBaselinePath}");
    }

    Console.WriteLine($"✓ Baselines saved to TestData folder for test case '{testCaseName}'");
    Console.WriteLine($"  - Processes: {processBaselineData.Count} entries -> {processBaselinePath}");
    Console.WriteLine($"  - Modules: {moduleBaselineData.Count} entries -> {moduleBaselinePath}");
    Console.WriteLine($"  - Functions: {totalFunctions} entries across {functionBaselineData.Count} modules");
    Console.WriteLine($"  - Assembly: {totalAssemblyFunctions} functions across {assemblyBaselineData.Count} binaries");
    Console.WriteLine($"\n✓ Baseline generation completed successfully for test case '{testCaseName}'");
  }

  /// <summary>
  /// Executes the common workflow steps (1-5) shared by both test methods
  /// </summary>
  private async Task<(List<ProcessBaselineEntry> processBaselineData, List<ModuleBaselineEntry> moduleBaselineData, Dictionary<string, List<FunctionBaselineEntry>> functionBaselineData, Dictionary<string, List<AssemblyBaselineEntry>> assemblyBaselineData)> ExecuteCommonWorkflowSteps(string testCaseName) {
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
    symbolSettings.InsertSymbolPath(testCase.BinariesPath); // Add our binaries directory
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

    // Step 6: Get assembly baseline data
    Console.WriteLine($"\n=== Step 6: Extracting Assembly Information ===");
    var assemblyBaselineData = await ExtractAssemblyBaselineData(session, testCaseName);

    return (processBaselineData, moduleBaselineData, functionBaselineData, assemblyBaselineData);
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
  /// Extracts assembly baseline data for configured binary and function pairs from the session.
  /// Returns a dictionary grouped by binary name, where each entry contains the assembly text for functions in that binary.
  /// </summary>
  private async Task<Dictionary<string, List<AssemblyBaselineEntry>>> ExtractAssemblyBaselineData(ISession session, string testCaseName) {
    var assemblyByBinary = new Dictionary<string, List<AssemblyBaselineEntry>>();
    
    // Check if this test case has assembly baseline configuration
    if (!TestCaseAssemblyBaselines.TryGetValue(testCaseName, out var binaryFunctionPairs)) {
      Console.WriteLine($"No assembly baseline configuration found for test case '{testCaseName}'");
      return assemblyByBinary;
    }

    Console.WriteLine($"Processing {binaryFunctionPairs.Count} assembly baseline pairs for test case '{testCaseName}'");

    foreach (var (binaryName, functionName) in binaryFunctionPairs) {
      Console.WriteLine($"  - Retrieving assembly for {binaryName}::{functionName}");
      
      try {
        var section = await GetSectionForFunction(session, binaryName, functionName);
        if (section == null) {
          Console.WriteLine($"    WARNING: Could not find section for {binaryName}::{functionName}");
          continue;
        }

        var parsedSection = await session.LoadAndParseSection(section);
        if (parsedSection?.Text == null) {
          Console.WriteLine($"    WARNING: Could not parse section or get assembly text for {binaryName}::{functionName}");
          continue;
        }

        var assemblyEntry = new AssemblyBaselineEntry {
          BinaryName = binaryName,
          FunctionName = functionName,
          AssemblyText = parsedSection.Text.ToString()
        };

        // Group by binary
        if (!assemblyByBinary.ContainsKey(binaryName)) {
          assemblyByBinary[binaryName] = new List<AssemblyBaselineEntry>();
        }
        assemblyByBinary[binaryName].Add(assemblyEntry);
        
        Console.WriteLine($"    ✓ Retrieved {parsedSection.Text.Length} characters of assembly text");
      }
      catch (Exception ex) {
        Console.WriteLine($"    ERROR: Failed to retrieve assembly for {binaryName}::{functionName}: {ex.Message}");
        // Continue processing other pairs instead of failing the entire test
      }
    }

    // Sort functions within each binary alphabetically for consistent ordering
    foreach (var binaryEntry in assemblyByBinary) {
      binaryEntry.Value.Sort((a, b) => string.Compare(a.FunctionName, b.FunctionName, StringComparison.OrdinalIgnoreCase));
    }

    Console.WriteLine($"Successfully extracted assembly data for {assemblyByBinary.Values.Sum(list => list.Count)} functions across {assemblyByBinary.Count} binaries");
    return assemblyByBinary;
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
  /// Saves assembly baseline data to CSV file for a specific binary.
  /// Each row contains the function name and its complete assembly/disassembly text.
  /// </summary>
  private void SaveAssemblyBaseline(List<AssemblyBaselineEntry> data, string filePath) {
    var csv = new StringBuilder();
    csv.AppendLine("FunctionName,AssemblyText");
    
    foreach (var entry in data.OrderBy(x => x.FunctionName)) {
      csv.AppendLine($"\"{EscapeCsvValue(entry.FunctionName)}\"," +
                     $"\"{EscapeCsvValue(entry.AssemblyText)}\"");
    }
    
    File.WriteAllText(filePath, csv.ToString());
  }

  /// <summary>
  /// Loads assembly baseline data from CSV file for debugging or manual inspection purposes.
  /// </summary>
  private List<AssemblyBaselineEntry> LoadAssemblyBaseline(string filePath, string binaryName) {
    var entries = new List<AssemblyBaselineEntry>();
    
    if (!File.Exists(filePath)) {
      return entries;
    }

    var lines = File.ReadAllLines(filePath);
    if (lines.Length <= 1) { // Skip header or empty files
      return entries;
    }

    for (int i = 1; i < lines.Length; i++) { // Skip header line
      var line = lines[i];
      if (string.IsNullOrWhiteSpace(line)) continue;

      // Simple CSV parsing - assumes properly escaped values
      var parts = ParseCsvLine(line);
      if (parts.Count >= 2) {
        entries.Add(new AssemblyBaselineEntry {
          BinaryName = binaryName,
          FunctionName = UnescapeCsvValue(parts[0]),
          AssemblyText = UnescapeCsvValue(parts[1])
        });
      }
    }

    return entries;
  }

  /// <summary>
  /// Simple CSV line parser that handles quoted values
  /// </summary>
  private List<string> ParseCsvLine(string line) {
    var parts = new List<string>();
    var current = new StringBuilder();
    bool inQuotes = false;
    
    for (int i = 0; i < line.Length; i++) {
      char c = line[i];
      
      if (c == '"') {
        if (inQuotes && i + 1 < line.Length && line[i + 1] == '"') {
          // Double quote escape
          current.Append('"');
          i++; // Skip next quote
        } else {
          inQuotes = !inQuotes;
        }
      } else if (c == ',' && !inQuotes) {
        parts.Add(current.ToString());
        current.Clear();
      } else {
        current.Append(c);
      }
    }
    
    parts.Add(current.ToString());
    return parts;
  }

  /// <summary>
  /// Unescapes CSV values by handling double quotes
  /// </summary>
  private string UnescapeCsvValue(string value) {
    if (string.IsNullOrEmpty(value)) return "";
    return value.Replace("\"\"", "\"");
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

  private async Task<IRTextSection> GetSectionForFunction(ISession session, string targetModuleName, string targetFunctionName) {
    // Assume you have: string targetModuleName, string targetFunctionName
    // And you have loaded: List<IRTextSummary> summaries (one per module/binary)

    List<IRTextSummary> summaries = session.Documents
      .Select(doc => doc.Summary)
      .Where(summary => summary != null)
      .ToList();

    foreach (var summary in summaries) {
      // Match the module/binary name (case-insensitive)
      if (!string.Equals(summary.ModuleName, targetModuleName, StringComparison.OrdinalIgnoreCase))
        continue;

      // Find the function by name (case-insensitive, adjust matching as needed)
      var function = summary.Functions
          .FirstOrDefault(f => session.CompilerInfo.NameProvider.FormatFunctionName(f).Contains(targetFunctionName));

      if (function != null && function.SectionCount > 0) {
        // Use the first section (or select a specific one if needed)
        return function.Sections[0];
      }
    }

    return null;
  }

  private string GetFileTypeFromName(string fileName) {
    if (fileName.StartsWith("processes_")) return "processes";
    if (fileName.StartsWith("modules_")) return "modules";
    if (fileName.StartsWith("functions_")) return "functions";
    if (fileName.StartsWith("assembly_")) return "assembly";
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
