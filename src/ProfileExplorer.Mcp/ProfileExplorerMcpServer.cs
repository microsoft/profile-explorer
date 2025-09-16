using System;
using System.ComponentModel;
using System.IO;
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

            OpenTraceResult result = await _executor.OpenTraceAsync(profileFilePath, processNameOrId);
            
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
                    AvailableProcesses = result.AvailableProcesses ?? Array.Empty<string>(),
                    Timestamp = DateTime.UtcNow
                };
                return System.Text.Json.JsonSerializer.Serialize(failureResult, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            }
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

            string filePath = await _executor.GetFunctionAssemblyToFileAsync(functionName);
            
            if (filePath == null)
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
                    Name = "OpenTrace", 
                    Description = "Open and load a trace file with a specific process by name or ID in one complete operation", 
                    Parameters = "profileFilePath (string) - Path to the ETL trace file to open, processNameOrId (string) - Process name (e.g., 'chrome.exe', 'POWERPNT') or process ID (e.g., '1234') to select and load from the trace"
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
                    Description = "Load a trace file and select a specific process by ID",
                    Command = "OpenTrace",
                    Parameters = new {
                        profileFilePath = @"C:\traces\sample.etl",
                        processNameOrId = "1234"
                    }
                },
                new
                {
                    Description = "Load a trace file and select a specific process by partial name",
                    Command = "OpenTrace",
                    Parameters = new {
                        profileFilePath = @"C:\traces\sample.etl",
                        processNameOrId = "POWERPNT"
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

