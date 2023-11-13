// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using IRExplorerCore;
using IRExplorerCore.IR;
using IRExplorerCore.Utilities;

namespace IRExplorerUI;

public partial class IRInfoPanel : ToolPanelControl {
  public IRInfoPanel() {
    InitializeComponent();
  }

  private void Button_Click(object sender, RoutedEventArgs e) {
    ErrorList.Visibility = Visibility.Collapsed;
    TextView.Visibility = Visibility.Visible;
    var printer = new IRPrinter(Session.CurrentDocument.Function);
    TextView.Text = printer.Print();

    TextView.SyntaxHighlighting =
      Utils.LoadSyntaxHighlightingFile(App.GetInternalIRSyntaxHighlightingFilePath());
  }

  private void ToolBar_Loaded(object sender, RoutedEventArgs e) {
    Utils.PatchToolbarStyle(sender as ToolBar);
  }

  private void Button_Click_1(object sender, RoutedEventArgs e) {
    ErrorList.Visibility = Visibility.Visible;
    TextView.Visibility = Visibility.Collapsed;
    var section = Session.FindAssociatedDocument(this).Section;
    var loader = Session.SessionState.FindLoadedDocument(section).Loader;
    var loadedSection = loader.TryGetLoadedSection(section);

    if (loadedSection != null && loadedSection.HadParsingErrors) {
      ErrorList.ItemsSource = loadedSection.ParsingErrors;
    }
    else {
      ErrorList.ItemsSource = null;
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

  public override void OnElementSelected(IRElementEventArgs e) {
    var builder = new StringBuilder();
    builder.AppendLine(e.Element.ToString());

    if (e.Element.Tags != null) {
      builder.AppendLine($"{e.Element.Tags.Count} tags:");

      foreach (var tag in e.Element.Tags) {
        builder.AppendLine($"  - {tag.ToString().Indent(4)}");
      }
    }

    TextView.Text = builder.ToString();
  }

        #endregion
}
