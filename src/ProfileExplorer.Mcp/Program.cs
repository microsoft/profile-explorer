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
    public class MockMcpActionExecutor : IMcpActionExecutor
    {
        public Task<bool> OpenLoadProfileDialogAsync()
        {
            Console.WriteLine("Mock: OpenLoadProfileDialog called - would execute AppCommand.LoadProfile");
            return Task.FromResult(true);
        }

        public Task<bool> SetProfileFilePathAsync(string profileFilePath)
        {
            Console.WriteLine($"Mock: SetProfileFilePath called with path='{profileFilePath}' - would set ProfileLoadWindow.ProfileFilePath and enumerate processes");
            return Task.FromResult(true);
        }

        public Task<bool> SelectProcessesAsync(int[] processIds)
        {
            Console.WriteLine($"Mock: SelectProcesses called with processIds=[{string.Join(", ", processIds)}] - would select items in ProcessList");
            return Task.FromResult(true);
        }

        public Task<bool> ExecuteProfileLoadAsync(bool useBackendDirectly = true)
        {
            if (useBackendDirectly)
            {
                Console.WriteLine("Mock: ExecuteProfileLoad called with backend=true - would call MainWindow.LoadProfileData directly");
            }
            else
            {
                Console.WriteLine("Mock: ExecuteProfileLoad called with backend=false - would simulate LoadButton_Click");
            }
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
