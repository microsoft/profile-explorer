using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ProfileExplorer.Mcp;

namespace ProfileExplorer.Mcp;

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
    public Task<OpenTraceResult> OpenTraceAsync(string profileFilePath, string processIdentifier) {
        Console.WriteLine($"Mock: OpenTraceAsync called with process identifier: {processIdentifier}");
        return Task.FromResult(new OpenTraceResult { Success = true });
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

    public Task<string> GetFunctionAssemblyAsync(string functionName) {
        Console.WriteLine($"Mock: GetFunctionAssemblyAsync called with functionName: {functionName}");
        return Task.FromResult($@"Mock assembly for function {functionName}:
main:
push rbp
mov rbp, rsp
sub rsp, 0x10
call sub_401000
mov eax, 0
add rsp, 0x10
pop rbp
ret");
    }
    
    public Task<string?> GetFunctionAssemblyToFileAsync(string functionName)
    {
        Console.WriteLine($"Mock: GetFunctionAssemblyToFileAsync called with functionName: {functionName}");
        
        try
        {
            // Create the assembly content
            string assemblyContent = $@"Mock assembly for function {functionName}:
main:
push rbp
mov rbp, rsp
sub rsp, 0x10
call sub_401000
mov eax, 0
add rsp, 0x10
pop rbp
ret";

            // Create the tmp directory path (same logic as real implementation)
            string currentDirectory = Directory.GetCurrentDirectory();
            string srcPath = Path.GetFullPath(Path.Combine(currentDirectory, "..", "..", "src"));
            string tmpDirectory = Path.Combine(srcPath, "tmp");
            
            // Ensure the tmp directory exists
            Directory.CreateDirectory(tmpDirectory);
            
            // Sanitize the function name for file system compatibility
            string sanitizedFunctionName = SanitizeFileName(functionName);
            
            // Create the file name (using "mock-process" as process name)
            string fileName = $"mock-process-{sanitizedFunctionName}.asm";
            string filePath = Path.Combine(tmpDirectory, fileName);
            
            // Write the assembly content to the file
            File.WriteAllText(filePath, assemblyContent);
            
            Console.WriteLine($"Mock: Assembly written to {filePath}");
            return Task.FromResult<string?>(filePath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Mock: Error writing assembly file: {ex.Message}");
            return Task.FromResult<string?>(null);
        }
    }
    
    /// <summary>
    /// Sanitize a string to be safe for use as a file name (for mock implementation)
    /// </summary>
    private static string SanitizeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return "unknown";
            
        // Remove or replace invalid file name characters
        char[] invalidChars = Path.GetInvalidFileNameChars();
        string sanitized = fileName;
        
        foreach (char invalidChar in invalidChars)
        {
            sanitized = sanitized.Replace(invalidChar, '_');
        }
        
        // Also replace some common problematic characters
        sanitized = sanitized.Replace(':', '_')
                                .Replace('<', '_')
                                .Replace('>', '_')
                                .Replace('*', '_')
                                .Replace('?', '_')
                                .Replace('|', '_')
                                .Replace('"', '_');
        
        // Limit length to avoid very long file names
        if (sanitized.Length > 100)
        {
            sanitized = sanitized.Substring(0, 100);
        }
        
        return sanitized;
    }

    public Task<GetAvailableProcessesResult> GetAvailableProcessesAsync(string profileFilePath, double? minWeightPercentage = null, int? topCount = null)
    {
        Console.WriteLine($"Mock: GetAvailableProcessesAsync called with profileFilePath: {profileFilePath}, minWeightPercentage: {minWeightPercentage}, topCount: {topCount}");
        
        // Return mock process list with weight percentages
        var mockProcesses = new ProcessInfo[]
        {
            new ProcessInfo
            {
                ProcessId = 1234,
                Name = "chrome",
                ImageFileName = "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe",
                CommandLine = "\"C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe\" --type=browser",
                WeightPercentage = 25.5,
                Weight = TimeSpan.FromSeconds(10.2),
                Duration = TimeSpan.FromSeconds(30)
            },
            new ProcessInfo
            {
                ProcessId = 5678,
                Name = "notepad",
                ImageFileName = "C:\\Windows\\System32\\notepad.exe",
                CommandLine = "notepad.exe sample.txt",
                WeightPercentage = 5.3,
                Weight = TimeSpan.FromSeconds(2.1),
                Duration = TimeSpan.FromSeconds(30)
            },
            new ProcessInfo
            {
                ProcessId = 9999,
                Name = "MyApp",
                ImageFileName = "C:\\MyApp\\MyApp.exe",
                CommandLine = "MyApp.exe --debug --verbose",
                WeightPercentage = 0.8,
                Weight = TimeSpan.FromSeconds(0.3),
                Duration = TimeSpan.FromSeconds(30)
            }
        };

        // Apply weight filtering if specified
        var filteredProcesses = mockProcesses;
        if (minWeightPercentage.HasValue)
        {
            filteredProcesses = mockProcesses
                .Where(p => p.WeightPercentage >= minWeightPercentage.Value)
                .ToArray();
        }
        
        // Apply top N filtering if specified
        if (topCount.HasValue)
        {
            // Sort by weight percentage descending and take top N
            filteredProcesses = filteredProcesses
                .OrderByDescending(p => p.WeightPercentage)
                .Take(topCount.Value)
                .ToArray();
        }

        return Task.FromResult(new GetAvailableProcessesResult
        {
            Success = true,
            Processes = filteredProcesses
        });
    }

    public Task<GetAvailableFunctionsResult> GetAvailableFunctionsAsync(FunctionFilter? filter = null)
    {
        filter ??= new FunctionFilter();
        Console.WriteLine($"Mock: GetAvailableFunctionsAsync called with filter - ModuleName: {filter.ModuleName}, MinSelfTimePercentage: {filter.MinSelfTimePercentage}, MinTotalTimePercentage: {filter.MinTotalTimePercentage}, TopCount: {filter.TopCount}, SortBySelfTime: {filter.SortBySelfTime}");
        
        // Return mock function list with weight percentages
        var mockFunctions = new FunctionInfo[]
        {
            new FunctionInfo
            {
                Name = "main",
                FullName = "main",
                ModuleName = "MyApp.exe",
                SelfTimePercentage = 45.2,
                TotalTimePercentage = 68.5,
                SelfTime = TimeSpan.FromSeconds(18.1),
                TotalTime = TimeSpan.FromSeconds(27.4),
                SourceFile = "C:\\source\\main.cpp",
                HasAssembly = true
            },
            new FunctionInfo
            {
                Name = "ProcessData",
                FullName = "MyNamespace::ProcessData(int, char*)",
                ModuleName = "MyApp.exe",
                SelfTimePercentage = 22.7,
                TotalTimePercentage = 35.2,
                SelfTime = TimeSpan.FromSeconds(9.1),
                TotalTime = TimeSpan.FromSeconds(14.1),
                SourceFile = "C:\\source\\processor.cpp",
                HasAssembly = true
            },
            new FunctionInfo
            {
                Name = "AllocateMemory",
                FullName = "MemoryManager::AllocateMemory(size_t)",
                ModuleName = "MyApp.exe",
                SelfTimePercentage = 15.3,
                TotalTimePercentage = 15.3,
                SelfTime = TimeSpan.FromSeconds(6.1),
                TotalTime = TimeSpan.FromSeconds(6.1),
                SourceFile = "C:\\source\\memory.cpp",
                HasAssembly = true
            },
            new FunctionInfo
            {
                Name = "StringCompare",
                FullName = "std::basic_string<char>::compare",
                ModuleName = "MSVCP140.dll",
                SelfTimePercentage = 8.9,
                TotalTimePercentage = 8.9,
                SelfTime = TimeSpan.FromSeconds(3.6),
                TotalTime = TimeSpan.FromSeconds(3.6),
                SourceFile = null,
                HasAssembly = true
            },
            new FunctionInfo
            {
                Name = "NtAllocateVirtualMemory",
                FullName = "NtAllocateVirtualMemory",
                ModuleName = "ntdll.dll",
                SelfTimePercentage = 6.2,
                TotalTimePercentage = 6.2,
                SelfTime = TimeSpan.FromSeconds(2.5),
                TotalTime = TimeSpan.FromSeconds(2.5),
                SourceFile = null,
                HasAssembly = true
            },
            new FunctionInfo
            {
                Name = "RtlAllocateHeap",
                FullName = "RtlAllocateHeap",
                ModuleName = "ntdll.dll",
                SelfTimePercentage = 4.1,
                TotalTimePercentage = 4.1,
                SelfTime = TimeSpan.FromSeconds(1.6),
                TotalTime = TimeSpan.FromSeconds(1.6),
                SourceFile = null,
                HasAssembly = true
            },
            new FunctionInfo
            {
                Name = "Helper",
                FullName = "Utils::Helper()",
                ModuleName = "MyApp.exe",
                SelfTimePercentage = 0.5,
                TotalTimePercentage = 2.1,
                SelfTime = TimeSpan.FromSeconds(0.2),
                TotalTime = TimeSpan.FromSeconds(0.8),
                SourceFile = "C:\\source\\utils.cpp",
                HasAssembly = true
            }
        };

        // Apply module filtering if specified
        var filteredFunctions = mockFunctions;
        if (!string.IsNullOrWhiteSpace(filter.ModuleName))
        {
            filteredFunctions = filteredFunctions
                .Where(f => !string.IsNullOrEmpty(f.ModuleName) && 
                           f.ModuleName.Contains(filter.ModuleName, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }
        
        // Apply self time filtering if specified
        if (filter.MinSelfTimePercentage.HasValue)
        {
            filteredFunctions = filteredFunctions
                .Where(f => f.SelfTimePercentage >= filter.MinSelfTimePercentage.Value)
                .ToArray();
        }
        
        // Apply total time filtering if specified
        if (filter.MinTotalTimePercentage.HasValue)
        {
            filteredFunctions = filteredFunctions
                .Where(f => f.TotalTimePercentage >= filter.MinTotalTimePercentage.Value)
                .ToArray();
        }
        
        // Apply top N filtering if specified
        if (filter.TopCount.HasValue)
        {
            // Sort by the chosen metric and take top N
            filteredFunctions = filter.SortBySelfTime
                ? filteredFunctions.OrderByDescending(f => f.SelfTimePercentage).Take(filter.TopCount.Value).ToArray()
                : filteredFunctions.OrderByDescending(f => f.TotalTimePercentage).Take(filter.TopCount.Value).ToArray();
        }
        else
        {
            // If no topCount specified, still sort the results
            filteredFunctions = filter.SortBySelfTime
                ? filteredFunctions.OrderByDescending(f => f.SelfTimePercentage).ToArray()
                : filteredFunctions.OrderByDescending(f => f.TotalTimePercentage).ToArray();
        }

        return Task.FromResult(new GetAvailableFunctionsResult
        {
            Success = true,
            Functions = filteredFunctions
        });
    }

    public Task<GetAvailableBinariesResult> GetAvailableBinariesAsync(double? minTimePercentage = null, TimeSpan? minTime = null, int? topCount = null)
    {
        Console.WriteLine($"Mock: GetAvailableBinariesAsync called with minTimePercentage: {minTimePercentage}, minTime: {minTime}, topCount: {topCount}");
        
        // Return mock binary list - just the unique binary names from the functions
        var mockBinaries = new BinaryInfo[]
        {
            new BinaryInfo
            {
                Name = "MyApp.exe",
                FullPath = "C:\\MyApp\\MyApp.exe",
                TimePercentage = 83.7, // sum of main, ProcessData, AllocateMemory, Helper self time percentages
                Time = TimeSpan.FromSeconds(33.5)
            },
            new BinaryInfo
            {
                Name = "ntdll.dll",
                FullPath = "C:\\Windows\\System32\\ntdll.dll",
                TimePercentage = 10.3, // sum of NtAllocateVirtualMemory, RtlAllocateHeap self time percentages
                Time = TimeSpan.FromSeconds(4.1)
            },
            new BinaryInfo
            {
                Name = "MSVCP140.dll",
                FullPath = "C:\\Windows\\System32\\MSVCP140.dll",
                TimePercentage = 8.9, // StringCompare self time percentage
                Time = TimeSpan.FromSeconds(3.6)
            },
            new BinaryInfo
            {
                Name = "kernel32.dll",
                FullPath = "C:\\Windows\\System32\\kernel32.dll",
                TimePercentage = 0,
                Time = TimeSpan.Zero
            }
        };

        // Apply filtering
        var filteredBinaries = mockBinaries;
        
        if (minTimePercentage.HasValue)
        {
            filteredBinaries = filteredBinaries
                .Where(b => b.TimePercentage >= minTimePercentage.Value)
                .ToArray();
        }
        
        if (minTime.HasValue)
        {
            filteredBinaries = filteredBinaries
                .Where(b => b.Time >= minTime.Value)
                .ToArray();
        }
        
        // Sort by time percentage descending
        filteredBinaries = filteredBinaries
            .OrderByDescending(b => b.TimePercentage)
            .ToArray();

        // Apply top count filtering if specified
        if (topCount.HasValue)
        {
            filteredBinaries = filteredBinaries
                .Take(topCount.Value)
                .ToArray();
        }

        return Task.FromResult(new GetAvailableBinariesResult
        {
            Success = true,
            Binaries = filteredBinaries
        });
    }
}
