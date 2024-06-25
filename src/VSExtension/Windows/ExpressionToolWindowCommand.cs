// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.ComponentModel.Design;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;

namespace IRExplorerExtension.Windows;

/// <summary>
///   Command handler
/// </summary>
sealed class ExpressionToolWindowCommand {
  /// <summary>
  ///   Command ID.
  /// </summary>
  public const int CommandId = 4177;
  /// <summary>
  ///   Command menu group (command set GUID).
  /// </summary>
  public static readonly Guid CommandSet = new("9885ad8d-69e0-4ec4-8324-b9fd109ebdcd");
  /// <summary>
  ///   VS Package that provides this command, not null.
  /// </summary>
  private readonly AsyncPackage package;

  /// <summary>
  ///   Initializes a new instance of the <see cref="ExpressionToolWindowCommand" /> class.
  ///   Adds our command handlers for menu (commands must exist in the command table file)
  /// </summary>
  /// <param name="package">Owner package, not null.</param>
  /// <param name="commandService">Command service to add command to, not null.</param>
  private ExpressionToolWindowCommand(AsyncPackage package, OleMenuCommandService commandService) {
    this.package = package ?? throw new ArgumentNullException(nameof(package));
    commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));

    var menuCommandID = new CommandID(CommandSet, CommandId);
    var menuItem = new MenuCommand(Execute, menuCommandID);
    commandService.AddCommand(menuItem);
  }

  /// <summary>
  ///   Gets the instance of the command.
  /// </summary>
  public static ExpressionToolWindowCommand Instance {
    get;
    private set;
  }
  /// <summary>
  ///   Gets the service provider from the owner package.
  /// </summary>
  private IAsyncServiceProvider ServiceProvider => package;

  /// <summary>
  ///   Initializes the singleton instance of the command.
  /// </summary>
  /// <param name="package">Owner package, not null.</param>
  public static async Task InitializeAsync(AsyncPackage package) {
    // Switch to the main thread - the call to AddCommand in ExpressionToolWindowCommand's constructor requires
    // the UI thread.
    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

    var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
    Instance = new ExpressionToolWindowCommand(package, commandService);
  }

  /// <summary>
  ///   Shows the tool window when the menu item is clicked.
  /// </summary>
  /// <param name="sender">The event sender.</param>
  /// <param name="e">The event args.</param>
  private void Execute(object sender, EventArgs e) {
    package.JoinableTaskFactory.RunAsync(async delegate {
      var window = await package.ShowToolWindowAsync(typeof(ExpressionToolWindow), 0, true, package.DisposalToken);

      if (null == window || null == window.Frame) {
        throw new NotSupportedException("Cannot create tool window");
      }
    });
  }
}
