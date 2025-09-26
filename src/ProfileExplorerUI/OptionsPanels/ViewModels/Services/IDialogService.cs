// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Threading.Tasks;

namespace ProfileExplorer.UI.Services;

public interface IDialogService {
  /// <summary>
  /// Shows an open folder dialog and returns the selected folder path.
  /// </summary>
  /// <param name="title">The dialog title</param>
  /// <param name="initialDirectory">Optional initial directory to start in</param>
  /// <returns>The selected folder path, or null if cancelled</returns>
  Task<string?> ShowOpenFolderDialogAsync(string title, string? initialDirectory = null);

  /// <summary>
  /// Shows a yes/no message box.
  /// </summary>
  /// <param name="message">The message to display</param>
  /// <param name="title">Optional title for the dialog</param>
  /// <returns>True if Yes was clicked, false otherwise</returns>
  Task<bool> ShowYesNoMessageBoxAsync(string message, string? title = null);

  /// <summary>
  /// Shows an information message box.
  /// </summary>
  /// <param name="message">The message to display</param>
  /// <param name="title">Optional title for the dialog</param>
  Task ShowMessageBoxAsync(string message, string? title = null);

  /// <summary>
  /// Shows a text input dialog.
  /// </summary>
  /// <param name="title">The dialog title</param>
  /// <param name="prompt">The prompt message to display</param>
  /// <param name="defaultValue">Optional default value for the input</param>
  /// <returns>The entered text, or null if cancelled</returns>
  Task<string?> ShowTextInputDialogAsync(string title, string prompt, string? defaultValue = null);
}