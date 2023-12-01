// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.using System;
using System;
using System.Collections.Generic;
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
    if(JsonUtils.Deserialize(filePath, out HelpIndex index)) {
      return index;
    }
    return new HelpIndex();
  }
}

public partial class HelpPanel : ToolPanelControl {
  private HelpIndex helpIndex_;

  public HelpPanel() {
    var h = new  HelpIndex();
    h.Topics.Add(new HelpTopic() { Title = "Test", URL = "https://www.google.com" });
    h.PanelTopics.Add(ToolPanelKind.FlameGraph, new HelpTopic() { Title="Flame Graph", URL = @"help\flameGraph.html"});
    var j = JsonUtils.Serialize(h);

    InitializeComponent();
    helpIndex_ = HelpIndex.Deserialize(App.GetHelpIndexFilePath());

    //? TODO: Handle link click to open in external browser.
  }

  public async Task LoadPanelHelp(ToolPanelKind kind) {
    if(helpIndex_.PanelTopics.TryGetValue(kind, out var topic)) {
      await NavigateToURL(App.GetHelpFilePath(topic.URL));
    }
  }

  public static void DisplayPanelHelp(ToolPanelKind kind, ISession session) {
    var panel = new HelpPanel();
    session.DisplayFloatingPanel(panel);
    panel.LoadPanelHelp(kind);
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
}
