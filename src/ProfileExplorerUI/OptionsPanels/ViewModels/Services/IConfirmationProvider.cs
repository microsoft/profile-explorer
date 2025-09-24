// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Threading.Tasks;

namespace ProfileExplorer.UI.Services;

public interface IConfirmationProvider {
  /// <summary>
  /// Shows a yes/no message box.
  /// </summary>
  /// <param name="message">The message to display</param>
  /// <param name="title">Optional title for the dialog</param>
  /// <returns>True if Yes was clicked, false otherwise</returns>
  Task<bool> RequestConfirmation(string message, string? title = null);
}