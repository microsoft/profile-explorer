using System.Threading.Tasks;

namespace ProfileExplorer.Mcp;

/// <summary>
/// Interface for executing MCP actions against the Profile Explorer UI.
/// This will be implemented by the UI project to provide the actual integration.
/// Provides methods for opening traces, analyzing processes and functions, and retrieving assembly code.
/// </summary>
public interface IMcpActionExecutor
{
    /// <summary>
    /// Opens a trace file and loads the specified process in one complete operation
    /// This provides a simplified interface that handles the entire trace loading workflow
    /// </summary>
    /// <param name="profileFilePath">Path to the ETL trace file to open</param>
    /// <param name="processIdentifier">Process ID (e.g., "12345") or process name (e.g., "chrome.exe", "POWERPNT") to select and load from the trace</param>
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
    /// <param name="topCount">Optional number to limit results to the top N heaviest processes (e.g., 10 for top 10 heaviest processes)</param>
    /// <returns>Task that completes with the list of available processes in the trace file</returns>
    Task<GetAvailableProcessesResult> GetAvailableProcessesAsync(string profileFilePath, double? minWeightPercentage = null, int? topCount = null);

    /// <summary>
    /// Gets the list of available functions from the currently loaded process/trace
    /// This should be called after a process is loaded via OpenTraceAsync
    /// </summary>
    /// <param name="filter">Optional filter settings for functions - specify moduleName, performance thresholds, result limits, and sorting preferences</param>
    /// <returns>Task that completes with the list of available functions in the currently loaded process</returns>
    Task<GetAvailableFunctionsResult> GetAvailableFunctionsAsync(FunctionFilter? filter = null);

    /// <summary>
    /// Gets the list of available binaries/DLLs from the currently loaded process/trace
    /// Returns all binaries that contain functions with performance data, with aggregated performance metrics
    /// This should be called after a process is loaded via OpenTraceAsync
    /// </summary>
    /// <param name="minTimePercentage">Optional minimum time percentage to filter binaries (e.g., 0.01 for binaries with >= 0.01% time)</param>
    /// <param name="minTime">Optional minimum absolute time to filter binaries (e.g., "00:00:00.500" for binaries with >= 500ms runtime)</param>
    /// <param name="topCount">Optional number to limit results to the top N binaries by performance (e.g., 10 for top 10 most time-consuming binaries)</param>
    /// <returns>Task that completes with the list of available binaries in the currently loaded process</returns>
    Task<GetAvailableBinariesResult> GetAvailableBinariesAsync(double? minTimePercentage = null, TimeSpan? minTime = null, int? topCount = null);
}

/// <summary>
/// Filter options for GetAvailableFunctions operation
/// All properties are optional - specify only the filters you want to apply
/// </summary>
public class FunctionFilter
{
    /// <summary>
    /// Filter functions by specific module/DLL name (e.g., "ntdll.dll", "kernel32.dll", "ntdll")
    /// Supports partial matching - MOST COMMONLY USED FILTER
    /// </summary>
    public string? ModuleName { get; set; }
    
    /// <summary>
    /// Minimum self time percentage to filter functions (e.g., 1.0 for functions with >= 1% self time)
    /// Use for finding CPU-intensive functions doing actual work
    /// </summary>
    public double? MinSelfTimePercentage { get; set; }
    
    /// <summary>
    /// Minimum total time percentage to filter functions (e.g., 5.0 for functions with >= 5% total time)  
    /// Use for finding functions with high overall impact including callees
    /// </summary>
    public double? MinTotalTimePercentage { get; set; }
    
    /// <summary>
    /// Limit results to top N heaviest functions (e.g., 10 for top 10 functions)
    /// Useful for focusing on worst performers
    /// </summary>
    public int? TopCount { get; set; }
    
    /// <summary>
    /// Sort by self time (true, default) or total time (false)
    /// Self time shows functions doing actual work, total time shows overall impact
    /// </summary>
    public bool SortBySelfTime { get; set; } = true;
}

