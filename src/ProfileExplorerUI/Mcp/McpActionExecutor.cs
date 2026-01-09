// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using ProfileExplorer.Mcp;
using ProfileExplorer.Core.Profile.Data;
using ProfileExplorer.Core.IR;

namespace ProfileExplorer.UI.Mcp;

/// <summary>
/// Implementation of IMcpActionExecutor that provides Profile Explorer integration for AI agent interactions.
/// This connects MCP tools to the actual Profile Explorer UI functionality.
/// </summary>
public class McpActionExecutor : IMcpActionExecutor
{
    private readonly MainWindow mainWindow;
    private readonly Dispatcher dispatcher;

    public McpActionExecutor(MainWindow mainWindow)
    {
        this.mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
        this.dispatcher = mainWindow.Dispatcher;
    }

    public async Task<OpenTraceResult> OpenTraceAsync(string profileFilePath, string processIdentifier)
    {
        // Validate file exists first
        if (!File.Exists(profileFilePath))
        {
            return new OpenTraceResult
            {
                Success = false,
                FailureReason = OpenTraceFailureReason.FileNotFound,
                ErrorMessage = $"Trace file not found: {profileFilePath}"
            };
        }

        // Check if the requested trace and process is already loaded
        var alreadyLoadedResult = await CheckIfTraceAlreadyLoadedAsync(profileFilePath, processIdentifier);
        if (alreadyLoadedResult != null)
        {
            return alreadyLoadedResult;
        }

        // Try to parse as a process ID first
        if (int.TryParse(processIdentifier, out int processId))
        {
            if (processId <= 0)
            {
                return new OpenTraceResult
                {
                    Success = false,
                    FailureReason = OpenTraceFailureReason.UnknownError,
                    ErrorMessage = "Process ID must be a positive integer."
                };
            }
            return await OpenTraceByProcessIdAsync(profileFilePath, processId);
        }

        // If not a number, treat as process name
        return await OpenTraceByProcessNameAsync(profileFilePath, processIdentifier);
    }

    /// <summary>
    /// Checks if the requested trace file and process is already loaded.
    /// Returns a successful OpenTraceResult if already loaded, or null if not loaded.
    /// This helps avoid timeout errors when a trace was already loaded but MCP timed out waiting.
    /// </summary>
    private async Task<OpenTraceResult> CheckIfTraceAlreadyLoadedAsync(string profileFilePath, string processIdentifier)
    {
        return await dispatcher.InvokeAsync(() =>
        {
            try
            {
                var sessionState = mainWindow.SessionState;
                var profileData = sessionState?.ProfileData;
                var report = profileData?.Report;

                if (report == null)
                {
                    return null; // No profile loaded
                }

                // Get the currently loaded trace path
                string loadedTracePath = report.TraceInfo?.TraceFilePath;
                if (string.IsNullOrEmpty(loadedTracePath))
                {
                    return null;
                }

                // Normalize paths for comparison
                string normalizedRequestedPath = Path.GetFullPath(profileFilePath).ToLowerInvariant();
                string normalizedLoadedPath = Path.GetFullPath(loadedTracePath).ToLowerInvariant();

                if (normalizedRequestedPath != normalizedLoadedPath)
                {
                    return null; // Different trace file
                }

                // Check if the requested process matches
                var currentProcess = report.Process;
                if (currentProcess == null)
                {
                    return null;
                }

                bool processMatches = false;
                
                // Check by process ID
                if (int.TryParse(processIdentifier, out int requestedPid))
                {
                    processMatches = currentProcess.ProcessId == requestedPid;
                }
                else
                {
                    // Check by process name (case-insensitive, partial match)
                    processMatches = 
                        (currentProcess.Name != null && 
                         currentProcess.Name.Contains(processIdentifier, StringComparison.OrdinalIgnoreCase)) ||
                        (currentProcess.ImageFileName != null && 
                         currentProcess.ImageFileName.Contains(processIdentifier, StringComparison.OrdinalIgnoreCase));
                }

                if (processMatches)
                {
                    // The requested trace and process is already loaded!
                    return new OpenTraceResult
                    {
                        Success = true,
                        AlreadyLoaded = true,
                        Message = $"Trace and process already loaded (PID: {currentProcess.ProcessId}, Name: {currentProcess.Name})"
                    };
                }

                return null; // Same trace but different process requested
            }
            catch (Exception)
            {
                return null; // On any error, proceed with normal loading
            }
        });
    }

    private async Task<OpenTraceResult> OpenTraceByProcessIdAsync(string profileFilePath, int processId)
    {
        try
        {
            var loadResult = await LoadTraceAsync(profileFilePath);
            if (!loadResult.Success) {
                return loadResult.Result;
            }
            
            // Select the process by PID
            return await SelectProcessByPidAsync(loadResult.ProfileLoadWindow, processId);
        }
        catch (Exception ex)
        {
            return new OpenTraceResult
            {
                Success = false,
                FailureReason = OpenTraceFailureReason.UnknownError,
                ErrorMessage = $"Unexpected error: {ex.Message}"
            };
        }
    }

    private async Task<OpenTraceResult> OpenTraceByProcessNameAsync(string profileFilePath, string processName) {
        try {
            // Load the trace and prepare the process list
            var loadResult = await LoadTraceAsync(profileFilePath);
            if (!loadResult.Success) {
                return loadResult.Result;
            }

            // Select the process by name
            return await SelectProcessByNameAsync(loadResult.ProfileLoadWindow, processName);
        }
        catch (Exception ex) {
            return new OpenTraceResult {
                Success = false,
                FailureReason = OpenTraceFailureReason.UnknownError,
                ErrorMessage = $"Unexpected error: {ex.Message}"
            };
        }
    }

