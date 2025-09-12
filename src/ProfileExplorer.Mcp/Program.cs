using System;
using System.Threading.Tasks;
using ProfileExplorer.Mcp;

namespace ProfileExplorer.Mcp
{
    /// <summary>
    /// Standalone entry point for the Profile Explorer MCP Server
    /// This can be used for testing or running the server independently
    /// </summary>
    public class Program
    {
        public static async Task Main(string[] args)
        {
            Console.WriteLine("Starting Profile Explorer MCP Server...");
            
            try
            {
                // Start the server with a mock executor for testing
                await McpServerConfiguration.StartServerWithMockExecutorAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error starting MCP server: {ex.Message}");
                Environment.Exit(1);
            }
        }
    }

    /// <summary>
    /// Mock implementation of IMcpActionExecutor for testing purposes
    /// Implements the 4 documented UI entry points for testing
    /// </summary>
    public class MockMcpActionExecutor : IMcpActionExecutor {
        public Task<bool> OpenTraceAsync(string profileFilePath, int processId) {
            Console.WriteLine("Mock: OpenTraceAsync called");
            return Task.FromResult(true);
        }

        public Task<ProfilerStatus> GetStatusAsync()
        {
            Console.WriteLine("Mock: GetStatus called");
            return Task.FromResult(new ProfilerStatus
            {
                IsProfileLoaded = true,
                CurrentProfilePath = "/mock/path/to/profile.etl",
                LoadedProcesses = new[] { 1234, 5678 },
                ActiveFilters = new[] { "function:main", "module:test" },
                LastUpdated = DateTime.UtcNow
            });
        }
    }
}
