// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using ProfileExplorer.Mcp;
using ProfileExplorer.Core.Profile.Data;

namespace ProfileExplorer.UI.Mcp
{
    /// <summary>
    /// Implementation of IMcpActionExecutor that provides UI automation for Profile Explorer.
    /// This is currently a dummy implementation that can be filled in later.
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

        public async Task<bool> OpenTraceAsync(string profileFilePath, int processId)
        {
            // Execute the command in the background since ShowDialog() blocks
            var task = Task.Run(() =>
            {
                dispatcher.Invoke(() => AppCommand.LoadProfile.Execute(null, mainWindow));
            });
            
            // Wait for the dialog to be created and shown (with timeout)
            var profileLoadWindow = await WaitForWindowAsync<ProfileExplorer.UI.ProfileLoadWindow>(TimeSpan.FromSeconds(5));
            if (profileLoadWindow == null)
            {
                return false;
            }
            
            try
            {
                // Step 1: Set the profile file path
                await dispatcher.InvokeAsync(() => 
                {
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
                    return false; // Timeout or error loading process list
                }
                
                // Step 4: Additional verification that ItemsSource is actually populated
                // Sometimes there can be a brief delay between our wait completing and ItemsSource being set
                var verificationResult = await WaitForItemsSourceAsync(profileLoadWindow, TimeSpan.FromSeconds(10));
                if (!verificationResult)
                {
                    return false; // ItemsSource still not available after additional wait
                }
                
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
                    return false; // Process not found or ItemsSource still null
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
                
                return true;
            }
            catch (Exception)
            {
                return false;
            }
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

        public async Task<ProfilerStatus> GetStatusAsync()
        {
            return await dispatcher.InvokeAsync(() =>
            {
                // TODO: Implement actual status retrieval
                return new ProfilerStatus
                {
                    IsProfileLoaded = false,
                    CurrentProfilePath = null,
                    LoadedProcesses = Array.Empty<int>(),
                    ActiveFilters = Array.Empty<string>(),
                    LastUpdated = DateTime.UtcNow
                };
            });
        }
    }
}