/// <summary>
/// Result of an OpenTrace operation
/// </summary>
public class OpenTraceResult
{
    public bool Success { get; set; }
    public OpenTraceFailureReason FailureReason { get; set; } = OpenTraceFailureReason.None;
    public string? ErrorMessage { get; set; }
    /// <summary>
    /// Indicates if the trace was already loaded (no action needed, but success is true)
    /// </summary>
    public bool AlreadyLoaded { get; set; }
    /// <summary>
    /// Optional message with additional context about the result
    /// </summary>
    public string? Message { get; set; }
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
    ProfileLoadTimeout,
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

/// <summary>
/// Information about a function available in the currently loaded process
/// </summary>
public class FunctionInfo
{
    /// <summary>
    /// Short function name (e.g., "ProcessData")
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Full function signature including namespace, class, and parameters
    /// (e.g., "MyNamespace::MyClass::ProcessData(int, char*)")
    /// </summary>
    public string? FullName { get; set; }
    
    /// <summary>
    /// Name of the module/binary containing this function (e.g., "MyApp.exe", "kernel32.dll")
    /// </summary>
    public string? ModuleName { get; set; }
    
    /// <summary>
    /// Self time percentage - time spent exclusively in this function's code (excluding called functions)
    /// Corresponds to "Time (self)" percentage in ProfileExplorer Summary window
    /// </summary>
    public double SelfTimePercentage { get; set; }
    
    /// <summary>
    /// Total time percentage - time spent in this function including all functions it calls
    /// Corresponds to "Time (total)" percentage in ProfileExplorer Summary window
    /// </summary>
    public double TotalTimePercentage { get; set; }
    
    /// <summary>
    /// Self time - time spent exclusively in this function's code (excluding called functions)
    /// Corresponds to "Time (self)" in ProfileExplorer Summary window
    /// </summary>
    public TimeSpan SelfTime { get; set; }
    
    /// <summary>
    /// Total time - time spent in this function including all functions it calls  
    /// Corresponds to "Time (total)" in ProfileExplorer Summary window
    /// </summary>
    public TimeSpan TotalTime { get; set; }
    
    /// <summary>
    /// Path to the source file containing this function, if available from debug information
    /// </summary>
    public string? SourceFile { get; set; }
    
    /// <summary>
    /// Whether assembly/disassembly code is available for this function
    /// </summary>
    public bool HasAssembly { get; set; }
}

/// <summary>
/// Result of a GetAvailableFunctions operation
/// </summary>
public class GetAvailableFunctionsResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public FunctionInfo[] Functions { get; set; } = Array.Empty<FunctionInfo>();
}

/// <summary>
/// Information about a binary/DLL available in the currently loaded process
/// Represents aggregated performance data for all functions within each binary
/// </summary>
public class BinaryInfo
{
    /// <summary>
    /// Name of the binary/DLL (e.g., "kernel32.dll", "ntdll.dll", "MyApp.exe")
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Full path to the binary file, if available
    /// </summary>
    public string? FullPath { get; set; }
    
    /// <summary>
    /// Time percentage for this binary (typically self time percentage)
    /// Represents how much CPU time this binary consumes
    /// </summary>
    public double TimePercentage { get; set; }
    
    /// <summary>
    /// Time value for this binary
    /// Represents the actual time consumed by this binary
    /// </summary>
    public TimeSpan Time { get; set; }

    /// <summary
    /// 
    /// 
    /// / </summary>
    public bool BinaryFileMissing { get; set; }
    /// <summary>
    /// 
    /// 
    /// </summary>
    public bool DebugFileMissing { get; set; }
}

/// <summary>
/// Result of a GetAvailableBinaries operation
/// </summary>
public class GetAvailableBinariesResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public BinaryInfo[] Binaries { get; set; } = Array.Empty<BinaryInfo>();
}

