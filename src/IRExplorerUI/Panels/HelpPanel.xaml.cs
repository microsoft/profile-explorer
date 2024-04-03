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
using System.Threading;
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
  private SemaphoreSlim loadingTopic_;
  private bool browserInitialized_;
  private Window videoWindow_;

  public HelpPanel() {
    loadingTopic_ = new SemaphoreSlim(1);

    InitializeComponent();
  }

  public override async void OnActivatePanel() {
    base.OnActivatePanel();

    await loadingTopic_.WaitAsync();

    if (currentTopic_ == null) {
      await LoadHomeTopic();
    }

    loadingTopic_.Release();
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
    var panel = await session.ShowPanel(ToolPanelKind.Help) as HelpPanel;

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

  private async Task InitializeBrowser() {
    if (browserInitialized_) {
      return;
    }

    browserInitialized_ = true;

    // Place cache files in the settings directory.
    var webView2Environment = await CoreWebView2Environment.CreateAsync(null, App.GetSettingsDirectoryPath());

    try {
      await Browser.EnsureCoreWebView2Async(webView2Environment);

      if (Browser.CoreWebView2 == null) {
        Trace.WriteLine("Failed to initialize WebView2 control in HelpPanel.");
        return;
      }
    }
    catch (Exception ex) {
      Trace.WriteLine($"Failed to initialize WebView2 control in HelpPanel: {ex.Message}");
      return;
    }

    // Handle a video/image entering full-screen mode
    // by making a new maximized window to which the browser control
    // is moved. When exiting full-screen mode, the browser is moved
    // back to the panel and the window is closed.
    Browser.CoreWebView2.ContainsFullScreenElementChanged += (sender, args) => {
      if (Browser.CoreWebView2.ContainsFullScreenElement) {
        videoWindow_ = new Window();
        videoWindow_.WindowState = WindowState.Maximized;
        videoWindow_.WindowStyle = WindowStyle.None;
        videoWindow_.ResizeMode = ResizeMode.NoResize;
        videoWindow_.ShowInTaskbar = false;
        BrowserHost.Children.Remove(Browser);
        videoWindow_.Content = Browser;
        videoWindow_.Show();
      }
      else {
        if (videoWindow_ != null) {
          videoWindow_.Content = null;
          videoWindow_.Close();
          BrowserHost.Children.Add(Browser);
        }
      }
    };

    Browser.NavigationStarting += (sender, args) => {
      // Update current topic when navigating to a new page.
      foreach (var topic in helpIndex_.Topics) {
        if (args.Uri.Contains(topic.URL.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase)) {
          TopicTextBox.Text = topic.Title;
          currentTopic_ = topic;
          break;
        }
      }
    };

    // Force light mode for the WebView2 control for now
    // screenshots don't look good in dark mode.
    Browser.CoreWebView2.Profile.PreferredColorScheme = CoreWebView2PreferredColorScheme.Light;
  }

  private async Task NavigateToURL(string url) {
    await InitializeBrowser();

    if (Browser.Source != null) {
      Browser.Stop();
    }

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
    if (Browser.CoreWebView2 == null) {
      return;
    }

    Browser.CoreWebView2.NewWindowRequested += Browser_NewWindowRequested;
  }

  private async void Browser_NewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e) {
    try {
      if (e.Uri.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
          e.Uri.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)) {
        if (previewWindow_ != null) {
          // Second click on same image closes current preview.
          bool showPreview = previewUrl_ != e.Uri;
          CloseImagePreview();

          if (!showPreview) {
            e.Handled = true;
            return;
          }
        }

        // Try to parse out an initial popup size from the image name.
        if (!TryExtractImageSize(e.Uri, out int width, out int height)) {
          width = 800;
          height = 600;
        }

        width = (int)Math.Min(width, SystemParameters.PrimaryScreenWidth - 50);
        height = (int)Math.Min(height, SystemParameters.PrimaryScreenHeight - 50);
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

  private Window previewWindow_;
  private string previewUrl_;

  private void ShowImagePreview(string url, int width, int height) {
    var window = new Window();
    window.WindowStyle = WindowStyle.None;
    window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
    window.ResizeMode = ResizeMode.CanResizeWithGrip;
    var browser = new Image();
    browser.Source = new BitmapImage(new Uri(url));
    browser.HorizontalAlignment = HorizontalAlignment.Stretch;
    browser.VerticalAlignment = VerticalAlignment.Stretch;
    browser.Stretch = Stretch.Uniform;
    window.Content = browser;
    window.Width = width;
    window.Height = height;
    window.Owner = Application.Current.MainWindow;

    window.PreviewMouseDown += (sender, args) => {
      CloseImagePreview();
    };

    window.PreviewKeyDown += (sender, args) => {
      switch (args.Key) {
        case Key.Escape:
        case Key.Space:
        case Key.Back: {
          CloseImagePreview();
          break;
        }
      }
    };

    previewWindow_ = window;
    previewUrl_ = url;
    window.Show();
  }

  private void CloseImagePreview() {
    if (previewWindow_ != null) {
      previewWindow_.Close();
      previewWindow_ = null;
      previewUrl_ = null;
    }
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

  private async void ExternaButton_Click(object sender, RoutedEventArgs e) {
    try {
      if (await DownloadHelpIndex()) {
        var psi = new ProcessStartInfo() {
          FileName = App.GetHelpFilePath(helpIndex_.HomeTopic.URL),
          UseShellExecute = true
        };
        Process.Start(psi);
      }
    }
    catch (Exception ex) {
      Trace.WriteLine($"Failed to start external browser: {ex.Message}");
    }
  }
}