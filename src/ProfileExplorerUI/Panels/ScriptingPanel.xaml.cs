// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Editing;
using ProfileExplorer.Core;
using ProfileExplorer.Core.IR;
using ProfileExplorer.UI.Scripting;
using Microsoft.CodeAnalysis;

namespace ProfileExplorer.UI;

public static class ScriptingCommand {
  public static readonly RoutedUICommand ExecuteScript = new("Untitled", "ExecuteScript", typeof(BookmarksPanel));
}

public class CompletionWindowEx : CompletionWindow {
  public CompletionWindowEx(TextArea textArea) : base(textArea) {
  }

  public void SetInitialWidth(double width) {
    Width = Math.Clamp(width, 100, TextArea.ActualWidth);
  }

  public void SetStartOffset(TextArea textArea) {
    StartOffset = EndOffset = textArea.Caret.Offset;
  }

  public void AdjustStartOffset(string currentWord) {
    StartOffset -= currentWord.Length;
  }

  public void PreselectItem(string text) {
    CompletionList.IsFiltering = false; // Disable filtering to still show complete list.
    CompletionList.SelectItem(text);
    CompletionList.ScrollIntoView(CompletionList.SelectedItem);
  }
}

public partial class ScriptingPanel : ToolPanelControl {
  private const int SyntaxErrorHighlightingDelay = 1;
  private static readonly string InitialScript =
    string.Join(Environment.NewLine,
                "using System;",
                "using System.Collections.Generic;",
                "using System.Windows.Media;",
                "using ProfileExplorer.Core;", "using ProfileExplorer.Core.IR;",
                "using ProfileExplorer.Core.Analysis;",
                "using ProfileExplorer.UI;",
                "using ProfileExplorer.UI.Scripting;",
                "\n",
                "public class Script {",
                "    // s: provides script interaction with Profile Explorer (text output, marking, etc.)",
                "    public bool Execute(ScriptSession s) {",
                "        // Write C#-based script here.",
                "        return true;",
                "    }",
                "}");
  private CompletionWindowEx completionWindow_;
  private int currentAutocompleteHash_;
  private ScriptAutoComplete autoComplete_;
  private ElementHighlighter errorHighlighter_;
  private DelayedAction errorHighlightingAction_;
  private List<Diagnostic> errorDiagnostics_;
  private ToolTip errorTooltip_;

  public ScriptingPanel() {
    InitializeComponent();
    autoComplete_ = new ScriptAutoComplete();

    errorHighlighter_ = new ElementHighlighter(HighlighingType.Marked);
    TextView.TextArea.TextEntered += TextArea_TextEntered;
    TextView.TextArea.KeyUp += TextArea_KeyUp;
    TextView.MouseHover += TextView_MouseHover;
    TextView.MouseHoverStopped += TextView_MouseHoverStopped;
    TextView.TextArea.TextView.BackgroundRenderers.Add(errorHighlighter_);
  }

  private void TextView_MouseHoverStopped(object sender, MouseEventArgs e) {
    if (errorTooltip_ != null) {
      errorTooltip_.IsOpen = false;
      errorTooltip_ = null;
      e.Handled = true;
    }
  }

  private void TextView_MouseHover(object sender, MouseEventArgs e) {
    if (errorDiagnostics_ == null) {
      return;
    }

    var position = TextView.TextArea.TextView.GetPositionFloor(e.GetPosition(TextView.TextArea.TextView) +
                                                               TextView.TextArea.TextView.ScrollOffset);

    if (!position.HasValue) {
      return;
    }

    var logicalPosition = position.Value.Location;
    int offset = TextView.Document.GetOffset(logicalPosition);

    foreach (var error in errorDiagnostics_) {
      if (offset >= error.Location.SourceSpan.Start &&
          offset <= error.Location.SourceSpan.End) {
        errorTooltip_ = new ToolTip();
        errorTooltip_.Closed += ErrorTooltip__Closed;
        errorTooltip_.PlacementTarget = TextView;
        errorTooltip_.Content = new TextBlock {
          Text = error.ToString(),
          TextWrapping = TextWrapping.Wrap
        };

        errorTooltip_.IsOpen = true;
        e.Handled = true;
        break;
      }
    }
  }

  private void ErrorTooltip__Closed(object sender, RoutedEventArgs e) {
    if (errorTooltip_ != null) {
      errorTooltip_.IsOpen = false;
    }
  }

  private async void TextArea_KeyUp(object sender, KeyEventArgs e) {
    if (e.Key == Key.Back || e.Key == Key.Delete) {
      ClearSyntaxErrorHighlighting();

      e.Handled = true;
      await HandleTextChange(TextView.Text, "");
    }
  }

  private async Task HandleTextChange(string text, string changedText) {
    //? TODO: More efficient ways of getting the text
    // https://stackoverflow.com/questions/39422126/whats-the-right-way-to-update-roslyns-document-while-typing
    // https://stackoverflow.com/questions/39421668/whats-the-most-efficient-way-to-use-roslyns-completionsevice-when-typing

    // Get the list of autocomplete items.
    int position = TextView.CaretOffset;
    string word = ScriptAutoComplete.GetCurrentWord(text, position - 1);
    var results = await autoComplete_.GetSuggestionsAsync(text, position, changedText);

    if (results.Count == 0) {
      HideAutocompleteBox();
    }

    // If it's the same list as before, keep  the current autocomplete box.
    int hash = ComputeAutcompleteHash(results);

    if (hash == currentAutocompleteHash_) {
      return;
    }

    currentAutocompleteHash_ = hash;
    ShowAutocompleteBox(results, word);
  }

