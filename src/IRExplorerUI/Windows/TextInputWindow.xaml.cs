// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System.Windows;
using System.Windows.Input;

namespace IRExplorerUI.Windows;

public partial class TextInputWindow : Window {
  public TextInputWindow() {
    InitializeComponent();
    Loaded += (sender, args) => {
      AutocompleteBox.Focus();
    };

    PreviewKeyDown += (sender, args) => {
      if (args.Key == Key.Escape) {
        Cancel();
      }
      else if (args.Key == Key.Return) {
        Accept();
      }
    };
  }

  public TextInputWindow(string title, string prompt,
                         string acceptButtonLabel, string cancelButtonLabel,
                         Window owner = null) : this() {
    Title = title;
    InputPrompt = prompt;
    AcceptButtonLabel = acceptButtonLabel;
    CancelButtonLabel = cancelButtonLabel;
    Owner = owner;
  }

  public double WindowScaling => App.Settings.GeneralSettings.WindowScaling;
  public string InputTitle { get; set; }
  public string InputPrompt { get; set; }
  public string AcceptButtonLabel { get; set; }
  public string CancelButtonLabel { get; set; }

  public bool Show(out string inputText, bool showNextToMouseCursor) {
    DataContext = this;

    if (showNextToMouseCursor) {
      var position = Mouse.GetPosition(Application.Current.MainWindow);
      Left = position.X + SystemParameters.CursorWidth;
      Top = position.Y + SystemParameters.CursorHeight;
    }
    else if (Owner != null) {
      WindowStartupLocation = WindowStartupLocation.CenterOwner;
    }
    else {
      WindowStartupLocation = WindowStartupLocation.CenterScreen;
    }

    bool? result = ShowDialog();

    if (result == true) {
      inputText = AutocompleteBox.Text.Trim();
      return !string.IsNullOrEmpty(inputText);
    }

    inputText = null;
    return false;
  }

  private void AcceptButton_Click(object sender, RoutedEventArgs e) {
    Accept();
  }

  private void Accept() {
    if (!string.IsNullOrEmpty(AutocompleteBox.Text.Trim())) {
      DialogResult = true;
      Close();
    }
  }

  private void CancelButton_Click(object sender, RoutedEventArgs e) {
    Cancel();
  }

  private void Cancel() {
    DialogResult = false;
    Close();
  }
}