    private async Task<(bool Success, ProfileExplorer.UI.ProfileLoadWindow ProfileLoadWindow, OpenTraceResult Result)> LoadTraceAsync(string profileFilePath) {
        // Execute the command in the background since ShowDialog() blocks
        var task = Task.Run(() =>
        {
            dispatcher.Invoke(() => AppCommand.LoadProfile.Execute(null, mainWindow));
        });
        
        // Wait for the dialog to be created and shown (with timeout)
        var profileLoadWindow = await WaitForWindowAsync<ProfileExplorer.UI.ProfileLoadWindow>(TimeSpan.FromSeconds(5));
        if (profileLoadWindow == null)
        {
            var errorResult = new OpenTraceResult 
            {
                Success = false,
                FailureReason = OpenTraceFailureReason.UIError,
                ErrorMessage = "Failed to open profile load dialog window"
            };
            return (false, null, errorResult);
        }
        
        // Step 1: Set the profile file path
        await dispatcher.InvokeAsync(() => {
            profileLoadWindow.ProfileFilePath = profileFilePath;
        });

        // Step 2: Trigger the text changed logic to load the process list
        await dispatcher.InvokeAsync(() => 
        {
            var textChangedMethod = profileLoadWindow.GetType().GetMethod("ProfileAutocompleteBox_TextChanged",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (textChangedMethod != null) 
            {
                textChangedMethod.Invoke(profileLoadWindow, new object[] { profileLoadWindow, new RoutedEventArgs() });
            }
        });

        // Step 3: Wait for process list to finish loading (with timeout)
        bool processListLoaded = await WaitForProcessListLoadedAsync(profileLoadWindow, TimeSpan.FromMinutes(2));
        if (!processListLoaded) 
        {
            var errorResult = new OpenTraceResult 
            {
                Success = false,
                FailureReason = OpenTraceFailureReason.ProcessListLoadTimeout,
                ErrorMessage = "Timeout while loading process list from trace file"
            };
            return (false, profileLoadWindow, errorResult);
        }

        // Step 4: Additional verification that ItemsSource is actually populated
        var verificationResult = await WaitForItemsSourceAsync(profileLoadWindow, TimeSpan.FromSeconds(10));
        if (!verificationResult) 
        {
            var errorResult = new OpenTraceResult 
            {
                Success = false,
                FailureReason = OpenTraceFailureReason.ProcessListLoadTimeout,
                ErrorMessage = "Process list failed to load properly"
            };
            return (false, profileLoadWindow, errorResult);
        }

        return (true, profileLoadWindow, null);
    }

    private async Task<OpenTraceResult> SelectProcessByPidAsync(ProfileExplorer.UI.ProfileLoadWindow profileLoadWindow, int processId) {
        // Step 5: Select the specified process from the process list
        // Use retry logic in case there are still brief timing issues
        bool processSelected = false;
        for (int retryCount = 0; retryCount < 3 && !processSelected; retryCount++)
        {
            if (retryCount > 0)
            {
                await Task.Delay(500); // Brief delay between retries
            }
            
            processSelected = await dispatcher.InvokeAsync(() =>
            {
                var processListControl = profileLoadWindow.FindName("ProcessList") as System.Windows.Controls.ListView;
                if (processListControl?.ItemsSource != null)
                {
                    try
                    {
                        var processSummaries = processListControl.ItemsSource.Cast<ProcessSummary>().ToList();
                        var targetProcess = processSummaries.FirstOrDefault(p => p.Process.ProcessId == processId);
                        
                        if (targetProcess != null)
                        {
                            processListControl.SelectedItems.Clear();
                            processListControl.SelectedItems.Add(targetProcess);
                            
                            // Trigger the selection changed event
                            var selectionChangedMethod = profileLoadWindow.GetType().GetMethod("ProcessList_OnSelectionChanged", 
                                BindingFlags.NonPublic | BindingFlags.Instance);
                            if (selectionChangedMethod != null)
                            {
                                var args = new System.Windows.Controls.SelectionChangedEventArgs(
                                    System.Windows.Controls.Primitives.Selector.SelectionChangedEvent,
                                    new object[0], new object[] { targetProcess });
                                selectionChangedMethod.Invoke(profileLoadWindow, new object[] { processListControl, args });
                            }
                            return true;
                        }
                    }
                    catch (Exception)
                    {
                        // If casting or process selection fails, return false
                        return false;
                    }
                }
                return false;
            });
        }
        
        if (!processSelected)
        {
            return new OpenTraceResult
            {
                Success = false,
                FailureReason = OpenTraceFailureReason.ProcessNotFound,
                ErrorMessage = $"Process with ID {processId} not found in trace file",
            };
        }
        
        // Step 6: Execute the profile load (click Load button)
        await dispatcher.InvokeAsync(() =>
        {
            var loadButtonClickMethod = profileLoadWindow.GetType().GetMethod("LoadButton_Click", 
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (loadButtonClickMethod != null)
            {
                loadButtonClickMethod.Invoke(profileLoadWindow, new object[] { profileLoadWindow, new RoutedEventArgs() });
            }
        });
        
        // Step 7: Wait for the profile to finish loading
        bool profileLoadCompleted = await WaitForProfileLoadingCompletedAsync(profileLoadWindow, TimeSpan.FromMinutes(30));
        if (!profileLoadCompleted)
        {
            return new OpenTraceResult
            {
                Success = false,
                FailureReason = OpenTraceFailureReason.ProfileLoadTimeout,
                ErrorMessage = "Timeout while waiting for profile loading to complete. The profile may be very large or there may be symbol loading issues."
            };
        }
        
        return new OpenTraceResult { Success = true };
    }

    private async Task<OpenTraceResult> SelectProcessByNameAsync(ProfileExplorer.UI.ProfileLoadWindow profileLoadWindow, string processName) {
        // Step 5: Select the specified process by name from the process list
        bool processSelected = false;
        for (int retryCount = 0; retryCount < 3 && !processSelected; retryCount++) {
            if (retryCount > 0) {
                await Task.Delay(500); // Brief delay between retries
            }

            processSelected = await dispatcher.InvokeAsync(() => {
                var processListControl = profileLoadWindow.FindName("ProcessList") as System.Windows.Controls.ListView;
                if (processListControl?.ItemsSource != null) {
                    try {
                        var processSummaries = processListControl.ItemsSource.Cast<ProcessSummary>().ToList();

                        // Look for process by name (case-insensitive, supports partial matching)
                        var targetProcess = processSummaries.FirstOrDefault(p =>
                            p.Process.Name != null &&
                            p.Process.Name.Contains(processName, StringComparison.OrdinalIgnoreCase));

                        // If no partial match, try exact match
                        if (targetProcess == null) {
                            targetProcess = processSummaries.FirstOrDefault(p =>
                                string.Equals(p.Process.Name, processName, StringComparison.OrdinalIgnoreCase));
                        }

                        // If still no match, try matching against image file name
                        if (targetProcess == null) {
                            targetProcess = processSummaries.FirstOrDefault(p =>
                                p.Process.ImageFileName != null &&
                                p.Process.ImageFileName.Contains(processName, StringComparison.OrdinalIgnoreCase));
                        }

                        if (targetProcess != null) {
                            processListControl.SelectedItems.Clear();
                            processListControl.SelectedItems.Add(targetProcess);

                            // Trigger the selection changed event
                            var selectionChangedMethod = profileLoadWindow.GetType().GetMethod("ProcessList_OnSelectionChanged",
                                BindingFlags.NonPublic | BindingFlags.Instance);
                            if (selectionChangedMethod != null) {
                                var args = new System.Windows.Controls.SelectionChangedEventArgs(
                                    System.Windows.Controls.Primitives.Selector.SelectionChangedEvent,
                                    new object[0], new object[] { targetProcess });
                                selectionChangedMethod.Invoke(profileLoadWindow, new object[] { processListControl, args });
                            }
                            return true;
                        }
                    }
                    catch (Exception) {
                        // If casting or process selection fails, return false
                        return false;
                    }
                }
                return false;
            });
        }

        if (!processSelected) {
            return new OpenTraceResult {
                Success = false,
                FailureReason = OpenTraceFailureReason.ProcessNotFound,
                ErrorMessage = $"Process '{processName}' not found in trace file",
            };
        }

        // Step 6: Execute the profile load (click Load button)
        await dispatcher.InvokeAsync(() => {
            var loadButtonClickMethod = profileLoadWindow.GetType().GetMethod("LoadButton_Click",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (loadButtonClickMethod != null) {
                loadButtonClickMethod.Invoke(profileLoadWindow, new object[] { profileLoadWindow, new RoutedEventArgs() });
            }
        });

        // Step 7: Wait for the profile to finish loading
        bool profileLoadCompleted = await WaitForProfileLoadingCompletedAsync(profileLoadWindow, TimeSpan.FromMinutes(30));
        if (!profileLoadCompleted)
        {
            return new OpenTraceResult
            {
                Success = false,
                FailureReason = OpenTraceFailureReason.ProfileLoadTimeout,
                ErrorMessage = "Timeout while waiting for profile loading to complete. The profile may be very large or there may be symbol loading issues."
            };
        }

        return new OpenTraceResult { Success = true };
    }

    /// <summary>
    /// Waits for a window of type T to appear, with a timeout.
    /// </summary>
    private async Task<T> WaitForWindowAsync<T>(TimeSpan timeout) where T : Window
    {
        var startTime = DateTime.UtcNow;
        
        while (DateTime.UtcNow - startTime < timeout)
        {
            var window = await dispatcher.InvokeAsync(() => 
                mainWindow.OwnedWindows.OfType<T>().FirstOrDefault());
            
            if (window != null)
            {
                return window;
            }
            
            await Task.Delay(100); // Check every 100ms
        }
        
        return null;
    }

    /// <summary>
    /// Waits for the process list to finish loading in the ProfileLoadWindow.
    /// Monitors the IsLoadingProcessList and ShowProcessList properties.
    /// Also ensures at least 2 processes are loaded to avoid partial loading issues.
    /// </summary>
    private async Task<bool> WaitForProcessListLoadedAsync(ProfileExplorer.UI.ProfileLoadWindow window, TimeSpan timeout)
    {
        var startTime = DateTime.UtcNow;
        const int MinimumProcessCount = 2; // Wait for at least 2 processes to ensure full loading
        
        while (DateTime.UtcNow - startTime < timeout)
        {
            var (isLoading, showList, hasItemsSource, processCount) = await dispatcher.InvokeAsync(() =>
            {
                var isLoadingProp = window.GetType().GetProperty("IsLoadingProcessList");
                var showListProp = window.GetType().GetProperty("ShowProcessList");
                var processListControl = window.FindName("ProcessList") as System.Windows.Controls.ListView;
                
                bool isLoading = isLoadingProp?.GetValue(window) as bool? ?? false;
                bool showList = showListProp?.GetValue(window) as bool? ?? false;
                bool hasItemsSource = processListControl?.ItemsSource != null;
                int processCount = 0;
                
                if (hasItemsSource)
                {
                    try
                    {
                        processCount = processListControl.ItemsSource.Cast<ProcessSummary>().Count();
                    }
                    catch
                    {
                        processCount = 0;
                        hasItemsSource = false; // If we can't cast, treat as not having items source
                    }
                }
                
                return (isLoading, showList, hasItemsSource, processCount);
            });
            
            // Process list is ready when:
            // 1. It's not currently loading (IsLoadingProcessList = false)
            // 2. The process list is shown (ShowProcessList = true)
            // 3. The ProcessList control has a valid ItemsSource
            // 4. The ProcessList control has at least the minimum number of processes OR we've waited long enough
            if (!isLoading && showList && hasItemsSource && 
                (processCount >= MinimumProcessCount || 
                    (processCount > 0 && DateTime.UtcNow - startTime > TimeSpan.FromSeconds(10))))
            {
                return true;
            }
            
            // Additional check: If we're not loading but still don't have ItemsSource,
            // even though ShowProcessList is true, continue waiting as this indicates
            // the UI update cycle hasn't completed yet
            if (!isLoading && showList && !hasItemsSource)
            {
                // This is likely a timing issue - UI state says ready but ItemsSource not set yet
                // Continue waiting unless we've been in this state for too long
                if (DateTime.UtcNow - startTime > TimeSpan.FromSeconds(10))
                {
                    return false; // Likely an error occurred - UI says ready but no data
                }
            }
            
            // If we're not loading and not showing the list, it might indicate an error
            if (!isLoading && !showList)
            {
                // Give it a bit more time in case the state transitions haven't completed
                if (DateTime.UtcNow - startTime > TimeSpan.FromSeconds(5))
                {
                    return false; // Likely an error occurred
                }
            }
            
            await Task.Delay(200); // Check every 200ms
        }
        
        return false; // Timeout
    }

    /// <summary>
    /// Additional verification that ItemsSource is populated.
    /// This handles edge cases where the main waiting logic completes but ItemsSource is still briefly null.
    /// </summary>
    private async Task<bool> WaitForItemsSourceAsync(ProfileExplorer.UI.ProfileLoadWindow window, TimeSpan timeout)
    {
        var startTime = DateTime.UtcNow;
        
        while (DateTime.UtcNow - startTime < timeout)
        {
            var (hasItemsSource, processCount, isAccessible) = await dispatcher.InvokeAsync(() =>
            {
                var processListControl = window.FindName("ProcessList") as System.Windows.Controls.ListView;
                bool hasItemsSource = processListControl?.ItemsSource != null;
                int processCount = 0;
                bool isAccessible = false;
                
                if (hasItemsSource)
                {
                    try
                    {
                        // Try to access the items to ensure they're actually available
                        var items = processListControl.ItemsSource.Cast<ProcessSummary>().ToList();
                        processCount = items.Count;
                        isAccessible = true;
                    }
                    catch
                    {
                        hasItemsSource = false;
                        processCount = 0;
                        isAccessible = false;
                    }
                }
                
                return (hasItemsSource, processCount, isAccessible);
            });
            
            if (hasItemsSource && processCount > 0 && isAccessible)
            {
                return true;
            }
            
            await Task.Delay(50); // Check every 50ms for more responsive verification
        }
        
        return false; // Timeout - ItemsSource never became available
    }

    /// <summary>
    /// Waits for the profile loading to complete by monitoring the IsLoadingProfile property.
    /// This ensures the profile is fully loaded before operations like GetAvailableFunctions can succeed.
    /// </summary>
    private async Task<bool> WaitForProfileLoadingCompletedAsync(ProfileExplorer.UI.ProfileLoadWindow window, TimeSpan timeout)
    {
        var startTime = DateTime.UtcNow;
        
        while (DateTime.UtcNow - startTime < timeout)
        {
            bool isLoadingProfile = await dispatcher.InvokeAsync(() =>
            {
                var isLoadingProp = window.GetType().GetProperty("IsLoadingProfile");
                return isLoadingProp?.GetValue(window) as bool? ?? false;
            });
            
            // Profile loading is complete when IsLoadingProfile is false
            if (!isLoadingProfile)
            {
                return true;
            }
            
            await Task.Delay(500); // Check every 500ms since profile loading can take a long time
        }
        
        return false; // Timeout
    }

    /// <summary>
    /// Helper method to get the section panel and function list control
    /// </summary>
    private async Task<(ProfileExplorer.UI.SectionPanelPair SectionPanel, System.Windows.Controls.ListView FunctionList)> 
        GetFunctionListControlAsync()
    {
        var sectionPanel = await dispatcher.InvokeAsync(() =>
        {
            return mainWindow.FindName("SectionPanel") as ProfileExplorer.UI.SectionPanelPair ??
                    mainWindow.FindPanel(ProfileExplorer.UI.ToolPanelKind.Section) as ProfileExplorer.UI.SectionPanelPair;
        });

        if (sectionPanel?.MainPanel == null)
        {
            return (null, null);
        }

        var functionListControl = await dispatcher.InvokeAsync(() => 
            sectionPanel.MainPanel.FindName("FunctionList") as System.Windows.Controls.ListView);

        return (sectionPanel, functionListControl);
    }

    public async Task<ProfilerStatus> GetStatusAsync()
    {
        return await dispatcher.InvokeAsync(() =>
        {
            try
            {
                var sessionState = mainWindow.SessionState;
                var profileData = sessionState?.ProfileData;
                var report = profileData?.Report; // This is the ProfileDataReport with all the info

                // Check if we have profile data loaded
                bool isProfileLoaded = profileData != null && report != null;
                string currentProfilePath = null;
                int[] loadedProcesses = Array.Empty<int>();
                string[] activeFilters = Array.Empty<string>();
                ProcessInfo? currentProcess = null;

                if (isProfileLoaded && report != null)
                {
                    // Get trace file path and duration from ProfileDataReport.TraceInfo
                    currentProfilePath = report.TraceInfo?.TraceFilePath;

                    // Get process information from ProfileDataReport.Process (main process)
                    if (report.Process != null)
                    {
                        currentProcess = new ProcessInfo
                        {
                            ProcessId = report.Process.ProcessId,
                            Name = report.Process.Name ?? string.Empty,
                            ImageFileName = report.Process.ImageFileName ?? string.Empty,
                            CommandLine = report.Process.CommandLine ?? string.Empty
                        };
                    }

                    // Get all running processes from ProfileDataReport.RunningProcesses
                    if (report.RunningProcesses?.Count > 0)
                    {
                        loadedProcesses = report.RunningProcesses
                            .Select(p => p.Process.ProcessId)  // ProcessSummary.Process.ProcessId
                            .ToArray();
                    }

                    // Get active filter information from session state
                    var filterList = new List<string>();
                    var profileFilter = sessionState?.ProfileFilter;
                    
                    if (profileFilter != null)
                    {
                        if (profileFilter.HasFilter)
                        {
                            filterList.Add("Has active filter");
                        }
                        
                        if (profileFilter.HasThreadFilter)
                        {
                            filterList.Add($"Thread filter: {profileFilter.ThreadFilterText}");
                        }
                        
                        if (profileFilter.FilteredTime != TimeSpan.Zero)
                        {
                            filterList.Add($"Filtered time: {profileFilter.FilteredTime}");
                        }
                    }

                    activeFilters = filterList.ToArray();
                }

                return new ProfilerStatus
                {
                    IsProfileLoaded = isProfileLoaded,
                    CurrentProfilePath = currentProfilePath,
                    LoadedProcesses = loadedProcesses,
                    ActiveFilters = activeFilters,
                    CurrentProcess = currentProcess,
                    LastUpdated = DateTime.UtcNow
                };
            }
            catch (Exception)
            {
                // Return safe defaults if anything goes wrong
                return new ProfilerStatus
                {
                    IsProfileLoaded = false,
                    CurrentProfilePath = null,
                    LoadedProcesses = Array.Empty<int>(),
                    ActiveFilters = Array.Empty<string>(),
                    CurrentProcess = null,
                    LastUpdated = DateTime.UtcNow
                };
            }
        });
    }

    private async Task<string> GetFunctionAssemblyAsync(string functionName)
    {
        try
        {
            // Step 1: Get the function list control
            var (sectionPanel, functionListControl) = await GetFunctionListControlAsync();
            if (sectionPanel == null || functionListControl == null)
            {
                return null;
            }

            // Step 3: Find the function in the function list
            var functionFound = await dispatcher.InvokeAsync(() =>
            {
                if (functionListControl?.ItemsSource != null)
                {
                    try
                    {
                        // Search through the function list to find the matching function
                        foreach (var item in functionListControl.ItemsSource)
                        {
                            if (item is ProfileExplorer.UI.IRTextFunctionEx functionEx)
                            {
                                // Check if the function name matches
                                if (functionEx.ToolTip == functionName)
                                {
                                    // Early check: Verify function has assembly data before proceeding
                                    if (!HasAssemblyData(functionEx))
                                    {
                                        return false; // Function found but has no assembly data
                                    }
                                    // Select the function
                                    functionListControl.SelectedItems.Clear();
                                    functionListControl.SelectedItems.Add(functionEx);
                                    functionListControl.ScrollIntoView(functionEx);

                                    // Programmatically trigger the double-click event
                                    var method = sectionPanel.MainPanel.GetType().GetMethod("FunctionDoubleClick",
                                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                    if (method != null)
                                    {
                                        // Create a ListViewItem to simulate the sender
                                        var listViewItem = functionListControl.ItemContainerGenerator.ContainerFromItem(functionEx)
                                            as System.Windows.Controls.ListViewItem;
                                        if (listViewItem != null)
                                        {
                                            listViewItem.Content = functionEx;
                                            var mouseEventArgs = new System.Windows.Input.MouseButtonEventArgs(
                                                System.Windows.Input.Mouse.PrimaryDevice,
                                                Environment.TickCount,
                                                System.Windows.Input.MouseButton.Left)
                                            {
                                                RoutedEvent = System.Windows.Controls.Control.MouseDoubleClickEvent
                                            };
                                            method.Invoke(sectionPanel.MainPanel, new object[] { listViewItem, mouseEventArgs });
                                            return true;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception)
                    {
                        return false;
                    }
                }
                return false;
            });

            if (!functionFound)
            {
                return null; // Function not found in the list
            }

            // Add a delay to let the UI process the double-click and open the document
            await Task.Delay(500); // Increased delay to allow more time for document and timing data loading

            // Step 4: Wait for the assembly document to be opened
            var assemblyDocument = await WaitForAssemblyDocumentAsync(TimeSpan.FromSeconds(10));
            if (assemblyDocument == null)
            {
                return null; // Timeout waiting for assembly document
            }

            // Step 4.5: Wait for timing information to be available
            var timingDataAvailable = await WaitForTimingDataAsync(assemblyDocument, TimeSpan.FromSeconds(15));
            
            // Step 5: Retrieve the assembly content and timing information from the document
            var assemblyContentWithTiming = await dispatcher.InvokeAsync(() =>
            {
                var assemblyText = assemblyDocument.TextView.Text;
                var columnData = assemblyDocument.TextView.ProfileColumnData;
                
                if (columnData == null || !columnData.HasData)
                {
                    // No timing information available, return just the assembly text
                    return assemblyText;
                }
                
                // Extract timing information and combine with assembly text
                var lines = assemblyText.Split('\n');
                var result = new System.Text.StringBuilder();
                var function = assemblyDocument.TextView.Function;
                
                for (int i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    
                    // Try to get timing information for this line by finding the IR element
                    // Line numbers in TextLocation are 0-based, but we're iterating 0-based as well
                    IRElement elementAtLine = null;
                    string timePercentage = null;
                    string timeValue = null;
                    
                    if (function != null)
                    {
                        // Find elements that correspond to this line
                        foreach (var block in function.Blocks)
                        {
                            foreach (var tuple in block.Tuples)
                            {
                                if (tuple.TextLocation.Line == i) // Both are 0-based
                                {
                                    elementAtLine = tuple;
                                    break;
                                }
                            }
                            if (elementAtLine != null) break;
                        }
                    }
                    
                    if (elementAtLine != null)
                    {
                        var rowValue = columnData.GetValues(elementAtLine);
                        if (rowValue != null)
                        {
                            // Extract Time(%) and Time(ms) information
                            foreach (var columnValue in rowValue.ColumnValues)
                            {
                                var column = columnValue.Key;
                                var value = columnValue.Value;
                                
                                // Check for time percentage column
                                if (column.Title.Contains("Time (%)") || column.ColumnName.Contains("TimePercentage"))
                                {
                                    timePercentage = value.Text;
                                }
                                // Check for time value column
                                else if (column.Title.Contains("Time (") || column.ColumnName.Contains("Time"))
                                {
                                    timeValue = value.Text;
                                }
                            }
                        }
                    }
                    
                    // Build the complete line with timing information on the same line
                    result.Append(line);
                    
                    // Append timing information if available, aligned to the right on the same line
                    if (!string.IsNullOrEmpty(timePercentage) || !string.IsNullOrEmpty(timeValue))
                    {
                        // Calculate padding to align timing info to the right (assuming 100-character width)
                        const int TargetWidth = 100;
                        int currentLength = line.Length;
                        int timingInfoLength = 15; // Approximate length of timing info
                        if (!string.IsNullOrEmpty(timePercentage)) timingInfoLength += timePercentage.Length + 12; // "Time(%): " + value
                        if (!string.IsNullOrEmpty(timeValue)) timingInfoLength += timeValue.Length + 8; // "Time: " + value
                        if (!string.IsNullOrEmpty(timePercentage) && !string.IsNullOrEmpty(timeValue)) timingInfoLength += 2; // ", "
                        
                        int paddingNeeded = Math.Max(2, TargetWidth - currentLength - timingInfoLength);
                        
                        result.Append(new string(' ', paddingNeeded));
                        result.Append("[");
                        if (!string.IsNullOrEmpty(timePercentage))
                        {
                            result.Append($"Time(%): {timePercentage}");
                        }
                        if (!string.IsNullOrEmpty(timeValue))
                        {
                            if (!string.IsNullOrEmpty(timePercentage))
                                result.Append(", ");
                            result.Append($"Time: {timeValue}");
                        }
                        result.Append("]");
                    }
                    
                    // Add line break only at the end, after timing info is added
                    if (i < lines.Length - 1)
                        result.AppendLine();
                }
                
                return result.ToString();
            });

            return assemblyContentWithTiming;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Waits for a new assembly document to be opened after the function double-click.
    /// Simplified approach: just wait for any document with assembly content to appear.
    /// </summary>
    private async Task<ProfileExplorer.UI.IRDocumentHost> WaitForAssemblyDocumentAsync(TimeSpan timeout)
    {
        var startTime = DateTime.UtcNow;

        // Simple approach: continuously check all open documents for assembly content
        while (DateTime.UtcNow - startTime < timeout)
        {
            var assemblyDocument = await dispatcher.InvokeAsync(() =>
            {
                try
                {
                    if (mainWindow is ProfileExplorer.UI.IUISession session)
                    {
                        var openDocs = session.OpenDocuments;

                        // Check each document for assembly content
                        foreach (var document in openDocs)
                        {
                            if (document?.Text != null && !string.IsNullOrEmpty(document.Text))
                            {
                                var text = document.Text;
                                
                                // Enhanced assembly detection with more patterns
                                bool hasAssemblyInstructions = text.Contains("mov ") || text.Contains("call ") || 
                                    text.Contains("ret") || text.Contains("push ") || text.Contains("pop ") || 
                                    text.Contains("jmp ") || text.Contains("add ") || text.Contains("sub ") || 
                                    text.Contains("lea ") || text.Contains("cmp ") || text.Contains("test ") ||
                                    text.Contains("xor ") || text.Contains("and ") || text.Contains("or ");
                                
                                // Also check for register patterns and memory addresses
                                bool hasAssemblyPatterns = text.Contains("rax") || text.Contains("rbx") || 
                                    text.Contains("rcx") || text.Contains("rdx") || text.Contains("rsp") ||
                                    text.Contains("rbp") || text.Contains("eax") || text.Contains("ebx") ||
                                    (text.Contains("[") && text.Contains("]")); // Memory addressing

                                if (hasAssemblyInstructions || hasAssemblyPatterns)
                                {
                                    // Found a document with assembly content, find its IRDocumentHost
                                    var sessionStateField = mainWindow.GetType().GetField("sessionState_", 
                                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                    if (sessionStateField?.GetValue(mainWindow) is object sessionState)
                                    {
                                        var documentHostsProperty = sessionState.GetType().GetProperty("DocumentHosts");
                                        if (documentHostsProperty?.GetValue(sessionState) is System.Collections.IList documentHosts)
                                        {
                                            foreach (var hostInfo in documentHosts)
                                            {
                                                var documentHostProperty = hostInfo.GetType().GetProperty("DocumentHost");
                                                var docHost = documentHostProperty?.GetValue(hostInfo) as ProfileExplorer.UI.IRDocumentHost;
                                                if (docHost?.TextView == document)
                                                {
                                                    return docHost;
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // If there's any exception, continue searching
                }
                return null;
            });

            if (assemblyDocument != null)
            {
                return assemblyDocument;
            }

            await Task.Delay(200); // Check every 200ms
        }

        return null; // Timeout
    }

    /// <summary>
    /// Waits for timing data to be available in the assembly document.
    /// The ProfileColumnData might not be immediately available after the document opens.
    /// </summary>
    private async Task<bool> WaitForTimingDataAsync(ProfileExplorer.UI.IRDocumentHost documentHost, TimeSpan timeout)
    {
        var startTime = DateTime.UtcNow;
        
        while (DateTime.UtcNow - startTime < timeout)
        {
            var hasTimingData = await dispatcher.InvokeAsync(() =>
            {
                try
                {
                    var columnData = documentHost.TextView?.ProfileColumnData;
                    if (columnData != null && columnData.HasData)
                    {
                        // Additionally check if we can actually get timing values from at least one element
                        var function = documentHost.TextView.Function;
                        if (function != null)
                        {
                            // Try to find at least one element with timing data
                            foreach (var block in function.Blocks)
                            {
                                foreach (var tuple in block.Tuples)
                                {
                                    var rowValue = columnData.GetValues(tuple);
                                    if (rowValue?.ColumnValues != null && rowValue.ColumnValues.Any())
                                    {
                                        // Check if any column contains timing information
                                        foreach (var columnValue in rowValue.ColumnValues)
                                        {
                                            var column = columnValue.Key;
                                            if (column.Title.Contains("Time") || column.ColumnName.Contains("Time"))
                                            {
                                                return true; // Found timing data
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    return false;
                }
                catch
                {
                    return false;
                }
            });
            
            if (hasTimingData)
            {
                return true;
            }
            
            await Task.Delay(500); // Check every 500ms - timing data loading can take a bit longer
        }
        
        return false; // Timeout - proceed without timing data
    }

    public async Task<string?> GetFunctionAssemblyToFileAsync(string functionName)
    {
        try
        {
            // Get the assembly content first
            string assemblyContent = await GetFunctionAssemblyAsync(functionName);
            
            if (assemblyContent == null)
            {
                return null; // Function not found
            }

            // Get the current process name from the loaded session
            string processName = await GetCurrentProcessNameAsync();

            // Create the tmp directory path
            string currentDirectory = Directory.GetCurrentDirectory();
            string srcPath = Path.GetFullPath(Path.Combine(currentDirectory, "..", "..", "src"));
            string tmpDirectory = Path.Combine(srcPath, "tmp");
            
            // Ensure the tmp directory exists
            Directory.CreateDirectory(tmpDirectory);
            
            // Sanitize the names for file system compatibility
            string sanitizedProcessName = SanitizeFileName(processName);
            string sanitizedFunctionName = SanitizeFileName(functionName);
            
            // Create the file name
            string fileName = $"{sanitizedProcessName}-{sanitizedFunctionName}.asm";
            string filePath = Path.Combine(tmpDirectory, fileName);
            
            // Write the assembly content to the file
            await File.WriteAllTextAsync(filePath, assemblyContent);

            return filePath;
        }
        catch
        {
            return null;
        }
    }

    public async Task<GetAvailableProcessesResult> GetAvailableProcessesAsync(string profileFilePath, double? minWeightPercentage = null, int? topCount = null)
    {
        // Validate file exists first
        if (!File.Exists(profileFilePath))
        {
            return new GetAvailableProcessesResult
            {
                Success = false,
                ErrorMessage = $"Trace file not found: {profileFilePath}"
            };
        }

        try
        {
            // Load the trace and prepare the process list (similar to OpenTraceAsync but standalone)
            var loadResult = await LoadTraceAsync(profileFilePath);
            if (!loadResult.Success)
            {
                return new GetAvailableProcessesResult
                {
                    Success = false,
                    ErrorMessage = loadResult.Result?.ErrorMessage ?? "Failed to load trace file"
                };
            }

            // Extract process information from the loaded trace
            var processes = await ExtractProcessInfoAsync(loadResult.ProfileLoadWindow);
            
            // Apply weight filtering if specified
            if (minWeightPercentage.HasValue)
            {
                if (minWeightPercentage < 0 || minWeightPercentage > 100)
                {
                    return new GetAvailableProcessesResult
                    {
                        Success = false,
                        ErrorMessage = "minWeightPercentage must be nonnegative, between 0 and 100."
                    };
                }
                processes = processes
                    .Where(p => p.WeightPercentage >= minWeightPercentage.Value)
                    .ToArray();
            }
            
            // Apply top N filtering if specified
            if (topCount.HasValue)
            {
                if (topCount < 1) {
                    return new GetAvailableProcessesResult {
                        Success = false,
                        ErrorMessage = "topCount must be a positive integer."
                    };
                }
                // Sort by weight percentage descending and take top N
                if (processes.Length > topCount.Value) {
                    processes = processes
                    .OrderByDescending(p => p.WeightPercentage)
                    .Take(topCount.Value)
                    .ToArray();
                }
            }
            
            // Close the profile load window since we're just extracting process info
            await dispatcher.InvokeAsync(() =>
            {
                loadResult.ProfileLoadWindow?.Close();
            });

            return new GetAvailableProcessesResult
            {
                Success = true,
                Processes = processes
            };
        }
        catch (Exception ex)
        {
            return new GetAvailableProcessesResult
            {
                Success = false,
                ErrorMessage = $"Unexpected error: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Get the current process name from the loaded session
    /// </summary>
    private async Task<string> GetCurrentProcessNameAsync()
    {
        try
        {
            var status = await GetStatusAsync();
            
            // Use the current process information if available
            if (status.CurrentProcess != null)
            {
                if (!string.IsNullOrEmpty(status.CurrentProcess.Name))
                {
                    return status.CurrentProcess.Name;
                }
                
                return $"process-{status.CurrentProcess.ProcessId}";
            }
            
            // Try to extract process name from loaded processes
            if (status.LoadedProcesses?.Length > 0)
            {
                return $"process-{status.LoadedProcesses[0]}";
            }
            
            // If we have a profile path, try to extract a meaningful name from it
            if (!string.IsNullOrEmpty(status.CurrentProfilePath))
            {
                string fileName = Path.GetFileNameWithoutExtension(status.CurrentProfilePath);
                return !string.IsNullOrEmpty(fileName) ? fileName : "trace";
            }
            
            return "unknown";
        }
        catch
        {
            return "unknown";
        }
    }

    /// <summary>
    /// Sanitize a string to be safe for use as a file name
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

    /// <summary>
    /// Extract process information from the ProfileLoadWindow
    /// </summary>
    private async Task<ProcessInfo[]> ExtractProcessInfoAsync(ProfileExplorer.UI.ProfileLoadWindow profileLoadWindow)
    {
        try
        {
            return await dispatcher.InvokeAsync(() =>
            {
                var processListControl = profileLoadWindow.FindName("ProcessList") as System.Windows.Controls.ListView;
                if (processListControl?.ItemsSource != null)
                {
                    try
                    {
                        var processSummaries = processListControl.ItemsSource.Cast<ProcessSummary>().ToList();
                        return processSummaries.Select(p => new ProcessInfo
                        {
                            ProcessId = p.Process.ProcessId,
                            Name = p.Process.Name ?? string.Empty,
                            ImageFileName = p.Process.ImageFileName,
                            CommandLine = p.Process.CommandLine,
                            Weight = p.Weight,
                            WeightPercentage = p.WeightPercentage,
                            Duration = p.Duration
                        }).ToArray();
                    }
                    catch
                    {
                        return Array.Empty<ProcessInfo>();
                    }
                }
                return Array.Empty<ProcessInfo>();
            });
        }
        catch
        {
            return Array.Empty<ProcessInfo>();
        }
    }

    public async Task<GetAvailableFunctionsResult> GetAvailableFunctionsAsync(FunctionFilter? filter = null)
    {
        try
        {
            // Check if a profile is currently loaded
            var status = await GetStatusAsync();
            if (!status.IsProfileLoaded)
            {
                return new GetAvailableFunctionsResult
                {
                    Success = false,
                    ErrorMessage = "No profile is currently loaded. Please open a trace file first using OpenTrace."
                };
            }

            // Extract function information from the currently loaded session
            var functions = await ExtractFunctionInfoAsync();

            if (functions.Length == 0)
            {
                return new GetAvailableFunctionsResult
                {
                    Success = false,
                    ErrorMessage = "No functions found in the currently loaded profile. If you just opened a trace file, " +
                                 "please wait for the profile loading to complete (this can take 20+ seconds for large traces) " +
                                 "before calling GetAvailableFunctions. The loading includes multiple stages: " +
                                 "'Reading Trace' → 'Downloading and loading binaries' → 'Downloading and loading symbols' → " +
                                 "'Processing trace samples' → 'Computing Call Tree'."
                };
            }

            // Apply module filtering if specified
            if (!string.IsNullOrWhiteSpace(filter?.ModuleName))
            {
                functions = functions
                    .Where(f => !string.IsNullOrEmpty(f.ModuleName) && 
                               f.ModuleName.Contains(filter.ModuleName, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                    
                if (functions.Length == 0)
                {
                    // Provide helpful error message with available module suggestions
                    var availableModules = (await ExtractFunctionInfoAsync())
                        .Where(f => !string.IsNullOrEmpty(f.ModuleName))
                        .Select(f => f.ModuleName!)
                        .Distinct()
                        .OrderBy(m => m)
                        .Take(10) // Show first 10 modules as examples
                        .ToArray();
                    
                    var modulesSuggestion = availableModules.Length > 0 
                        ? $" Available modules include: {string.Join(", ", availableModules)}{(availableModules.Length == 10 ? ", ..." : "")}" 
                        : "";
                    
                    return new GetAvailableFunctionsResult
                    {
                        Success = false,
                        ErrorMessage = $"No functions found in module '{filter.ModuleName}'. The module name might not exist, be spelled differently, or not be loaded.{modulesSuggestion}"
                    };
                }
            }

            // Apply self time filtering if specified
            if (filter?.MinSelfTimePercentage.HasValue == true)
            {
                if (filter.MinSelfTimePercentage < 0 || filter.MinSelfTimePercentage > 100)
                {
                    return new GetAvailableFunctionsResult
                    {
                        Success = false,
                        ErrorMessage = "MinSelfTimePercentage must be between 0 and 100."
                    };
                }
                functions = functions
                    .Where(f => f.SelfTimePercentage >= filter.MinSelfTimePercentage.Value)
                    .ToArray();
            }

            // Apply total time filtering if specified
            if (filter?.MinTotalTimePercentage.HasValue == true)
            {
                if (filter.MinTotalTimePercentage < 0 || filter.MinTotalTimePercentage > 100)
                {
                    return new GetAvailableFunctionsResult
                    {
                        Success = false,
                        ErrorMessage = "MinTotalTimePercentage must be between 0 and 100."
                    };
                }
                functions = functions
                    .Where(f => f.TotalTimePercentage >= filter.MinTotalTimePercentage.Value)
                    .ToArray();
            }

            // Apply top N filtering if specified
            if (filter?.TopCount.HasValue == true)
            {
                if (filter.TopCount < 1)
                {
                    return new GetAvailableFunctionsResult
                    {
                        Success = false,
                        ErrorMessage = "TopCount must be a positive integer."
                    };
                }
                // Sort by the chosen metric and take top N
                if (functions.Length > filter.TopCount.Value)
                {
                    functions = (filter.SortBySelfTime)
                        ? functions.OrderByDescending(f => f.SelfTimePercentage).Take(filter.TopCount.Value).ToArray()
                        : functions.OrderByDescending(f => f.TotalTimePercentage).Take(filter.TopCount.Value).ToArray();
                }
            }
            else
            {
                // If no topCount specified, still sort the results
                functions = (filter?.SortBySelfTime ?? true)
                    ? functions.OrderByDescending(f => f.SelfTimePercentage).ToArray()
                    : functions.OrderByDescending(f => f.TotalTimePercentage).ToArray();
            }

            return new GetAvailableFunctionsResult
            {
                Success = true,
                Functions = functions
            };
        }
        catch (Exception ex)
        {
            return new GetAvailableFunctionsResult
            {
                Success = false,
                ErrorMessage = $"Unexpected error: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Extract function information from the currently loaded profile session
    /// </summary>
    private async Task<FunctionInfo[]> ExtractFunctionInfoAsync()
    {
        try
        {
            // Use the shared helper to get the function list control
            var (sectionPanel, functionListControl) = await GetFunctionListControlAsync();
            if (functionListControl?.ItemsSource == null)
            {
                return Array.Empty<FunctionInfo>();
            }

            return await dispatcher.InvokeAsync(() =>
            {
                try
                {
                    var functionInfos = new List<FunctionInfo>();

                    // Iterate through the function list and extract information
                    foreach (var item in functionListControl.ItemsSource)
                    {
                        if (item is ProfileExplorer.UI.IRTextFunctionEx functionEx)
                        {
                            var functionInfo = new FunctionInfo
                            {
                                Name = functionEx.Name ?? string.Empty,
                                FullName = functionEx.ToolTip ?? functionEx.Name ?? string.Empty,
                                ModuleName = ExtractModuleName(functionEx),
                                SelfTimePercentage = Math.Round(functionEx.ExclusivePercentage * 100, 4), // Convert fraction to percentage
                                TotalTimePercentage = Math.Round(functionEx.Percentage * 100, 4), // Convert fraction to percentage
                                SelfTime = functionEx.ExclusiveWeight,
                                TotalTime = functionEx.Weight,
                                SourceFile = ExtractSourceFile(functionEx),
                                HasAssembly = HasAssemblyData(functionEx)
                            };

                            functionInfos.Add(functionInfo);
                        }
                    }

                    // Sort by self time percentage descending (most expensive functions first)
                    return functionInfos
                        .OrderByDescending(f => f.SelfTimePercentage)
                        .ToArray();
                }
                catch
                {
                    return Array.Empty<FunctionInfo>();
                }
            });
        }
        catch
        {
            return Array.Empty<FunctionInfo>();
        }
    }

    /// <summary>
    /// Extract module name from function information
    /// </summary>
    private string ExtractModuleName(ProfileExplorer.UI.IRTextFunctionEx functionEx)
    {
        try
        {
            // Get module name directly from the function
            if (!string.IsNullOrEmpty(functionEx.ModuleName))
            {
                return functionEx.ModuleName;
            }

            // Alternative: extract from full name if it contains module info
            var fullName = functionEx.ToolTip ?? functionEx.Name ?? string.Empty;
            if (fullName.Contains("!"))
            {
                var parts = fullName.Split('!');
                if (parts.Length > 1)
                {
                    return parts[0];
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Extract source file information from function
    /// </summary>
    private string ExtractSourceFile(ProfileExplorer.UI.IRTextFunctionEx functionEx)
    {
        try
        {
            // Try to get source file information from debug info
            var sessionState = mainWindow.SessionState;
            var profileData = sessionState?.ProfileData;
            
            if (profileData?.ModuleDebugInfo != null && functionEx.Function != null)
            {
                // Look through module debug info for this function
                foreach (var debugInfo in profileData.ModuleDebugInfo.Values)
                {
                    var sourceInfo = debugInfo.FindFunctionSourceFilePath(functionEx.Function);
                    if (!sourceInfo.IsUnknown && sourceInfo.HasFilePath)
                    {
                        return sourceInfo.FilePath;
                    }
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Check if function has assembly data available
    /// </summary>
    private bool HasAssemblyData(ProfileExplorer.UI.IRTextFunctionEx functionEx)
    {
        try
        {
            // Check if the function has any sections (indicating code/assembly data)
            return functionEx.Function?.HasSections ?? false;
        }
        catch
        {
            return false;
        }
    }

    public async Task<GetAvailableBinariesResult> GetAvailableBinariesAsync(double? minTimePercentage = null, TimeSpan? minTime = null, int? topCount = null)
    {
        try
        {
            // Check if a profile is currently loaded
            var status = await GetStatusAsync();
            if (!status.IsProfileLoaded)
            {
                return new GetAvailableBinariesResult
                {
                    Success = false,
                    ErrorMessage = "No profile is currently loaded. Please open a trace file first using OpenTrace.",
                    Binaries = Array.Empty<BinaryInfo>()
                };
            }

            // Get module data directly from ProfileData CallTree
            var moduleInfos = await GetModuleDataFromCallTreeAsync();
            if (moduleInfos.Length == 0)
            {
                return new GetAvailableBinariesResult
                {
                    Success = false,
                    ErrorMessage = "No module data available. Ensure the profile has finished loading completely.",
                    Binaries = Array.Empty<BinaryInfo>()
                };
            }

            // Slice ModuleEx into BinaryInfo
            var binaryInfos = moduleInfos.Select(module => {
                var binaryInfo = new BinaryInfo {
                    Name = module.Name ?? string.Empty,
                    FullPath = ExtractModuleFullPath(module.Name),
                    TimePercentage = Math.Round(module.ExclusivePercentage, 4),
                    Time = module.ExclusiveWeight,
                    BinaryFileMissing = module.BinaryFileMissing,
                    DebugFileMissing = module.DebugFileMissing
                };
                
                // Log binary info for diagnostics
                ProfileExplorer.Core.Utilities.DiagnosticLogger.LogDebug($"[MCP-BinaryInfo] Module: {binaryInfo.Name}, BinaryMissing: {binaryInfo.BinaryFileMissing}, DebugMissing: {binaryInfo.DebugFileMissing}");
                
                return binaryInfo;
            }).ToArray();

            // Apply filtering
            var filteredBinaries = binaryInfos;

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

            // Apply top count filter if specified
            if (topCount.HasValue)
            {
                filteredBinaries = filteredBinaries
                    .Take(topCount.Value)
                    .ToArray();
            }

            return new GetAvailableBinariesResult
            {
                Success = true,
                Binaries = filteredBinaries
            };
        }
        catch (Exception ex)
        {
            return new GetAvailableBinariesResult
            {
                Success = false,
                ErrorMessage = $"Error retrieving binaries: {ex.Message}",
                Binaries = Array.Empty<BinaryInfo>()
            };
        }
    }

    /// <summary>
    /// Get module data directly from SessionState.ProfileData (same source as UI)
    /// </summary>
    private async Task<ProfileExplorer.UI.ModuleEx[]> GetModuleDataFromCallTreeAsync()
    {
        try
        {
            return await dispatcher.InvokeAsync(() =>
            {
                try
                {
                    var sessionState = mainWindow.SessionState;
                    var profileData = sessionState?.ProfileData;

                    if (profileData?.ModuleWeights == null || profileData.Modules == null)
                    {
                        return Array.Empty<ProfileExplorer.UI.ModuleEx>();
                    }

                    var moduleInfos = new List<ProfileExplorer.UI.ModuleEx>();

                    // Extract module information directly from ProfileData (same logic as LoadFunctionProfile)
                    foreach (var pair in profileData.ModuleWeights)
                    {
                        var module = profileData.Modules[pair.Key];
                        double weightPercentage = profileData.ScaleModuleWeight(pair.Value);

                        // Get module status for additional info
                        var moduleStatus = profileData.Report?.GetModuleStatus(module.ModuleName);

                        var moduleInfo = new ProfileExplorer.UI.ModuleEx {
                            Name = module.ModuleName,
                            ExclusivePercentage = weightPercentage * 100.0, // convert 0.abcd decimal to ab.cd percent
                            ExclusiveWeight = pair.Value,
                            BinaryFileMissing = moduleStatus != null ? !moduleStatus.HasBinaryLoaded : false,
                            DebugFileMissing = moduleStatus != null ? !moduleStatus.HasDebugInfoLoaded : false,
                        };

                        moduleInfos.Add(moduleInfo);
                    }

                    return moduleInfos.OrderByDescending(m => m.ExclusivePercentage).ToArray();
                }
                catch
                {
                    return Array.Empty<ProfileExplorer.UI.ModuleEx>();
                }
            });
        }
        catch
        {
            return Array.Empty<ProfileExplorer.UI.ModuleEx>();
        }
    }

    /// <summary>
    /// Extract full path for a module if available
    /// </summary>
    private string ExtractModuleFullPath(string moduleName)
    {
        try
        {
            var sessionState = mainWindow.SessionState;
            var profileData = sessionState?.ProfileData;
            
            if (profileData?.Modules != null)
            {
                // Look for a module with matching name
                var module = profileData.Modules.FirstOrDefault(m => 
                    string.Equals(m.Value.ModuleName, moduleName, StringComparison.OrdinalIgnoreCase) ||
                    Path.GetFileName(m.Value.FilePath).Equals(moduleName, StringComparison.OrdinalIgnoreCase));
                
                return module.Value?.FilePath;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }
}
