// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using ProfileExplorer.UI.Controls;

namespace ProfileExplorer.UI.Services;

public class DialogService : IDialogService {
  private readonly Window? _parentWindow;

  public DialogService(Window? parentWindow = null) {
    _parentWindow = parentWindow;
  }

  public Task<string?> ShowOpenFolderDialogAsync(string title, string? initialDirectory = null) {
    string? result = null;

    // Execute on UI thread
    Application.Current.Dispatcher.Invoke(() => {
      using var centerForm = _parentWindow != null ? new DialogCenteringHelper(_parentWindow) : null;
      var dialog = new OpenFolderDialog {
        Title = title
      };

      if (!string.IsNullOrEmpty(initialDirectory)) {
        dialog.InitialDirectory = initialDirectory;
      }

      if (dialog.ShowDialog() == true) {
        result = dialog.FolderName;
      }
    });

    return Task.FromResult(result);
  }

  public Task<bool> ShowYesNoMessageBoxAsync(string message, string? title = null) {
    bool result = false;

    Application.Current.Dispatcher.Invoke(() => {
      var messageBoxResult = MessageBox.Show(
        _parentWindow,
        message,
        title ?? "Confirmation",
        MessageBoxButton.YesNo,
        MessageBoxImage.Question
      );
      result = messageBoxResult == MessageBoxResult.Yes;
    });

    return Task.FromResult(result);
  }

  public Task ShowMessageBoxAsync(string message, string? title = null) {
    Application.Current.Dispatcher.Invoke(() => {
      MessageBox.Show(
        _parentWindow,
        message,
        title ?? "Information",
        MessageBoxButton.OK,
        MessageBoxImage.Information
      );
    });

    return Task.CompletedTask;
  }
}