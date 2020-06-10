using System;
using System.ComponentModel.Design;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using System.Diagnostics;
using System.IO;
using Grpc.Core;
using System.Collections.Generic;
using EnvDTE90;

namespace IRExplorerExtension
{
    /// <summary>
    /// Command handler
    /// </summary>
    internal sealed class AttachCommand : CommandBase
    {
        /// <summary>
        /// Command ID.
        /// </summary>
        public const int CommandId = 0x0106;

        /// <summary>
        /// Command menu group (command set GUID).
        /// </summary>
        public static readonly Guid CommandSet = new Guid("9885ad8d-69e0-4ec4-8324-b9fd109ebdcd");

        /// <summary>
        /// VS Package that provides this command, not null.
        /// </summary>
        private static AsyncPackage Package;

        /// <summary>
        /// Initializes a new instance of the <see cref="AttachCommand"/> class.
        /// Adds our command handlers for menu (commands must exist in the command table file)
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        /// <param name="commandService">Command service to add command to, not null.</param>
        private AttachCommand(AsyncPackage package, OleMenuCommandService commandService) : base(package)
        {
            Package = package ?? throw new ArgumentNullException(nameof(package));
            commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));

            var menuCommandID = new CommandID(CommandSet, CommandId);
            var menuItem = new OleMenuCommand(this.Execute, menuCommandID);
            menuItem.BeforeQueryStatus += MenuItem_BeforeQueryStatus;
            commandService.AddCommand(menuItem);
        }

        private void MenuItem_BeforeQueryStatus(object sender, EventArgs e)
        {
            if (sender is OleMenuCommand command)
            {
                command.Enabled = ClientInstance.IsEnabled;
            }
        }

        /// <summary>
        /// Gets the instance of the command.
        /// </summary>
        public static AttachCommand Instance
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets the service provider from the owner package.
        /// </summary>
        private Microsoft.VisualStudio.Shell.IAsyncServiceProvider ServiceProvider
        {
            get
            {
                return Package;
            }
        }

        /// <summary>
        /// Initializes the singleton instance of the command.
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        public static async Task InitializeAsync(AsyncPackage package)
        {
            // Switch to the main thread - the call to AddCommand in Command1's constructor requires
            // the UI thread.
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            OleMenuCommandService commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            Instance = new AttachCommand(package, commandService);
        }

        /// <summary>
        /// This function is the callback used to execute the command when the menu item is clicked.
        /// See the constructor to see how the menu item is associated with this function using
        /// OleMenuCommandService service and MenuCommand class.
        /// </summary>
        /// <param name="sender">Event sender.</param>
        /// <param name="e">Event args.</param>
        /// 

        private async void Execute(object sender, EventArgs e)
        {
            try
            {
                Logger.Log("Try attach debugger to compiler instance...");

                if (DebuggerInstance.InBreakMode)
                {
                    // Debugger already started, try to connect.
                    if(ClientInstance.SetupDebugSession())
                    {
                        ClientInstance.UpdateIR();
                    }
                    return;
                }

                var list = DebuggerInstance.GetRunningProcesses();
                if (list == null) return;

                var candidates = new List<Process3>();
                int clInstances = 0;
                int linkInstances = 0;
                Process3 linkInstance = null;
                DateTime linkStartTime = DateTime.MinValue;

                foreach (var process in list)
                {
                    if (process.IsBeingDebugged)
                    {
                        continue;
                    }

                    if (process.Name.Contains("cl.exe"))
                    {
                        candidates.Add(process);
                        clInstances++;
                    }
                    else if (process.Name.Contains("link.exe"))
                    {
                        var processInstance = System.Diagnostics.Process.GetProcessById(process.ProcessID);
                        
                        if(linkInstance == null)
                        {
                            linkInstance = process;
                            linkStartTime = processInstance.StartTime;
                            candidates.Add(process);
                        }
                        else if(processInstance.StartTime > linkStartTime) {
                            candidates.Remove(linkInstance);
                            linkInstance = process;
                            linkStartTime = processInstance.StartTime;
                            candidates.Add(process);
                        }

                        linkInstances++;
                    }
                }

                if (candidates.Count == 0)
                {
                    Logger.Log("No running compiler instance found");
                    return;
                }
                else if (candidates.Count == 1)
                {
                    candidates[0].Attach();
                    Logger.Log("Debugger attached");
                }
                else if(linkInstances == 2 && clInstances == 0)
                {
                    linkInstance.Attach();
                    Logger.Log("Debugger attached");
                }
                else
                {
                    VsShellUtilities.ShowMessageBox(Package,
                        "There are more than two instances of CL/LINK running, use the \"Debug > Attach to Process\" dialog instead",
                        "IR Explorer extension",
                        OLEMSGICON.OLEMSGICON_WARNING,
                        OLEMSGBUTTON.OLEMSGBUTTON_OK,
                        OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                }
            }
            catch(Exception ex)
            {
                Logger.LogException(ex, "Failed to attach to process");
            }
        }
    }
}
