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
        /// Open the Profiling menu and Load Profile dialog in the main window
        /// Corresponds to: Executing AppCommand.LoadProfile
        /// </summary>
        /// <returns>Task that completes when the dialog is opened</returns>
        Task<bool> OpenLoadProfileDialogAsync();

        /// <summary>
        /// Set the profile file path in the Load Profile dialog
        /// Corresponds to: Setting ProfileLoadWindow.ProfileFilePath property
        /// </summary>
        /// <param name="profileFilePath">Path to the profile trace file</param>
        /// <returns>Task that completes when the path is set and processes are enumerated</returns>
        Task<bool> SetProfileFilePathAsync(string profileFilePath);

        /// <summary>
        /// Select process(es) in the process list for profile loading
        /// Corresponds to: Selecting items in ProcessList, triggering ProcessList_OnSelectionChanged
        /// </summary>
        /// <param name="processIds">Array of process IDs to select</param>
        /// <returns>Task that completes when processes are selected</returns>
        Task<bool> SelectProcessesAsync(int[] processIds);

        /// <summary>
        /// Execute the profile load operation
        /// Corresponds to: LoadButton_Click or MainWindow.LoadProfileData backend call
        /// </summary>
        /// <param name="useBackendDirectly">If true, use MainWindow.LoadProfileData directly; if false, simulate LoadButton_Click</param>
        /// <returns>Task that completes when the profile is loaded</returns>
        Task<bool> ExecuteProfileLoadAsync(bool useBackendDirectly = true);

        /// <summary>
        /// Get the current status of the Profile Explorer UI
        /// </summary>
        /// <returns>Status information about the current state</returns>
        Task<ProfilerStatus> GetStatusAsync();
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
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    }
}
