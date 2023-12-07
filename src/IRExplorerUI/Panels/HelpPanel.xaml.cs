// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.using System;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using IRExplorerUI.Controls;
using Microsoft.Web.WebView2.Core;

namespace IRExplorerUI.Panels;

public class HelpTopic {
  public string Title { get; set; }
  public string URL { get; set; }
  public List<HelpTopic> SubTopics { get; set; }
}

public class HelpIndex {
  public List<HelpTopic> Topics { get; set; }
  public Dictionary<ToolPanelKind, HelpTopic> PanelTopics { get; set; }
  public HelpTopic HomeTopic { get; set; }

  public HelpIndex() {
    Topics = new List<HelpTopic>();
    PanelTopics = new Dictionary<ToolPanelKind, HelpTopic>();
  }

  public static HelpIndex DeserializeFromFile(string filePath) {
    if (JsonUtils.DeserializeFromFile(filePath, out HelpIndex index)) {
      return index;
    }

    return new HelpIndex();
  }

  public static HelpIndex Deserialize(string text) {
    if (JsonUtils.Deserialize(text, out HelpIndex index)) {
      return index;
    }

    return new HelpIndex();
  }
}

public partial class HelpPanel : ToolPanelControl {
  private HelpIndex helpIndex_;
  private HelpTopic currentTopic_;

  public HelpPanel() {
    InitializeComponent();
  }

  public override async void OnActivatePanel() {
    base.OnActivatePanel();

    if (currentTopic_ == null) {
      await LoadHomeTopic();
    }
  }

  private async Task<bool> DownloadHelpIndex() {
#if DEBUG
    // Open local index file in debug builds.
    helpIndex_ = HelpIndex.DeserializeFromFile(App.GetHelpIndexFilePath());

    if (helpIndex_ != null) {
      TopicsTree.ItemsSource = helpIndex_.Topics;
    }
#else
    // Download index file from web location.
    if (helpIndex_ != null) {
      return true;
    }

    try {
      using var client = new HttpClient();
      using var response = await client.GetAsync(App.GetHelpIndexFilePath());

      if (response.IsSuccessStatusCode) {
        var contents = await response.Content.ReadAsStringAsync();
        helpIndex_ = HelpIndex.Deserialize(contents);

        if (helpIndex_ != null) {
          TopicsTree.ItemsSource = helpIndex_.Topics;
        }
      }
      else {
        Trace.WriteLine($"Failed to download file: {response.ReasonPhrase}");
      }
    }
    catch (Exception ex) {
      Trace.WriteLine($"Failed to download file: {ex.Message}");
    }
#endif

    return helpIndex_ != null;
  }

  public async Task LoadHomeTopic() {
    if (await DownloadHelpIndex()) {
      await NavigateToTopic(helpIndex_.HomeTopic);
    }
  }

  public async Task LoadPanelHelp(ToolPanelKind kind) {
    if (await DownloadHelpIndex()) {
      if (helpIndex_.PanelTopics.TryGetValue(kind, out var topic)) {
        await NavigateToTopic(topic);
      }
    }
  }

  public static async Task DisplayPanelHelp(ToolPanelKind kind, ISession session) {
    await session.ShowPanel(ToolPanelKind.Help);
    var panel = session.FindPanel(ToolPanelKind.Help) as HelpPanel;

    if (panel != null) {
      await panel.LoadPanelHelp(kind);
    }
  }

  private async Task NavigateToTopic(HelpTopic topic) {
    if (topic != null && !string.IsNullOrEmpty(topic.URL)) {
      TopicTextBox.Text = topic.Title;
      currentTopic_ = topic;
      await NavigateToURL(App.GetHelpFilePath(topic.URL));
    }
  }

  private async Task NavigateToURL(string url) {
    // Force light mode for the WebView2 control for now.
    await Browser.EnsureCoreWebView2Async();
    Browser.CoreWebView2.Profile.PreferredColorScheme = CoreWebView2PreferredColorScheme.Light;
    Browser.Source = new Uri(url);
  }

  public override ToolPanelKind PanelKind => ToolPanelKind.Help;