  private void ShowAutocompleteBox(List<AutocompleteEntry> results, string word) {
    // Reuse the existing auto-complete box if still visible, removes UI flickering
    // that happens if a new box is always created to replace the old one.
    bool newWindow = false;

    if (completionWindow_ == null) {
      completionWindow_ = new CompletionWindowEx(TextView.TextArea);
      newWindow = true;
    }
    else {
      completionWindow_.CompletionList.CompletionData.Clear();
      completionWindow_.SetStartOffset(TextView.TextArea);
    }

    // Make the box wide enough to avoid horizontal scrolling.
    string longestText = results.OrderByDescending(s => s.Text.Length).First().Text;
    completionWindow_.SetInitialWidth(longestText.Length * 10);

    // Populate window and check if an item is preferred and should be preselected.
    string preferredItem = null;

    foreach (var item in results) {
      completionWindow_.CompletionList.CompletionData.Add(item);

      if (item.IsPreferred) {
        preferredItem = item.Text;
      }
    }

    // Offset the start position so that the current word gets replaced
    // when a suggestion is selected.
    completionWindow_.AdjustStartOffset(word);

    if (preferredItem != null) {
      completionWindow_.PreselectItem(preferredItem);
    }
    else {
      // Preselect first result if there was no preferred item.
      string firstItem = results.First().Text;

      if (firstItem.StartsWith(word, StringComparison.OrdinalIgnoreCase)) {
        completionWindow_.PreselectItem(firstItem);
      }
    }

    if (newWindow) {
      completionWindow_.Closed += delegate {
        completionWindow_ = null;
        currentAutocompleteHash_ = 0;
      };

      completionWindow_.CloseAutomatically = false;
      completionWindow_.Show();
    }
  }

  private async void TextArea_TextEntered(object sender, TextCompositionEventArgs e) {
    ClearSyntaxErrorHighlighting();

    e.Handled = true;
    string text = TextView.Text;
    string changedText = e.Text;
    await HandleTextChange(text, changedText).ConfigureAwait(false);

    if (changedText == ".") {
      return;
    }

    // Start a task that checks for syntax errors after a delay.
    if (errorHighlightingAction_ != null) {
      errorHighlightingAction_.Cancel();
      errorHighlightingAction_ = null;
    }

    var action = new DelayedAction();
    errorHighlightingAction_ = action;

    await action.Start(TimeSpan.FromSeconds(SyntaxErrorHighlightingDelay), async () => {
      var errors = await autoComplete_.GetSourceErrorsAsync(text);
      errorDiagnostics_ = errors;

      await Dispatcher.BeginInvoke((Action)(() => {
        ClearSyntaxErrorHighlighting();
        var group = new HighlightedElementGroup(new HighlightingStyle(Colors.Red, 0.1, ColorPens.GetPen(Colors.Red)));

        foreach (var error in errors) {
          if (error.Location.SourceSpan.Length > 0) {
            //? TODO: Have a highlighter that doesn't need making dummy IR elements...
            var element = CreateDummyElement(error.Location.SourceSpan.Start, error.Location.SourceSpan.Length);
            group.Add(element);
          }
        }

        errorHighlighter_.Add(group);
        TextView.TextArea.TextView.Redraw();
      }));
    });
  }

  private void ClearSyntaxErrorHighlighting() {
    errorHighlighter_.Clear();
    TextView.TextArea.TextView.Redraw();
  }

  private IRElement CreateDummyElement(int offset, int length) {
    int line = TextView.Document.GetLineByOffset(offset).LineNumber;
    var location = new TextLocation(offset, line, 0);
    return new IRElement(location, length);
  }

  private int ComputeAutcompleteHash(List<AutocompleteEntry> sortedData) {
    int hash = 0;

    foreach (var item in sortedData) {
      hash = hash * 31 + item.Text.GetHashCode();
    }

    return hash;
  }

  private void HideAutocompleteBox() {
    if (completionWindow_ != null) {
      completionWindow_.Close();
      completionWindow_ = null;
    }
  }

  private async void ExecuteScriptExecuted(object sender, ExecutedRoutedEventArgs e) {
    var document = Session.FindAssociatedDocument(this);
    string userCode = TextView.Text.Trim();
    var scriptSession = new ScriptSession(document, Session);
    var script = new Script(userCode);

    try {
      var sw = Stopwatch.StartNew();
      string outputText = "";
      bool result = await script.ExecuteAsync(scriptSession);
      sw.Stop();

      if (!result) {
        if (script.ScriptException != null) {
          outputText += $"Failed to run script: {script.ScriptException}";
        }
      }
      else {
        //? TODO: Long-running scripts need a way to update text before this
        outputText = string.Join(Environment.NewLine,
                                 $"Script result: {script.ScriptResult}",
                                 $"Script completed in {sw.ElapsedMilliseconds} ms",
                                 "----------------------------------------\n",
                                 $"{scriptSession.OutputText}");
      }

      OutputTextView.Text = outputText;

      foreach (var pair in scriptSession.MarkedElements) {
        document.MarkElement(pair.Item1, pair.Item2);
      }
    }
    catch (Exception ex) {
      OutputTextView.Text = $"Failed to run script: {ex}";
    }
  }

  private void ToolBar_Loaded(object sender, RoutedEventArgs e) {
    Utils.PatchToolbarStyle(sender as ToolBar);
  }

        #region IToolPanel

  public override ToolPanelKind PanelKind => ToolPanelKind.Scripting;

  public override void OnSessionStart() {
    base.OnSessionStart();
    TextView.Text = InitialScript;
  }

  public override void OnActivatePanel() {
    base.OnActivatePanel();

    Task.Run(() => Script.WarmUp());
    Task.Run(() => ScriptAutoComplete.WarmUp());
  }

        #endregion
}