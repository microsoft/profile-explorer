using System.Threading.Tasks;

namespace ProfileExplorer.Mcp;

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
    /// <returns>Task that completes when the trace is fully loaded, with detailed result information</returns>
    Task<OpenTraceResult> OpenTraceAsync(string profileFilePath, string processIdentifier);

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
    Task<string?> GetFunctionAssemblyToFileAsync(string functionName);

    /// <summary>
    /// Gets the list of available processes from a trace file without opening it
    /// </summary>
    /// <param name="profileFilePath">Path to the ETL trace file to analyze</param>
    /// <param name="minWeightPercentage">Optional minimum weight percentage to filter processes (e.g., 1.0 for processes with >= 1% weight)</param>
    /// <returns>Task that completes with the list of available processes in the trace file</returns>
    Task<GetAvailableProcessesResult> GetAvailableProcessesAsync(string profileFilePath, double? minWeightPercentage = null);
}

/// <summary>
/// Result of an OpenTrace operation
/// </summary>
public class OpenTraceResult
{
    public bool Success { get; set; }
    public OpenTraceFailureReason FailureReason { get; set; } = OpenTraceFailureReason.None;
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Specific reasons why an OpenTrace operation might fail
/// </summary>
public enum OpenTraceFailureReason
{
    None,
    FileNotFound,
    ProcessNotFound,
    TraceLoadTimeout,
    ProcessListLoadTimeout,
    UIError,
    UnknownError
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
    public ProcessInfo? CurrentProcess { get; set; }
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Information about a process available in a trace file
/// </summary>
public class ProcessInfo
{
    public int ProcessId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ImageFileName { get; set; }
    public string? CommandLine { get; set; }
    public TimeSpan Weight { get; set; }
    public double WeightPercentage { get; set; }
    public TimeSpan Duration { get; set; }
}

/// <summary>
/// Result of a GetAvailableProcesses operation
/// </summary>
public class GetAvailableProcessesResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public ProcessInfo[] Processes { get; set; } = Array.Empty<ProcessInfo>();
}

