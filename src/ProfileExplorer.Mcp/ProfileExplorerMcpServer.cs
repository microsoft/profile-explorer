using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace ProfileExplorer.Mcp
{
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

        [McpServerTool, Description("Open and load a trace file with a specific process in one complete operation")]
        public static async Task<string> OpenTrace(string profileFilePath, int processId)
        {
            if (string.IsNullOrWhiteSpace(profileFilePath))
                throw new ArgumentException("Profile file path cannot be empty", nameof(profileFilePath));

            if (processId <= 0)
                throw new ArgumentException("Process ID must be a positive integer", nameof(processId));

            try
            {
                if (_executor == null)
                {
                    throw new InvalidOperationException("MCP action executor is not initialized");
                }

                bool success = await _executor.OpenTraceAsync(profileFilePath, processId);
                
                var result = new
                {
                    Action = "OpenTrace",
                    ProfileFilePath = profileFilePath,
                    ProcessId = processId,
                    Status = success ? "Success" : "Failed",
                    Description = "Opened Profile Explorer, loaded trace file, selected process, and executed profile load",
                    Timestamp = DateTime.UtcNow
                };

                return System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            }
            catch (Exception ex)
            {
                var errorResult = new
                {
                    Action = "OpenTrace",
                    ProfileFilePath = profileFilePath,
                    ProcessId = processId,
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
                Description = "Simplified MCP server for Profile Explorer - opens and loads traces in one operation",
                AvailableCommands = new[]
                {
                    new { 
                        Name = "OpenTrace", 
                        Description = "Open and load a trace file with a specific process in one complete operation", 
                        Parameters = "profileFilePath (string) - Path to the ETL trace file to open, processId (int) - Process ID to select and load from the trace"
                    },
                    new { 
                        Name = "GetHelp", 
                        Description = "Get help information about available MCP commands", 
                        Parameters = "none" 
                    }
                },
                Example = new
                {
                    Description = "Load a trace file and select a specific process",
                    Command = "OpenTrace",
                    Parameters = new {
                        profileFilePath = @"C:\traces\sample.etl",
                        processId = 1234
                    }
                },
                Timestamp = DateTime.UtcNow
            };

            return System.Text.Json.JsonSerializer.Serialize(helpInfo, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }

        #endregion
    }
}
