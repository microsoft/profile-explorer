// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using ProfileExplorer.UI.Controls;
using ProfileExplorer.UI.Windows;

namespace ProfileExplorer.UI.Services;

public class DialogConfirmationProvider : IConfirmationProvider {
  private readonly FrameworkElement parentElement_;

  public DialogConfirmationProvider(FrameworkElement parentElement) {
    parentElement_ = parentElement;
  }
  public Task<bool> RequestConfirmation(string message, string? title = null) {
    bool result = false;

    Application.Current.Dispatcher.Invoke(() => {
      var parentWindow = parentElement_ != null ? Window.GetWindow(parentElement_) : null;
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
}