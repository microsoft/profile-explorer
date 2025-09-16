using System;
using System.IO;
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
    }
}
