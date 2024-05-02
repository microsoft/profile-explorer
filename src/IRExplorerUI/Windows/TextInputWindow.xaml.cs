using System.Windows;
using System.Windows.Input;

namespace IRExplorerUI.Windows;

public partial class TextInputWindow : Window {
  public TextInputWindow() {
    InitializeComponent();
    this.Loaded += (sender, args) => {
      AutocompleteBox.Focus();
    };

    this.PreviewKeyDown += (sender, args) => {
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
    this.Owner = owner;
  }

  public string InputTitle { get; set; }
  public string InputPrompt { get; set; }
  public string AcceptButtonLabel { get; set; }
  public string CancelButtonLabel { get; set; }

  public bool Show(out string inputText, bool showNextToMouseCursor) {
    DataContext = this;

    if (showNextToMouseCursor) {
      Point position = Mouse.GetPosition(App.Current.MainWindow);
      Left = position.X + SystemParameters.CursorWidth;
      Top = position.Y + SystemParameters.CursorHeight;
    }
    else if (Owner != null) {
      WindowStartupLocation = WindowStartupLocation.CenterOwner;
    }
    else {
      WindowStartupLocation = WindowStartupLocation.CenterScreen;
    }

    var result = ShowDialog();

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

