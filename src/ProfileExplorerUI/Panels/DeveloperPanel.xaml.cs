// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ProfileExplorer.Core;
using ProfileExplorer.Core.IR;
using ProfileExplorer.Core.Utilities;

namespace ProfileExplorer.UI;

public partial class DeveloperPanel : ToolPanelControl {
  private DispatcherTimer logFileTimer_;
  private string previousLogFileText_;

  public DeveloperPanel() {
    InitializeComponent();
  }

  private async void Button_Click(object sender, RoutedEventArgs e) {
    ErrorList.Visibility = Visibility.Collapsed;
    TextView.Visibility = Visibility.Visible;

    if (Session.CurrentDocument != null) {
      var printer = new IRPrinter(Session.CurrentDocument.Function);
      await TextView.SetText(printer.Print(),
                             Utils.LoadSyntaxHighlightingFile(App.GetInternalIRSyntaxHighlightingFilePath()));
    }
    else {
      await TextView.SetText("No document opened");
    }
  }

  private void ToolBar_Loaded(object sender, RoutedEventArgs e) {
    Utils.PatchToolbarStyle(sender as ToolBar);
  }

  private async void Button_Click_1(object sender, RoutedEventArgs e) {
    ErrorList.Visibility = Visibility.Visible;
    TextView.Visibility = Visibility.Collapsed;

    if (Session.CurrentDocument != null) {
      var section = Session.CurrentDocument.Section;
      var loader = Session.SessionState.FindLoadedDocument(section).Loader;
      var loadedSection = loader.TryGetLoadedSection(section);

      if (loadedSection != null && loadedSection.HadParsingErrors) {
        ErrorList.ItemsSource = loadedSection.ParsingErrors;
      }
      else {
        ErrorList.ItemsSource = null;
      }
    }
    else {
      await TextView.SetText("No document opened");
    }
  }

  private void ErrorList_SelectionChanged(object sender, SelectionChangedEventArgs e) {
    if (e.AddedItems.Count == 1) {
      var error = e.AddedItems[0] as IRParsingError;
      var document = Session.FindAssociatedDocument(this);
      document.MarkTextRange(error.Location.Offset, 1, Colors.IndianRed);
      document.SetCaretAtOffset(error.Location.Offset);
      document.BringTextOffsetIntoView(error.Location.Offset);
    }
  }

        #region IToolPanel

  public override ToolPanelKind PanelKind => ToolPanelKind.Developer;
  public override HandledEventKind HandledEvents => HandledEventKind.ElementSelection;

  public override async void OnElementSelected(IRElementEventArgs e) {
    var builder = new StringBuilder();
    builder.AppendLine(e.Element.ToString());

    if (e.Element.Tags != null) {
      builder.AppendLine($"{e.Element.Tags.Count} tags:");

      foreach (var tag in e.Element.Tags) {
        builder.AppendLine($"  - {tag.ToString().Indent(4)}");
      }
    }

    await TextView.SetText(builder.ToString());
  }

        #endregion

  private async void ButtonBase_OnClick(object sender, RoutedEventArgs e) {
    ErrorList.Visibility = Visibility.Collapsed;
    TextView.Visibility = Visibility.Visible;
    await ReloadLogFile();
  }

  private async Task ReloadLogFile() {
    string traceFile = App.GetTraceFilePath();

    if (File.Exists(traceFile)) {
      try {
        Trace.Flush();
        using var stream = new FileStream(traceFile, FileMode.Open,
                                          FileAccess.Read, FileShare.ReadWrite);
        using var streamReader = new StreamReader(stream);
        string text = await streamReader.ReadToEndAsync();

        if (text != previousLogFileText_) {
          await TextView.SetText(text);
          TextView.ScrollToEnd();
          previousLogFileText_ = text;
        }
      }
      catch (Exception ex) {
        await TextView.SetText($"Failed to load log file: {ex.Message}");
      }
    }
  }

  private async void ToggleButton_OnChecked(object sender, RoutedEventArgs e) {
    StopLogFileTimer();
    logFileTimer_ = new DispatcherTimer();
    logFileTimer_.Interval = TimeSpan.FromMilliseconds(500);
    logFileTimer_.Tick += async (o, args) => await ReloadLogFile();
    logFileTimer_.Start();
  }

  private void ToggleButton_OnUnchecked(object sender, RoutedEventArgs e) {
    StopLogFileTimer();
  }

  private void StopLogFileTimer() {
    if (logFileTimer_ != null) {
      logFileTimer_.Stop();
      logFileTimer_ = null;
    }
  }

  public override void OnSessionStart() {
    base.OnSessionStart();
    TextView.Session = Session;
  }
}