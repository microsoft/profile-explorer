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
/// MCP tools for Profile Explorer UI automation and analysis
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

    [McpServerTool, Description("Get the list of available processes from a trace file")]
    public static async Task<string> GetAvailableProcesses(string profileFilePath)
    {
        if (string.IsNullOrWhiteSpace(profileFilePath))
            throw new ArgumentException("Profile file path cannot be empty", nameof(profileFilePath));

        try
        {
            if (_executor == null)
            {
                throw new InvalidOperationException("MCP action executor is not initialized");
            }

            GetAvailableProcessesResult result = await _executor.GetAvailableProcessesAsync(profileFilePath);
            
            if (result.Success)
            {
                var successResult = new
                {
                    Action = "GetAvailableProcesses",
                    ProfileFilePath = profileFilePath,
                    Status = "Success",
                    ProcessCount = result.Processes.Length,
                    Processes = result.Processes.Select(p => new
                    {
                        ProcessId = p.ProcessId,
                        Name = p.Name,
                        ImageFileName = p.ImageFileName,
                        CommandLine = p.CommandLine
                    }).ToArray(),
                    Description = $"Successfully retrieved {result.Processes.Length} processes from trace file",
                    Instruction = "If the user requests to open a process using an ambiguous term (like 'defender', 'office', 'browser', etc.) that could match multiple processes from this list, you MUST ask the user to clarify which specific process they want instead of choosing one yourself. Present the matching options and let the user decide. Only proceed with OpenTrace if the user has explicitly specified an exact process name or ID, or if there is only one clear match.",
                    Timestamp = DateTime.UtcNow
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

            var result = new
            {
                Action = "GetFunctionAssembly",
                FunctionName = functionName,
                Status = "Success",
                FilePath = filePath,
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
            AvailableCommands = new[]
            {
                new { 
                    Name = "GetAvailableProcesses", 
                    Description = "Get the list of available processes from a trace file without opening it", 
                    Parameters = "profileFilePath (string) - Path to the ETL trace file to analyze for available processes"
                },
                new { 
                    Name = "OpenTrace", 
                    Description = "Open and load a trace file with a specific process by name or ID. For ambiguous queries, uses LLM world knowledge to help identify the correct process", 
                    Parameters = "profileFilePath (string) - Path to the ETL trace file to open, processNameOrId (string) - Process name (e.g., 'chrome.exe', 'POWERPNT'), category (e.g., 'defender', 'performance recorder'), or process ID (e.g., '1234')"
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
                    Description = "Get list of available processes in a trace file",
                    Command = "GetAvailableProcesses",
                    Parameters = new {
                        profileFilePath = @"C:\traces\sample.etl"
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

