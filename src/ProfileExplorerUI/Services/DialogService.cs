// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using ProfileExplorer.UI.Controls;
using ProfileExplorer.UI.Windows;

namespace ProfileExplorer.UI.Services;

public class DialogService : IDialogService {
  private readonly FrameworkElement? _parentElement;

  public DialogService(FrameworkElement? parentElement = null) {
    _parentElement = parentElement;
  }

  public Task<string?> ShowOpenFolderDialogAsync(string title, string? initialDirectory = null) {
    string? result = null;

    // Execute on UI thread
    Application.Current.Dispatcher.Invoke(() => {
      using var centerForm = _parentElement != null ? new DialogCenteringHelper(_parentElement) : null;
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
      var parentWindow = _parentElement != null ? Window.GetWindow(_parentElement) : null;
      var messageBoxResult = MessageBox.Show(
        parentWindow,
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
      var parentWindow = _parentElement != null ? Window.GetWindow(_parentElement) : null;
      MessageBox.Show(
        parentWindow,
        message,
        title ?? "Information",
        MessageBoxButton.OK,
        MessageBoxImage.Information
      );
    });

    return Task.CompletedTask;
  }

  public Task<string?> ShowTextInputDialogAsync(string title, string prompt, string? defaultValue = null) {
    string? result = null;

    Application.Current.Dispatcher.Invoke(() => {
      var parentWindow = _parentElement != null ? Window.GetWindow(_parentElement) : null;
      var inputWindow = new TextInputWindow(
        title,
        prompt,
        "OK",
        "Cancel",
        parentWindow
      );
      
      if (!string.IsNullOrEmpty(defaultValue)) {
        // Set default value if provided
        inputWindow.AutocompleteBox.Text = defaultValue;
      }

      if (inputWindow.Show(out string inputText, false)) {
        result = inputText;
      }
    });

    return Task.FromResult(result);
  }
}