  private void ToolBar_Loaded(object sender, RoutedEventArgs e) {
    Utils.PatchToolbarStyle(sender as ToolBar);
  }

  private async void TopicsTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e) {
    var treeView = sender as TreeView;
    var selectedTopic = treeView.SelectedItem as HelpTopic;

    if (selectedTopic != null) {
      await NavigateToTopic(selectedTopic);
    }

    TopicsTreePopup.IsOpen = false;
  }

  private void TopicsTextbox_PreviewMouseDown(object sender, MouseButtonEventArgs e) {
    if (TopicsTreePopup.IsOpen) {
      TopicsTreePopup.IsOpen = false;
      return;
    }

    TopicsTreePopup.Placement = System.Windows.Controls.Primitives.PlacementMode.Relative;
    TopicsTreePopup.PlacementTarget = TopicTextBox;
    TopicsTreePopup.VerticalOffset = TopicTextBox.ActualHeight;
    TopicsTreePopup.Height = TopicsTree.Height;
    TopicsTreePopup.Width = TopicTextBox.Width;
    TopicsTreePopup.IsOpen = true;
  }

  private void TopicsTextbox_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) {
    TopicsTreePopup.IsOpen = false;
  }

  private void Browser_CoreWebView2InitializationCompleted(object sender,
                                                           CoreWebView2InitializationCompletedEventArgs e) {
    Browser.CoreWebView2.NewWindowRequested += Browser_NewWindowRequested;
  }

  private async void Browser_NewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e) {
    try {
      if (e.Uri.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
          e.Uri.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)) {
        // Try to parse out an initial popup size from the image name.
        if (!TryExtractImageSize(e.Uri, out int width, out int height)) {
          width = 800;
          height = 600;
        }

        width = (int)Math.Min(width, System.Windows.SystemParameters.PrimaryScreenWidth - 50);
        height = (int)Math.Min(height, System.Windows.SystemParameters.PrimaryScreenHeight - 50);
        ShowImagePreview(e.Uri, width, height);
      }
      else {
        // Open in new external browser window.
        var psi = new ProcessStartInfo() {
          FileName = e.Uri,
          UseShellExecute = true
        };
        Process.Start(psi);
      }
      e.Handled = true;
    }
    catch (Exception ex) {
      Trace.WriteLine($"Failed to start external browser: {ex.Message}");
    }
  }

  private static void ShowImagePreview(string url, int width, int height) {
    var window = new Window();
    window.WindowStyle = WindowStyle.SingleBorderWindow;
    window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
    window.ResizeMode = ResizeMode.CanResizeWithGrip;
    var browser = new Microsoft.Web.WebView2.Wpf.WebView2();
    window.Content = browser;
    window.Width = width;
    window.Height = height;
    window.Owner = App.Current.MainWindow;
    window.Title = "Image Preview";
    browser.Source = new Uri(url);
    window.Show();
  }

  private bool TryExtractImageSize(string url, out int width, out int height) {
    // Try parse file_widthxheight.extension, like file_800x600.gif.
    width = height = 0;
    int start = url.LastIndexOf('_');
    int end = url.LastIndexOf('.');

    if (start == -1 || end <= start) {
      return false;
    }

    int middle = url.IndexOf('x', start);

    if (middle == -1) {
      return false;
    }

    return int.TryParse(url.Substring(start + 1, middle - start - 1), out width) &&
           int.TryParse(url.Substring(middle + 1, end - middle - 1), out height);
  }

  private void ZoomInButton_Click(object sender, RoutedEventArgs e) {
    Browser.ZoomFactor = Math.Min(4, Browser.ZoomFactor + 0.1);
  }

  private void ZoomOutButton_Click(object sender, RoutedEventArgs e) {
    Browser.ZoomFactor = Math.Max(0.5, Browser.ZoomFactor - 0.1);
  }

  private async void HomeButton_Click(object sender, RoutedEventArgs e) {
    await LoadHomeTopic();
  }

  private async void BackButton_Click(object sender, RoutedEventArgs e) {
    Browser.GoBack();
  }
}