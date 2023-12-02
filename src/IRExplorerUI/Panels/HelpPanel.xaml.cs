// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.using System;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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

  public HelpIndex() {
    Topics = new List<HelpTopic>();
    PanelTopics = new Dictionary<ToolPanelKind, HelpTopic>();
  }

  public static HelpIndex Deserialize(string filePath) {
    if (JsonUtils.DeserializeFromFile(filePath, out HelpIndex index)) {
      return index;
    }

    return new HelpIndex();
  }
}

public partial class HelpPanel : ToolPanelControl {
  private HelpIndex helpIndex_;

  public HelpPanel() {
    InitializeComponent();
    helpIndex_ = HelpIndex.Deserialize(App.GetHelpIndexFilePath());
    TopicsTree.ItemsSource = helpIndex_.Topics;
  }

  public async Task LoadPanelHelp(ToolPanelKind kind) {
    if (helpIndex_.PanelTopics.TryGetValue(kind, out var topic)) {
      await NavigateToTopic(topic);
    }
  }

  public static async Task DisplayPanelHelp(ToolPanelKind kind, ISession session) {
    var panel = session.FindPanel(ToolPanelKind.Help) as HelpPanel;

    if (panel == null) {
      panel = new HelpPanel();
      session.DisplayFloatingPanel(panel);
    }

    await panel.LoadPanelHelp(kind);
  }

  private async Task NavigateToTopic(HelpTopic topic) {
    if (!string.IsNullOrEmpty(topic.URL)) {
      TopicTextBox.Text = topic.Title;
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

  private void Browser_NewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e) {
    try {
      e.Handled = true;
      var psi = new ProcessStartInfo() {
        FileName = e.Uri,
        UseShellExecute = true
      };
      Process.Start(psi);
    }
    catch (Exception ex) {
      Trace.WriteLine($"Failed to start external browser: {ex.Message}");
    }
  }

  private void ZoomInButton_Click(object sender, RoutedEventArgs e) {
    Browser.ZoomFactor = Math.Min(4, Browser.ZoomFactor + 0.1);
  }

  private void ZoomOutButton_Click(object sender, RoutedEventArgs e) {
    Browser.ZoomFactor = Math.Max(0.5, Browser.ZoomFactor - 0.1);
  }
}
