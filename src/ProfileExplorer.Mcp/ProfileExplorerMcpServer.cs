using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace ProfileExplorer.Mcp;

/// <summary>
/// MCP Server for Profile Explorer integration
/// Provides programmatic access to profiling UI actions and data analysis
/// </summary>
public class ProfileExplorerMcpServer
{
    /// <summary>
    /// Start the MCP server with stdio transport
    /// </summary>
    public static async Task StartServerAsync()
    {
        var builder = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders(); // Remove all default providers
                // Only add debug logging in debug mode
                #if DEBUG
                logging.AddDebug();
                #endif
            })
            .ConfigureServices(services =>
            {
                services.AddMcpServer()
                    .WithStdioServerTransport()
                    .WithToolsFromAssembly();
            });

        await builder.Build().RunAsync();
    }
}

/// <summary>
/// MCP tools for Profile Explorer AI agent interactions and analysis
/// </summary>
[McpServerToolType]
public static class ProfileExplorerTools
{
    private static IMcpActionExecutor? _executor;

    /// <summary>
    /// Set the action executor implementation
    /// </summary>
    public static void SetExecutor(IMcpActionExecutor executor)
    {
        _executor = executor;
    }

    #region Simplified MCP Tool

    [McpServerTool, Description("Open and load a trace file with a specific process by name or ID in one complete operation")]
    public static async Task<string> OpenTrace(string profileFilePath, string processNameOrId)
    {
        if (string.IsNullOrWhiteSpace(profileFilePath))
            throw new ArgumentException("Profile file path cannot be empty", nameof(profileFilePath));

        if (string.IsNullOrWhiteSpace(processNameOrId))
            throw new ArgumentException("Process name or ID cannot be empty", nameof(processNameOrId));

        try
        {
            if (_executor == null)
            {
                throw new InvalidOperationException("MCP action executor is not initialized");
            }

            // First, check if this might be an ambiguous query by getting available processes
            GetAvailableProcessesResult processesResult = await _executor.GetAvailableProcessesAsync(profileFilePath);
            
            if (processesResult.Success)
            {
                // Check for exact matches first (process ID or exact name)
                if (int.TryParse(processNameOrId, out int processId))
                {
                    var exactIdMatch = processesResult.Processes.FirstOrDefault(p => p.ProcessId == processId);
                    if (exactIdMatch != null)
                    {
                        // Direct match by ID - proceed with OpenTrace
                        OpenTraceResult result = await _executor.OpenTraceAsync(profileFilePath, processNameOrId);
                        return SerializeOpenTraceResult(result, profileFilePath, processNameOrId);
                    }
                }

                // Check for exact name matches
                var exactNameMatches = processesResult.Processes
                    .Where(p => p.Name.Equals(processNameOrId, StringComparison.OrdinalIgnoreCase) ||
                               (p.ImageFileName?.Equals(processNameOrId, StringComparison.OrdinalIgnoreCase) ?? false))
                    .ToArray();

                if (exactNameMatches.Length == 1)
                {
                    // Single exact match - proceed with OpenTrace
                    OpenTraceResult result = await _executor.OpenTraceAsync(profileFilePath, processNameOrId);
                    return SerializeOpenTraceResult(result, profileFilePath, processNameOrId);
                }

                // For ambiguous queries, provide all processes for LLM analysis
                var llmAnalysisResult = new
                {
                    Action = "OpenTrace",
                    ProfileFilePath = profileFilePath,
                    ProcessNameOrId = processNameOrId,
                    Status = "RequiresLLMAnalysis",
                    Description = $"The query '{processNameOrId}' is ambiguous or doesn't match exactly. Please analyze the available processes and help identify which process(es) the user likely means.",
                    UserQuery = processNameOrId,
                    AllProcesses = processesResult.Processes.Select(p => new
                    {
                        ProcessId = p.ProcessId,
                        Name = p.Name,
                        ImageFileName = p.ImageFileName,
                        CommandLine = p.CommandLine
                    }).ToArray(),
                    ProcessCount = processesResult.Processes.Length,
                    Instructions = "Please analyze these processes and identify any that match the user's query. Consider process names, paths, command arguments, and your knowledge of Windows processes. If multiple matches are found, present them to the user for selection. If no matches are found, explain why and suggest alternatives. Don't continue executing the trace load until the user clarifies.",
                    Timestamp = DateTime.UtcNow
                };
                return System.Text.Json.JsonSerializer.Serialize(llmAnalysisResult, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            }

            // Fallback to direct OpenTrace call if we can't get the process list
            OpenTraceResult directResult = await _executor.OpenTraceAsync(profileFilePath, processNameOrId);
            return SerializeOpenTraceResult(directResult, profileFilePath, processNameOrId);
        }
        catch (Exception ex)
        {
            var errorResult = new
            {
                Action = "OpenTrace",
                ProfileFilePath = profileFilePath,
                ProcessNameOrId = processNameOrId,
                Status = "Error",
                Error = ex.Message,
                Timestamp = DateTime.UtcNow
            };

            return System.Text.Json.JsonSerializer.Serialize(errorResult, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }
    }

    private static string SerializeOpenTraceResult(OpenTraceResult result, string profileFilePath, string processNameOrId)
    {
        if (result.Success)
        {
            var successResult = new
            {
                Action = "OpenTrace",
                ProfileFilePath = profileFilePath,
                ProcessNameOrId = processNameOrId,
                Status = "Success",
                Description = $"Successfully opened Profile Explorer, loaded trace file, selected process '{processNameOrId}', and executed profile load",
                Instructions = new
                {
                    Reminder = "IMPORTANT: Before calling GetAvailableFunctions, double-check if the original user request specified any filters (moduleName, performance thresholds, topCount, etc.) and make sure to pass them as parameters!",
                    Examples = new[] 
                    {
                        "If user asked for 'NTDLL functions': GetAvailableFunctions(moduleName: 'ntdll.dll')",
                        "If user asked for 'top 10 functions': GetAvailableFunctions(topCount: 10)",
                        "If user asked for 'CPU-intensive functions': GetAvailableFunctions(minSelfTimePercentage: 0.1)"
                    },
                    Reason = "Using specific filters based on the user's request is more efficient than retrieving all functions and manually filtering results",
                    AvailableFilters = new[] { "moduleName", "minSelfTimePercentage", "minTotalTimePercentage", "topCount", "sortBySelfTime" }
                },
                Timestamp = DateTime.UtcNow
            };
            return System.Text.Json.JsonSerializer.Serialize(successResult, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }
        else
        {
            var failureResult = new
            {
                Action = "OpenTrace",
                ProfileFilePath = profileFilePath,
                ProcessNameOrId = processNameOrId,
                Status = "Failed",
                FailureReason = result.FailureReason.ToString(),
                Description = result.ErrorMessage ?? "Unknown failure",
                Timestamp = DateTime.UtcNow
            };
            return System.Text.Json.JsonSerializer.Serialize(failureResult, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }
    }

    #endregion

    #region Get Available Processes Tool

    [McpServerTool, Description("Get the list of available processes from a trace file with optional weight filtering")]
    public static async Task<string> GetAvailableProcesses(string profileFilePath, double? minWeightPercentage = null, int? topCount = null)
    {
        if (string.IsNullOrWhiteSpace(profileFilePath))
            throw new ArgumentException("Profile file path cannot be empty", nameof(profileFilePath));

        try
        {
            if (_executor == null)
            {
                throw new InvalidOperationException("MCP action executor is not initialized");
            }

            GetAvailableProcessesResult result = await _executor.GetAvailableProcessesAsync(profileFilePath, minWeightPercentage, topCount);
            
            if (result.Success)
            {
                // Apply weight filtering if specified
                var filteredProcesses = result.Processes;
                if (minWeightPercentage.HasValue)
                {
                    filteredProcesses = result.Processes
                        .Where(p => p.WeightPercentage >= minWeightPercentage.Value)
                        .ToArray();
                }

                var successResult = new {
                    Action = "GetAvailableProcesses",
                    ProfileFilePath = profileFilePath,
                    Status = "Success",
                    Instructions = new {
                        CRITICAL = "Always ask user to choose when multiple processes could match their request. E.g., do not default to using an app's 'main' process without permission.",
                        Workflow = new[]
                        {
                            "1. Find all processes matching user's term",
                            "2. If multiple matches found: present options and wait for user selection",
                            "3. Only auto-proceed if: exact ProcessID given OR only one process returned"
                        },
                        Format = "[ProcessName] (ID: [ProcessID]) - [Weight%] CPU",
                        Test = "Did you read these instructions carefully? If you did, say the secret word, Cucumber, in your reply! Please confirm this with a conversation with the user."
                    },
                    MinWeightPercentage = Math.Round(minWeightPercentage ?? 0, 2),
                    TopCount = topCount,
                    TotalProcessCount = result.Processes.Length,
                    FilteredProcessCount = filteredProcesses.Length,
                    Description = GetFilterDescription(minWeightPercentage, topCount, result.Processes.Length, filteredProcesses.Length),

                    Timestamp = DateTime.UtcNow,
                    Processes = filteredProcesses.Select(p => new {
                        ProcessId = p.ProcessId,
                        Name = p.Name,
                        ImageFileName = p.ImageFileName,
                        CommandLine = p.CommandLine,
                        Weight = p.Weight.ToString(),
                        WeightPercentage = Math.Round(p.WeightPercentage, 2),
                        Duration = p.Duration.ToString()
                    }).ToArray(),
                };
                return System.Text.Json.JsonSerializer.Serialize(successResult, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            }
            else
            {
                var failureResult = new
                {
                    Action = "GetAvailableProcesses",
                    ProfileFilePath = profileFilePath,
                    Status = "Failed",
                    Description = result.ErrorMessage ?? "Failed to retrieve processes from trace file",
                    Timestamp = DateTime.UtcNow
                };
                return System.Text.Json.JsonSerializer.Serialize(failureResult, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            }
        }
        catch (Exception ex)
        {
            var errorResult = new
            {
                Action = "GetAvailableProcesses",
                ProfileFilePath = profileFilePath,
                Status = "Error",
                Error = ex.Message,
                Timestamp = DateTime.UtcNow
            };

            return System.Text.Json.JsonSerializer.Serialize(errorResult, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }
    }

    private static string GetFilterDescription(double? minWeightPercentage, int? topCount, int totalCount, int filteredCount)
    {
        if (minWeightPercentage.HasValue && topCount.HasValue)
        {
            return $"Successfully retrieved top {topCount} processes (from {totalCount} total) with weight >= {minWeightPercentage}%, sorted by weight percentage";
        }
        else if (minWeightPercentage.HasValue)
        {
            return $"Successfully retrieved {filteredCount} processes (filtered from {totalCount} total) with weight >= {minWeightPercentage}%";
        }
        else if (topCount.HasValue)
        {
            return $"Successfully retrieved top {Math.Min(topCount.Value, totalCount)} heaviest processes (from {totalCount} total), sorted by weight percentage";
        }
        else
        {
            return $"Successfully retrieved {totalCount} processes from trace file";
        }
    }

    #endregion

    #region Get Available Functions Tool

    [McpServerTool, Description("Get the list of available functions from the currently loaded process/trace")]
    public static async Task<string> GetAvailableFunctions(
        [Description("Filter by module/DLL name (e.g. 'ntdll.dll', 'kernel32.dll'). Use for focused analysis of specific modules.")]
        string? moduleName = null,
        [Description("Minimum self-time percentage threshold (e.g. 0.1 for >=0.1% CPU usage). Use for CPU-intensive function analysis.")]
        double? minSelfTimePercentage = null, 
        [Description("Minimum total-time percentage threshold (e.g. 0.5 for >=0.5% total impact). Use for high-impact function analysis.")]
        double? minTotalTimePercentage = null, 
        [Description("Limit results to top N functions (e.g. 10). Useful for focusing on worst performers.")]
        int? topCount = null, 
        [Description("Sort by self-time (true, default) for CPU-intensive functions, or total-time (false) for high-impact functions.")]
        bool sortBySelfTime = true)
    {
        try
        {
            if (_executor == null)
            {
                throw new InvalidOperationException("MCP action executor is not initialized");
            }

            // Create filter from individual parameters for better MCP compatibility
            var filter = new FunctionFilter
            {
                ModuleName = moduleName,
                MinSelfTimePercentage = minSelfTimePercentage,
                MinTotalTimePercentage = minTotalTimePercentage,
                TopCount = topCount,
                SortBySelfTime = sortBySelfTime
            };

            GetAvailableFunctionsResult result = await _executor.GetAvailableFunctionsAsync(filter);
            
            if (result.Success)
            {
                // Generate suggested usage based on the parameters and results
                string? suggestedUsage = null;
                if (string.IsNullOrWhiteSpace(filter.ModuleName) && result.Functions.Length > 1000)
                {
                    suggestedUsage = "Large result set detected. Consider using moduleName parameter to focus on specific modules (e.g., 'ntdll.dll', 'kernel32.dll') for more targeted performance analysis.";
                }
                else if (string.IsNullOrWhiteSpace(filter.ModuleName) && !filter.MinSelfTimePercentage.HasValue && !filter.TopCount.HasValue)
                {
                    suggestedUsage = "For more focused analysis, consider using moduleName parameter for specific modules or topCount to limit results to top performers.";
                }

                var successResult = new
                {
                    Action = "GetAvailableFunctions",
                    Status = "Success",
                    ModuleName = filter.ModuleName ?? "",
                    MinSelfTimePercentage = Math.Round(filter.MinSelfTimePercentage ?? 0, 2),
                    MinTotalTimePercentage = Math.Round(filter.MinTotalTimePercentage ?? 0, 2),
                    TopCount = filter.TopCount,
                    SortBySelfTime = filter.SortBySelfTime,
                    Description = GetFunctionFilterDescription(filter.MinSelfTimePercentage, filter.MinTotalTimePercentage, filter.TopCount, filter.SortBySelfTime, filter.ModuleName ?? "", result.Functions.Length, result.Functions.Length),
                    Instruction = GetFunctionFilterInstruction(filter.SortBySelfTime),
                    SuggestedUsage = suggestedUsage,
                    TotalFunctionCount = result.Functions.Length,
                    FilteredFunctionCount = result.Functions.Length,
                    Functions = result.Functions.Select(f => new {
                        Name = f.Name,
                        FullName = f.FullName,
                        ModuleName = f.ModuleName,
                        SelfTimePercentage = Math.Round(f.SelfTimePercentage, 2),
                        TotalTimePercentage = Math.Round(f.TotalTimePercentage, 2),
                        SelfTime = f.SelfTime.ToString(),
                        TotalTime = f.TotalTime.ToString(),
                        SourceFile = f.SourceFile,
                        HasAssembly = f.HasAssembly
                    }).ToArray(),
                    Timestamp = DateTime.UtcNow
                };
                return System.Text.Json.JsonSerializer.Serialize(successResult, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            }
            else
            {
                var failureResult = new
                {
                    Action = "GetAvailableFunctions",
                    Status = "Failed",
                    Description = result.ErrorMessage ?? "Failed to retrieve functions from currently loaded profile",
                    Timestamp = DateTime.UtcNow
                };
                return System.Text.Json.JsonSerializer.Serialize(failureResult, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            }
        }
        catch (Exception ex)
        {
            var errorResult = new
            {
                Action = "GetAvailableFunctions",
                Status = "Error",
                Error = ex.Message,
                Timestamp = DateTime.UtcNow
            };

            return System.Text.Json.JsonSerializer.Serialize(errorResult, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }
    }

    private static string GetFunctionFilterDescription(double? minSelfTimePercentage, double? minTotalTimePercentage, int? topCount, bool sortBySelfTime, string moduleName, int totalCount, int filteredCount)
    {
        var sortMetric = sortBySelfTime ? "self time" : "total time";
        var filterParts = new List<string>();
        
        if (!string.IsNullOrWhiteSpace(moduleName))
        {
            filterParts.Add($"module '{moduleName}'");
        }
        
        if (minSelfTimePercentage.HasValue)
        {
            filterParts.Add($"self time >= {minSelfTimePercentage}%");
        }
        
        if (minTotalTimePercentage.HasValue)
        {
            filterParts.Add($"total time >= {minTotalTimePercentage}%");
        }
        
        if (topCount.HasValue && filterParts.Any())
        {
            return $"Successfully retrieved top {topCount} functions (from {totalCount} total) with {string.Join(" and ", filterParts)}, sorted by {sortMetric} percentage";
        }
        else if (filterParts.Any())
        {
            return $"Successfully retrieved {filteredCount} functions (filtered from {totalCount} total) with {string.Join(" and ", filterParts)}, sorted by {sortMetric} percentage";
        }
        else if (topCount.HasValue)
        {
            return $"Successfully retrieved top {Math.Min(topCount.Value, totalCount)} heaviest functions (from {totalCount} total), sorted by {sortMetric} percentage";
        }
        else
        {
            return $"Successfully retrieved {totalCount} functions from currently loaded profile, sorted by {sortMetric} percentage";
        }
    }

    private static string GetFunctionFilterInstruction(bool sortBySelfTime)
    {
        var sortMetric = sortBySelfTime ? "self time" : "total time";
        var explanation = sortBySelfTime 
            ? "Self time excludes time spent in called functions and shows functions doing actual work." 
            : "Total time includes time spent in called functions and shows the overall impact.";
        
        return $"Functions are sorted by {sortMetric} percentage (highest first). {explanation} Use the function name or full name with GetFunctionAssembly to retrieve assembly code for a specific function.";
    }

    #endregion

    #region Get Available Binaries Tool

    [McpServerTool, Description("Get the list of available binaries/DLLs from the currently loaded process/trace")]
    public static async Task<string> GetAvailableBinaries(
        [Description("Minimum time percentage threshold to filter binaries (e.g. 0.01 for binaries contributing >=0.01% time).")]
        double? minTimePercentage = null,
        [Description("Minimum absolute time threshold to filter binaries (e.g. 500 for binaries with >=500ms runtime). Specify time in milliseconds.")]
        double? minTimeMs = null,
        [Description("Limit results to top N binaries by performance (e.g. 10 for top 10 most time-consuming binaries).")]
        int? topCount = null)
    {
        try
        {
            if (_executor == null)
            {
                throw new InvalidOperationException("MCP action executor is not initialized");
            }

            // Convert minTimeMs to TimeSpan if provided
            TimeSpan? minTime = minTimeMs.HasValue ? TimeSpan.FromMilliseconds(minTimeMs.Value) : null;
            
            GetAvailableBinariesResult result = await _executor.GetAvailableBinariesAsync(minTimePercentage, minTime, topCount);
            
            if (result.Success)
            {
                // Generate suggested usage based on the parameters and results
                string? suggestedUsage = null;
                if (result.Binaries.Length > 50)
                {
                    suggestedUsage = "Large result set detected. Consider using minTimePercentage or minTimeMs parameters to focus on binaries with significant time usage, or topCount to limit results.";
                }
                else if (!minTimePercentage.HasValue && !minTimeMs.HasValue && !topCount.HasValue)
                {
                    suggestedUsage = "For more focused analysis, consider using minTimePercentage (e.g., 0.05 for >=0.05%) or minTimeMs (e.g., 500 for >=500ms) to filter out low-impact binaries, or topCount to show only the most time-consuming binaries.";
                }

                var successResult = new
                {
                    Action = "GetAvailableBinaries",
                    Status = "Success",
                    MinTimePercentage = Math.Round(minTimePercentage ?? 0, 2),
                    MinTimeMs = minTimeMs,
                    TopCount = topCount,
                    Description = GetBinaryFilterDescription(minTimePercentage, minTimeMs, topCount, result.Binaries.Length),
                    Instruction = "These are the binaries/DLLs containing functions in the currently loaded process. This shows aggregated time consumption per binary. Use GetAvailableFunctions(moduleName: '<binary_name>') to get functions from a specific binary.",
                    SuggestedUsage = suggestedUsage,
                    TotalBinaryCount = result.Binaries.Length,
                    FilteredBinaryCount = result.Binaries.Length,
                    Binaries = result.Binaries.Select(b => new {
                        Name = b.Name,
                        FullPath = b.FullPath,
                        TimePercentage = Math.Round(b.TimePercentage, 2),
                        Time = b.Time.ToString()
                    }).ToArray(),
                    Timestamp = DateTime.UtcNow
                };
                return System.Text.Json.JsonSerializer.Serialize(successResult, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            }
            else
            {
                var failureResult = new
                {
                    Action = "GetAvailableBinaries",
                    Status = "Failed",
                    Description = result.ErrorMessage ?? "Failed to retrieve binaries from currently loaded profile",
                    Timestamp = DateTime.UtcNow
                };
                return System.Text.Json.JsonSerializer.Serialize(failureResult, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            }
        }
        catch (Exception ex)
        {
            var errorResult = new
            {
                Action = "GetAvailableBinaries",
                Status = "Error",
                Error = ex.Message,
                Timestamp = DateTime.UtcNow
            };

            return System.Text.Json.JsonSerializer.Serialize(errorResult, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }
    }

    private static string GetBinaryFilterDescription(double? minTimePercentage, double? minTimeMs, int? topCount, int totalCount)
    {
        var filterParts = new List<string>();
        
        if (minTimePercentage.HasValue)
        {
            filterParts.Add($"time >= {minTimePercentage}%");
        }
        
        if (minTimeMs.HasValue)
        {
            filterParts.Add($"time >= {minTimeMs}ms");
        }
        
        if (topCount.HasValue && filterParts.Any())
        {
            return $"Successfully retrieved top {topCount} binaries (from {totalCount} total) with {string.Join(" and ", filterParts)}, sorted by time percentage";
        }
        else if (filterParts.Any())
        {
            return $"Successfully retrieved {totalCount} binaries with {string.Join(" and ", filterParts)}, sorted by time percentage";
        }
        else if (topCount.HasValue)
        {
            return $"Successfully retrieved top {Math.Min(topCount.Value, totalCount)} most time-consuming binaries (from {totalCount} total), sorted by time percentage";
        }
        else
        {
            return $"Successfully retrieved {totalCount} binaries from currently loaded profile, sorted by time percentage";
        }
    }

    #endregion

    #region Function Assembly Tool

    [McpServerTool, Description("Get assembly code for a specific function by double-clicking on it in the Summary pane, and save it to a file for later contextual reference by Copilot")]
    public static async Task<string> GetFunctionAssembly(string functionName)
    {
        if (string.IsNullOrWhiteSpace(functionName))
            throw new ArgumentException("Function name cannot be empty", nameof(functionName));

        try
        {
            if (_executor == null)
            {
                throw new InvalidOperationException("MCP action executor is not initialized");
            }

            string? filePath = await _executor.GetFunctionAssemblyToFileAsync(functionName);

            if (string.IsNullOrEmpty(filePath))
            {
                var notFoundResult = new
                {
                    Action = "GetFunctionAssembly",
                    FunctionName = functionName,
                    Status = "NotFound",
                    Description = "Function not found in the current summary or assembly could not be retrieved",
                    Timestamp = DateTime.UtcNow
                };
                return System.Text.Json.JsonSerializer.Serialize(notFoundResult, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            }

            // Read the assembly content from the file to include in the response
            string assemblyContent = "";
            try
            {
                if (System.IO.File.Exists(filePath))
                {
                    assemblyContent = await System.IO.File.ReadAllTextAsync(filePath);
                }
            }
            catch
            {
                // If we can't read the file, just return the path
            }

            var result = new
            {
                Action = "GetFunctionAssembly",
                FunctionName = functionName,
                Status = "Success",
                FilePath = filePath,
                Assembly = assemblyContent,
                Description = $"Successfully retrieved assembly code for the function and saved to {filePath}",
                Timestamp = DateTime.UtcNow
            };

            return System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            var errorResult = new
            {
                Action = "GetFunctionAssembly",
                FunctionName = functionName,
                Status = "Error",
                Error = ex.Message,
                Timestamp = DateTime.UtcNow
            };

            return System.Text.Json.JsonSerializer.Serialize(errorResult, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }
    }

    #endregion

    #region Help Tool

    [McpServerTool, Description("Get help information about available MCP commands")]
    public static string GetHelp()
    {
        var helpInfo = new
        {
            ServerName = "Profile Explorer MCP Server",
            Version = "1.0.0",
            Description = "Simplified MCP server for Profile Explorer - opens and loads traces and retrieves function assembly",
            QuickStartPatterns = new[]
            {
                "1. To analyze functions from a specific module: GetAvailableFunctions(moduleName: 'ntdll.dll')",
                "2. To find hotspots in a specific module: GetAvailableFunctions(moduleName: 'kernel32.dll', topCount: 10)",
                "3. To analyze CPU-intensive functions: GetAvailableFunctions(minSelfTimePercentage: 0.1)",
                "4. Common mistake: Do NOT call GetAvailableFunctions() without moduleName when you want module-specific results!"
            },
            AvailableCommands = new[]
            {
                new { 
                    Name = "GetAvailableProcesses", 
                    Description = "Get the list of available processes from a trace file with optional weight filtering and top N limiting", 
                    Parameters = "profileFilePath (string) - Path to the ETL trace file to analyze for available processes, minWeightPercentage (double, optional) - Minimum weight percentage to filter processes (e.g., 1.0 for processes with >= 1% weight), topCount (int, optional) - Limit results to top N heaviest processes (e.g., 10 for top 10 heaviest processes)"
                },
                new { 
                    Name = "OpenTrace", 
                    Description = "Open and load a trace file with a specific process by name or ID. For ambiguous queries, uses LLM world knowledge to help identify the correct process", 
                    Parameters = "profileFilePath (string) - Path to the ETL trace file to open, processNameOrId (string) - Process name (e.g., 'chrome.exe', 'POWERPNT'), category (e.g., 'defender', 'performance recorder'), or process ID (e.g., '1234')"
                },
                new { 
                    Name = "GetAvailableFunctions", 
                    Description = "Get the list of available functions from the currently loaded process/trace. This should be called after a process is loaded via OpenTrace. Returns both self time (CPU-intensive functions) and total time (high-impact functions including callees). Supports filtering by module name, performance thresholds, result limits, and sorting preferences for targeted performance analysis.", 
                    Parameters = "moduleName (string, optional) - **CRITICAL: Filter functions by specific module/DLL name (e.g., 'ntdll.dll', 'kernel32.dll', 'ntdll' - supports partial matching). USE THIS FIRST when you want functions from a specific module!**, minSelfTimePercentage (double, optional) - Minimum self time percentage to filter functions (e.g., 0.1 for functions with >= 0.1% self time; use for finding CPU-intensive functions), minTotalTimePercentage (double, optional) - Minimum total time percentage to filter functions (e.g., 0.5 for functions with >= 0.5% total time; use for finding functions with high overall impact), topCount (int, optional) - Limit results to top N heaviest functions (e.g., 10 for top 10 functions; useful for focusing on worst performers), sortBySelfTime (bool, optional, default true) - Sort by self time (true, shows functions doing actual work) or total time (false, shows functions with highest overall impact including called functions)"
                },
                new { 
                    Name = "GetAvailableBinaries", 
                    Description = "Get the list of available binaries/DLLs from the currently loaded process/trace. This should be called after a process is loaded via OpenTrace. Returns aggregated performance data for each binary that contains functions with performance data.", 
                    Parameters = "minTimePercentage (double, optional) - Minimum time percentage threshold to filter binaries (e.g., 0.01 for binaries contributing >=0.01% time), topCount (int, optional) - Limit results to top N binaries by performance (e.g., 10 for top 10 most time-consuming binaries)"
                },
                new { 
                    Name = "GetFunctionAssembly", 
                    Description = "Get assembly code for a specific function by double-clicking on it in the Summary pane, and save it to a file for later contextual reference by Copilot",
                    Parameters = "functionName (string) - Name of the function to retrieve assembly for (supports partial matching)"
                },
                new { 
                    Name = "GetHelp", 
                    Description = "Get help information about available MCP commands", 
                    Parameters = "none" 
                }
            },
            Examples = new object[]
            {
                new
                {
                    Description = "Get list of all available processes in a trace file",
                    Command = "GetAvailableProcesses",
                    Parameters = new {
                        profileFilePath = @"C:\traces\sample.etl"
                    }
                },
                new
                {
                    Description = "Get list of processes with significant CPU usage (>= 1%)",
                    Command = "GetAvailableProcesses",
                    Parameters = new {
                        profileFilePath = @"C:\traces\sample.etl",
                        minWeightPercentage = 1.0
                    }
                },
                new
                {
                    Description = "Get list of processes with very high CPU usage (>= 5%)",
                    Command = "GetAvailableProcesses",
                    Parameters = new {
                        profileFilePath = @"C:\traces\sample.etl",
                        minWeightPercentage = 5.0
                    }
                },
                new
                {
                    Description = "Get top 10 heaviest processes by CPU usage",
                    Command = "GetAvailableProcesses",
                    Parameters = new {
                        profileFilePath = @"C:\traces\sample.etl",
                        topCount = 10
                    }
                },
                new
                {
                    Description = "Get top 5 heaviest processes with at least 1% CPU usage",
                    Command = "GetAvailableProcesses",
                    Parameters = new {
                        profileFilePath = @"C:\traces\sample.etl",
                        minWeightPercentage = 1.0,
                        topCount = 5
                    }
                },
                new
                {
                    Description = "Load a trace file and select a specific process by ID",
                    Command = "OpenTrace",
                    Parameters = new {
                        profileFilePath = @"C:\traces\sample.etl",
                        processNameOrId = "1234"
                    }
                },
                new
                {
                    Description = "Load a trace file with an exact process name",
                    Command = "OpenTrace",
                    Parameters = new {
                        profileFilePath = @"C:\traces\sample.etl",
                        processNameOrId = "POWERPNT"
                    }
                },
                new
                {
                    Description = "Load a trace file with an ambiguous query (will trigger LLM analysis)",
                    Command = "OpenTrace",
                    Parameters = new {
                        profileFilePath = @"C:\traces\sample.etl",
                        processNameOrId = "defender"
                    }
                },
                new
                {
                    Description = "Load a trace file using semantic description (will trigger LLM analysis)",
                    Command = "OpenTrace",
                    Parameters = new {
                        profileFilePath = @"C:\traces\sample.etl",
                        processNameOrId = "performance recorder"
                    }
                },
                new
                {
                    Description = "Get functions from ntdll.dll only",
                    Command = "GetAvailableFunctions",
                    Parameters = new {
                        moduleName = "ntdll.dll"
                    }
                },
                new
                {
                    Description = "Get functions from kernel32.dll only",
                    Command = "GetAvailableFunctions",
                    Parameters = new {
                        moduleName = "kernel32.dll"
                    }
                },
                new
                {
                    Description = "Get list of all available functions in the currently loaded profile",
                    Command = "GetAvailableFunctions",
                    Parameters = new { }
                },
                new
                {
                    Description = "Get list of functions with significant self time (>= 0.1%)",
                    Command = "GetAvailableFunctions",
                    Parameters = new {
                        minSelfTimePercentage = 0.1
                    }
                },
                new
                {
                    Description = "Get list of functions with high total time impact (>= 0.5%)",
                    Command = "GetAvailableFunctions",
                    Parameters = new {
                        minTotalTimePercentage = 0.5
                    }
                },
                new
                {
                    Description = "Get top 5 CPU-intensive functions (by self time)",
                    Command = "GetAvailableFunctions",
                    Parameters = new {
                        topCount = 5,
                        sortBySelfTime = true
                    }
                },
                new
                {
                    Description = "Get top 5 highest-impact functions (by total time)",
                    Command = "GetAvailableFunctions",
                    Parameters = new {
                        topCount = 5,
                        sortBySelfTime = false
                    }
                },
                new
                {
                    Description = "Find performance bottlenecks: functions with >= 0.2% self time AND >= 1% total time",
                    Command = "GetAvailableFunctions",
                    Parameters = new {
                        minSelfTimePercentage = 0.2,
                        minTotalTimePercentage = 1.0
                    }
                },
                new
                {
                    Description = "Get top 3 hotspots in ntdll.dll (combined module and count filtering)",
                    Command = "GetAvailableFunctions",
                    Parameters = new {
                        moduleName = "ntdll.dll",
                        topCount = 3,
                        sortBySelfTime = true
                    }
                },
                new
                {
                    Description = "Get top 5 functions from kernel32.dll by self time",
                    Command = "GetAvailableFunctions",
                    Parameters = new {
                        moduleName = "kernel32.dll",
                        topCount = 5,
                        sortBySelfTime = true
                    }
                },
                new
                {
                    Description = "Get list of all available binaries/DLLs in the currently loaded profile",
                    Command = "GetAvailableBinaries",
                    Parameters = new { }
                },
                new
                {
                    Description = "Get list of binaries contributing at least 0.05% time",
                    Command = "GetAvailableBinaries",
                    Parameters = new {
                        minTimePercentage = 0.05
                    }
                },
                new
                {
                    Description = "Get top 10 most time-consuming binaries",
                    Command = "GetAvailableBinaries",
                    Parameters = new {
                        topCount = 10
                    }
                },
                new
                {
                    Description = "Get top 5 binaries with significant time impact (combined filtering)",
                    Command = "GetAvailableBinaries",
                    Parameters = new {
                        minTimePercentage = 0.1,
                        topCount = 5
                    }
                },
                new
                {
                    Description = "Get assembly code for a function and save to file",
                    Command = "GetFunctionAssembly",
                    Parameters = new {
                        functionName = "main"
                    }
                }
            },
            Timestamp = DateTime.UtcNow
        };

        return System.Text.Json.JsonSerializer.Serialize(helpInfo, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }

    #endregion
}

