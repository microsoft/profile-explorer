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
    internal sealed class EnableCommand : CommandBase
    {
        /// <summary>
        /// Command ID.
        /// </summary>
        public const int CommandId = 0x0108;

        /// <summary>
        /// Command menu group (command set GUID).
        /// </summary>
        public static readonly Guid CommandSet = new Guid("9885ad8d-69e0-4ec4-8324-b9fd109ebdcd");

        /// <summary>
        /// VS Package that provides this command, not null.
        /// </summary>
        private static AsyncPackage Package;

        /// <summary>
        /// Initializes a new instance of the <see cref="EnableCommand"/> class.
        /// Adds our command handlers for menu (commands must exist in the command table file)
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        /// <param name="commandService">Command service to add command to, not null.</param>
        private EnableCommand(AsyncPackage package, OleMenuCommandService commandService) : base(package)
        {
            Package = package ?? throw new ArgumentNullException(nameof(package));
            commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));

            var menuCommandID = new CommandID(CommandSet, CommandId);
            //var menuItem = new MenuCommand(this.Execute, menuCommandID);
            //commandService.AddCommand(menuItem);

            var oleMenuItem = new OleMenuCommand(this.Execute, menuCommandID);
            oleMenuItem.BeforeQueryStatus += OleMenuItem_BeforeQueryStatus;
            commandService.AddCommand(oleMenuItem);
        }

        private void OleMenuItem_BeforeQueryStatus(object sender, EventArgs e)
        {
            if (sender is OleMenuCommand command) {
                if(ClientInstance.IsEnabled)
                {
                    command.Text = "Disable extension";
                }
                else
                {
                    command.Text = "Enable extension";
                }
            }
        }

        /// <summary>
        /// Gets the instance of the command.
        /// </summary>
        public static EnableCommand Instance
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
            Instance = new EnableCommand(package, commandService);
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
            ClientInstance.ToggleEnabled();
        }
    }
}
