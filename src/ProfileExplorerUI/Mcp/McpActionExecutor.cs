// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Threading.Tasks;
using System.Windows.Threading;
using ProfileExplorer.Mcp;

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

        public async Task<bool> OpenLoadProfileDialogAsync()
        {
            return await dispatcher.InvokeAsync(() =>
            {
                // TODO: Implement actual UI automation
                // AppCommand.LoadProfile.Execute(null, mainWindow);
                return true;
            });
        }

        public async Task<bool> SetProfileFilePathAsync(string profileFilePath)
        {
            return await dispatcher.InvokeAsync(() =>
            {
                // TODO: Implement actual profile file path setting
                // This should open ProfileLoadWindow and set the ProfileFilePath property
                return true;
            });
        }

        public async Task<bool> SelectProcessesAsync(int[] processIds)
        {
            return await dispatcher.InvokeAsync(() =>
            {
                // TODO: Implement actual process selection
                // This should select processes in the ProcessList control
                return true;
            });
        }

        public async Task<bool> ExecuteProfileLoadAsync(bool useBackendDirectly = true)
        {
            return await dispatcher.InvokeAsync(() =>
            {
                // TODO: Implement actual profile loading
                if (useBackendDirectly)
                {
                    // Use MainWindow.LoadProfileData directly
                }
                else
                {
                    // Simulate LoadButton_Click
                }
                return true;
            });
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
