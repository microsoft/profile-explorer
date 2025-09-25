using System;
using System.Threading.Tasks;

namespace ProfileExplorer.Mcp;

/// <summary>
/// Configuration and initialization helper for the Profile Explorer MCP Server
/// </summary>
public static class McpServerConfiguration
{
    /// <summary>
    /// Initialize and start the MCP server with the provided executor
    /// </summary>
    /// <param name="executor">The action executor implementation</param>
    /// <returns>Task that completes when the server is stopped</returns>
    public static async Task StartServerWithExecutorAsync(IMcpActionExecutor executor)
    {
        if (executor == null)
            throw new ArgumentNullException(nameof(executor));

        // Set the executor for the tools
        ProfileExplorerTools.SetExecutor(executor);

        // Start the server
        await ProfileExplorerMcpServer.StartServerAsync();
    }

    /// <summary>
    /// Initialize the MCP server with a mock executor for testing
    /// </summary>
    /// <returns>Task that completes when the server is stopped</returns>
    public static async Task StartServerWithMockExecutorAsync()
    {
        var mockExecutor = new MockMcpActionExecutor();
        await StartServerWithExecutorAsync(mockExecutor);
    }
}
