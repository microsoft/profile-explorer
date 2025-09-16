using System.Threading.Tasks;

namespace ProfileExplorer.Mcp
{
    /// <summary>
    /// Interface for executing MCP actions against the Profile Explorer UI.
    /// This will be implemented by the UI project to provide the actual integration.
    /// Based on the 4 documented UI entry points in mcp-ui-entrypoints.md
    /// </summary>
    public interface IMcpActionExecutor
    {
        /// <summary>
        /// Opens a trace file and loads the specified process in one complete operation
        /// This combines all the UI entry points into a single streamlined action
        /// </summary>
        /// <param name="profileFilePath">Path to the ETL trace file to open</param>
        /// <param name="processIdentifier">Process ID (e.g., "1234") or process name (e.g., "chrome.exe", "POWERPNT") to select and load from the trace</param>
        /// <returns>Task that completes when the trace is fully loaded</returns>
        Task<bool> OpenTraceAsync(string profileFilePath, string processIdentifier);

        /// <summary>
        /// Get the current status of the Profile Explorer UI
        /// </summary>
        /// <returns>Status information about the current state</returns>
        Task<ProfilerStatus> GetStatusAsync();

        /// <summary>
        /// Gets a function's assembly and saves it to a file in the tmp directory
        /// </summary>
        /// <param name="functionName">Name of the function to retrieve assembly for</param>
        /// <returns>Task that completes with the file path where assembly was saved, or null if function not found</returns>
        Task<string> GetFunctionAssemblyToFileAsync(string functionName);
    }

    /// <summary>
    /// Status information about the Profile Explorer UI
    /// </summary>
    public class ProfilerStatus
    {
        public bool IsProfileLoaded { get; set; }
        public string? CurrentProfilePath { get; set; }
        public int[] LoadedProcesses { get; set; } = Array.Empty<int>();
        public string[] ActiveFilters { get; set; } = Array.Empty<string>();
        public string? CurrentProcessName { get; set; }
        public int? CurrentProcessId { get; set; }
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    }
}
