// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace IRExplorerUI;

public partial class SessionSharingPanel : Popup {
  public const double DefaultHeight = 90;
  public const double MinimumHeight = 90;
  public const double DefaultWidth = 400;
  public const double MinimumWidth = 200;
  private const string DefaultConnectionString =
    @"DefaultEndpointsProtocol=https;AccountName=irexplorer;AccountKey=+2FT4HoGoXXJm+LBaZ3cgGczYa3CMig2KE7ETkD5mgI5mjUL2jMQiZLVR+boTtIhj1XkxXBJFiZ+oH/D3effng==;EndpointSuffix=core.windows.net";
  private const string DefaultContainerName = "share";

  public SessionSharingPanel(Point position, double width, double height,
                             UIElement referenceElement, ISession session) {
    InitializeComponent();

    Session = session;
    var screenPosition = Utils.CoordinatesToScreen(position, referenceElement);
    HorizontalOffset = screenPosition.X;
    VerticalOffset = screenPosition.Y;
    Width = width;
    Height = height;
  }

  public ISession Session { get; set; }
  public string SharingLink { get; set; }

  private static SessionSharing CreateSharingClient() {
    return new SessionSharing(DefaultConnectionString);
  }

  private async void ShareButton_Click(object sender, RoutedEventArgs e) {
    SharingProgressBar.Visibility = Visibility.Visible;

    string sharingLink = await UploadSessionFile();
    DisplaySharingLink(sharingLink);

    SharingProgressBar.Visibility = Visibility.Hidden;
  }

  private async Task<string> UploadSessionFile() {
    try {
      string filePath = Path.GetTempFileName();

      if (await Session.SaveSessionDocument(filePath)) {
        var sharingClient = CreateSharingClient();
        var result = await sharingClient.UploadSession(filePath, DefaultContainerName);
        return sharingClient.ToSharingLink(result);
      }
    }
    catch (Exception ex) {
      using var centerForm = new DialogCenteringHelper(this);
      MessageBox.Show($"Failed to upload session file: {ex.Message}", "IR Explorer",
                      MessageBoxButton.OK, MessageBoxImage.Exclamation);
    }

    return null;
  }

  private void DisplaySharingLink(string sharingLink) {
    SharingLink = sharingLink;
    DataContext = this;
    SharingLinkTextBox.SelectAll();
    SharingLinkTextBox.Focus();
    Keyboard.Focus(SharingLinkTextBox);
  }

  private async void OpenButton_Click(object sender, RoutedEventArgs e) {
    SharingProgressBar.Visibility = Visibility.Visible;

    string sharingLink = SharingLinkTextBox.Text;
    string filePath = await DownloadSessionFile(sharingLink);

    if (filePath != null) {
      await Session.OpenSessionDocument(filePath);
      IsOpen = false;
    }
  }

  private async Task<string> DownloadSessionFile(string sharingLink) {
    try {
      var sharingClient = CreateSharingClient();
      var sharedSession = sharingClient.FromSharingLink(sharingLink);

      if (sharedSession != null) {
        return await sharingClient.DownloadSession(sharedSession);
      }
    }
    catch (Exception ex) {
      MessageBox.Show($"Failed to download and open session file: {ex.Message}", "IR Explorer",
                      MessageBoxButton.OK, MessageBoxImage.Exclamation);
    }

    return null;
  }
}
