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

        #region UI Entry Point Tools (Based on mcp-ui-entrypoints.md)

        [McpServerTool, Description("Open the Profiling menu and Load Profile dialog in the main window")]
        public static async Task<string> OpenLoadProfileDialog()
        {
            try
            {
                if (_executor == null)
                {
                    throw new InvalidOperationException("MCP action executor is not initialized");
                }

                bool success = await _executor.OpenLoadProfileDialogAsync();
                
                var result = new
                {
                    Action = "OpenLoadProfileDialog",
                    Status = success ? "Success" : "Failed",
                    Description = "Executed AppCommand.LoadProfile to open the Load Profile dialog",
                    Timestamp = DateTime.UtcNow
                };

                return System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            }
            catch (Exception ex)
            {
                var errorResult = new
                {
                    Action = "OpenLoadProfileDialog",
                    Status = "Error",
                    Error = ex.Message,
                    Timestamp = DateTime.UtcNow
                };

                return System.Text.Json.JsonSerializer.Serialize(errorResult, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            }
        }

        [McpServerTool, Description("Set the profile file path in the Load Profile dialog")]
        public static async Task<string> SetProfileFilePath(string profileFilePath)
        {
            if (string.IsNullOrWhiteSpace(profileFilePath))
                throw new ArgumentException("Profile file path cannot be empty", nameof(profileFilePath));

            try
            {
                if (_executor == null)
                {
                    throw new InvalidOperationException("MCP action executor is not initialized");
                }

                bool success = await _executor.SetProfileFilePathAsync(profileFilePath);
                
                var result = new
                {
                    Action = "SetProfileFilePath",
                    ProfileFilePath = profileFilePath,
                    Status = success ? "Success" : "Failed",
                    Description = "Set ProfileFilePath property in ProfileLoadWindow, triggering process enumeration",
                    Timestamp = DateTime.UtcNow
                };

                return System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            }
            catch (Exception ex)
            {
                var errorResult = new
                {
                    Action = "SetProfileFilePath",
                    ProfileFilePath = profileFilePath,
                    Status = "Error",
                    Error = ex.Message,
                    Timestamp = DateTime.UtcNow
                };

                return System.Text.Json.JsonSerializer.Serialize(errorResult, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            }
        }

        [McpServerTool, Description("Select process(es) in the process list for profile loading")]
        public static async Task<string> SelectProcesses(int[] processIds)
        {
            if (processIds == null || processIds.Length == 0)
                throw new ArgumentException("Process IDs array cannot be null or empty", nameof(processIds));

            try
            {
                if (_executor == null)
                {
                    throw new InvalidOperationException("MCP action executor is not initialized");
                }

                bool success = await _executor.SelectProcessesAsync(processIds);
                
                var result = new
                {
                    Action = "SelectProcesses",
                    ProcessIds = processIds,
                    Status = success ? "Success" : "Failed",
                    Description = "Selected processes in ProcessList, updating selectedProcSummary_",
                    Timestamp = DateTime.UtcNow
                };

                return System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            }
            catch (Exception ex)
            {
                var errorResult = new
                {
                    Action = "SelectProcesses",
                    ProcessIds = processIds,
                    Status = "Error",
                    Error = ex.Message,
                    Timestamp = DateTime.UtcNow
                };

                return System.Text.Json.JsonSerializer.Serialize(errorResult, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            }
        }

        [McpServerTool, Description("Execute the profile load operation (either via UI LoadButton_Click or backend LoadProfileData)")]
        public static async Task<string> ExecuteProfileLoad(bool useBackendDirectly = true)
        {
            try
            {
                if (_executor == null)
                {
                    throw new InvalidOperationException("MCP action executor is not initialized");
                }

                bool success = await _executor.ExecuteProfileLoadAsync(useBackendDirectly);
                
                var result = new
                {
                    Action = "ExecuteProfileLoad",
                    UseBackendDirectly = useBackendDirectly,
                    Status = success ? "Success" : "Failed",
                    Description = useBackendDirectly 
                        ? "Executed MainWindow.LoadProfileData() directly for clean backend load"
                        : "Executed LoadButton_Click() to simulate UI interaction",
                    Timestamp = DateTime.UtcNow
                };

                return System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            }
            catch (Exception ex)
            {
                var errorResult = new
                {
                    Action = "ExecuteProfileLoad",
                    UseBackendDirectly = useBackendDirectly,
                    Status = "Error",
                    Error = ex.Message,
                    Timestamp = DateTime.UtcNow
                };

                return System.Text.Json.JsonSerializer.Serialize(errorResult, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            }
        }

        #endregion

        #region Information and Status Tools

        [McpServerTool, Description("Get the current status of the Profile Explorer UI")]
        public static async Task<string> GetProfilerStatus()
        {
            try
            {
                if (_executor == null)
                {
                    throw new InvalidOperationException("MCP action executor is not initialized");
                }

                ProfilerStatus status = await _executor.GetStatusAsync();
                
                var result = new
                {
                    Action = "GetProfilerStatus",
                    Status = "Success",
                    IsProfileLoaded = status.IsProfileLoaded,
                    CurrentProfilePath = status.CurrentProfilePath,
                    LoadedProcesses = status.LoadedProcesses,
                    ActiveFilters = status.ActiveFilters,
                    LastUpdated = status.LastUpdated,
                    Timestamp = DateTime.UtcNow
                };

                return System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            }
            catch (Exception ex)
            {
                var errorResult = new
                {
                    Action = "GetProfilerStatus",
                    Status = "Error",
                    Error = ex.Message,
                    IsProfileLoaded = false,
                    CurrentProfilePath = (string?)null,
                    LoadedProcesses = new int[0],
                    ActiveFilters = new string[0],
                    Timestamp = DateTime.UtcNow
                };

                return System.Text.Json.JsonSerializer.Serialize(errorResult, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            }
        }

        [McpServerTool, Description("Get help information about available MCP commands")]
        public static string GetHelp()
        {
            var helpInfo = new
            {
                ServerName = "Profile Explorer MCP Server",
                Version = "1.0.0",
                Description = "MCP server for Profile Explorer UI automation based on documented entry points",
                AvailableCommands = new[]
                {
                    new { Name = "OpenLoadProfileDialog", Description = "Open the Profiling menu and Load Profile dialog", Parameters = "none" },
                    new { Name = "SetProfileFilePath", Description = "Set the profile file path in the Load Profile dialog", Parameters = "profileFilePath (string)" },
                    new { Name = "SelectProcesses", Description = "Select process(es) in the process list for profile loading", Parameters = "processIds (int[])" },
                    new { Name = "ExecuteProfileLoad", Description = "Execute the profile load operation", Parameters = "useBackendDirectly (bool, default: true)" },
                    new { Name = "GetProfilerStatus", Description = "Get the current status of the Profile Explorer UI", Parameters = "none" },
                    new { Name = "GetHelp", Description = "Get help information about available MCP commands", Parameters = "none" }
                },
                WorkflowExample = new
                {
                    Description = "Typical workflow to load a profile",
                    Steps = new[]
                    {
                        "1. OpenLoadProfileDialog() - Opens the Load Profile dialog",
                        "2. SetProfileFilePath(path) - Sets the trace file path, enumerates processes",
                        "3. SelectProcesses([pid1, pid2]) - Selects which processes to load",
                        "4. ExecuteProfileLoad(true) - Loads the profile (backend) or ExecuteProfileLoad(false) - Loads via UI"
                    }
                },
                Documentation = "Based on mcp-ui-entrypoints.md - each command corresponds to a documented UI entry point",
                Timestamp = DateTime.UtcNow
            };

            return System.Text.Json.JsonSerializer.Serialize(helpInfo, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }

        #endregion
    }
}
