// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using ProfileExplorerCore;
using ProfileExplorerCore.Analysis;
using ProfileExplorerCore.IR;
using ProfileExplorerCore.IR.Tags;
using ProfileExplorer.UI.DebugServer;
using ProfileExplorerCore.Utilities;
using ProfileExplorerCore.Session;

namespace ProfileExplorer.UI;

public partial class MainWindow : Window, IUISession {
  private DebugServer.DebugService debugService_;
  private Dictionary<ElementIteratorId, IRElement> debugCurrentIteratorElement_;
  private StackFrame debugCurrentStackFrame_;
  private IRTextFunction debugFunction_;
  private long debugProcessId_;
  private int debugSectionId_;
  private DebugSectionLoader debugSections_;
  private IRTextSummary debugSummary_;
  private CancelableTask sessionStartTask_;

  private void StartGrpcServer() {
    try {
      Trace.TraceInformation("Starting Grpc server...");
      debugService_ = new DebugServer.DebugService();
      debugService_.OnStartSession += DebugService_OnSessionStarted;
      debugService_.OnUpdateIR += DebugService_OnIRUpdated;
      debugService_.OnMarkElement += DebugService_OnElementMarked;
      debugService_.OnSetCurrentElement += DebugService_OnSetCurrentElement;
      debugService_.OnExecuteCommand += DebugService_OnExecuteCommand;
      debugService_.OnHasActiveBreakpoint += DebugService_OnHasActiveBreakpoint;
      debugService_.OnClearTemporaryHighlighting += DebugService__OnClearTemporaryHighlighting;
      debugService_.OnUpdateCurrentStackFrame += DebugService__OnUpdateCurrentStackFrame;

      Trace.TraceInformation("Grpc server started, waiting for client...");
      SetOptionalStatus("Waiting for client...");
      DiffPreviousSectionCheckbox.Visibility = Visibility.Visible;

      DebugServer.DebugService.StartServer(debugService_);
    }
    catch (Exception ex) {
      Trace.TraceError("Failed to start Grpc server: {0}", $"{ex.Message}\n{ex.StackTrace}");
      SetOptionalStatus("Failed to start Grpc server.", $"{ex.Message}\n{ex.StackTrace}", Brushes.Red);
    }
  }

  private async Task<bool> WaitForSessionInitialization() {
    // Wait until the session initialized properly.
    if (sessionStartTask_ == null) {
      return false;
    }

    Trace.WriteLine("=> WaitForSessionInitialization\n");

    if (sessionStartTask_.IsCompleted) {
      Trace.WriteLine("  < already completed\n");
      return true;
    }

    Trace.WriteLine("  > start waiting\n");

    if (!await Task.Run(() => sessionStartTask_.WaitToComplete(TimeSpan.FromSeconds(30))).ConfigureAwait(false)) {
      Trace.WriteLine("    < timeout\n");
      return false;
    }

    Trace.WriteLine($"    < completed {sessionStartTask_.IsCompleted}\n");
    return sessionStartTask_.IsCompleted;
  }

  private async void DebugService__OnUpdateCurrentStackFrame(object sender, CurrentStackFrameRequest e) {
    if (!await WaitForSessionInitialization().ConfigureAwait(false)) {
      return;
    }

    await Dispatcher.BeginInvoke(new Action(async () => {
      debugCurrentStackFrame_ = e.CurrentFrame;
    }));
  }

  private async void DebugService__OnClearTemporaryHighlighting(object sender, ClearHighlightingRequest e) {
    if (!await WaitForSessionInitialization().ConfigureAwait(false)) {
      return;
    }

    await Dispatcher.BeginInvoke(new Action(async () => {
      var activeDoc = await GetDebugSessionDocument();

      if (activeDoc == null) {
        return;
      }

      var action = new DocumentAction(DocumentActionKind.ClearTemporaryMarkers);
      activeDoc.TextView.ExecuteDocumentAction(action);
    }));
  }

  private async void DebugService_OnHasActiveBreakpoint(object sender,
                                                        RequestResponsePair<ActiveBreakpointRequest, bool> e) {
    if (!await WaitForSessionInitialization().ConfigureAwait(false)) {
      return;
    }

    if (addressTag_ == null) {
      return;
    }

    if (addressTag_.AddressToElementMap.TryGetValue(e.Request.ElementAddress, out var element)) {
      await Dispatcher.BeginInvoke(async () => {
        var activeDoc = await GetDebugSessionDocument();
        if (activeDoc == null)
          return;
        e.Response = activeDoc.TextView.BookmarkManager.FindBookmark(element) != null;
      });
    }
  }

  private DocumentActionKind CommandToAction(ElementCommand command) {
    return command switch {
      ElementCommand.GoToDefinition => DocumentActionKind.GoToDefinition,
      ElementCommand.MarkBlock      => DocumentActionKind.MarkBlock,
      ElementCommand.MarkExpression => DocumentActionKind.MarkExpression,
      ElementCommand.MarkReferences => DocumentActionKind.MarkReferences,
      ElementCommand.MarkUses       => DocumentActionKind.MarkUses,
      ElementCommand.ShowExpression => DocumentActionKind.ShowExpressionGraph,
      ElementCommand.ShowReferences => DocumentActionKind.ShowReferences,
      ElementCommand.ShowUses       => DocumentActionKind.ShowUses,
      ElementCommand.ClearMarker    => DocumentActionKind.ClearMarker,
      _                             => throw new NotImplementedException()
    };
  }

  private async void DebugService_OnExecuteCommand(object sender, ElementCommandRequest e) {
    if (!await WaitForSessionInitialization().ConfigureAwait(false)) {
      return;
    }

    //? TODO: This can be another race condition, it should wait for the IR to be set
    if (addressTag_ == null) {
      return;
    }

    if (addressTag_.AddressToElementMap.TryGetValue(e.ElementAddress, out var element)) {
      await Dispatcher.BeginInvoke(new Action(async () => {
        var activeDoc = await GetDebugSessionDocument();
        if (activeDoc == null)
          return;

        AppendNotesTag(element, e.Label);
        var style = new PairHighlightingStyle();
        style.ParentStyle = new HighlightingStyle(Colors.Transparent, ColorPens.GetPen(Colors.Purple));

        style.ChildStyle = new HighlightingStyle(Colors.LightPink,
                                                 ColorPens.GetPen(Colors.MediumVioletRed));

        var actionKind = CommandToAction(e.Command);

        if (actionKind == DocumentActionKind.MarkExpression) {
          var data = new MarkActionData {
            IsTemporary = e.Highlighting == HighlightingType.Temporary
          };

          var action = new DocumentAction(actionKind, element, data);
          activeDoc.TextView.ExecuteDocumentAction(action);
        }
        else {
          var action = new DocumentAction(actionKind, element, style);
          activeDoc.TextView.ExecuteDocumentAction(action);
        }
      }));
    }
  }

  private async void DebugService_OnSetCurrentElement(object sender, SetCurrentElementRequest e) {
    if (!await WaitForSessionInitialization().ConfigureAwait(false)) {
      return;
    }

    if (addressTag_ == null) {
      return;
    }

    await Dispatcher.BeginInvoke(new Action(async () => {
      var activeDoc = await GetDebugSessionDocument();
      if (activeDoc == null)
        return;

      try {
        var elementIteratorId = new ElementIteratorId(e.ElementId, e.ElementKind);

        if (debugCurrentIteratorElement_.TryGetValue(elementIteratorId, out var currentElement)) {
          currentElement.RemoveTag<NotesTag>();
          activeDoc.TextView.ClearMarkedElement(currentElement);
          debugCurrentIteratorElement_.Remove(elementIteratorId);
        }

        if (e.ElementAddress != 0 &&
            addressTag_.AddressToElementMap.TryGetValue(e.ElementAddress, out var element)) {
          var notesTag = AppendNotesTag(element, e.Label);

          if (e.ElementKind == IRElementKind.Block) {
            activeDoc.TextView.MarkBlock(element, GetIteratorElementStyle(elementIteratorId));
          }
          else {
            activeDoc.TextView.MarkElement(element, GetIteratorElementStyle(elementIteratorId));
          }

          activeDoc.TextView.BringElementIntoView(element);
          debugCurrentIteratorElement_[elementIteratorId] = element;
        }
      }
      catch (Exception ex) {
        Trace.TraceError($"Failed OnSetCurrentElement: {ex.Message}, {ex.StackTrace}");
      }
    }));
  }

  private NotesTag AppendNotesTag(IRElement element, string label) {
    if (!string.IsNullOrEmpty(label)) {
      element.RemoveTag<NotesTag>();
      var notesTag = element.GetOrAddTag<NotesTag>();
      notesTag.Title = label;

      if (debugCurrentStackFrame_ != null) {
        notesTag.Notes.Add(
          $"{debugCurrentStackFrame_.Function}:{debugCurrentStackFrame_.LineNumber}");
      }

      return notesTag;
    }

    return null;
  }

  private HighlightingStyle GetIteratorElementStyle(ElementIteratorId elementIteratorId) {
    return elementIteratorId.ElementKind switch {
      IRElementKind.Instruction => new HighlightingStyle(
        Colors.LightBlue, ColorPens.GetPen(Colors.MediumBlue)),
      IRElementKind.Operand => new HighlightingStyle(Colors.PeachPuff,
                                                     ColorPens.GetPen(Colors.SaddleBrown)),
      IRElementKind.User => new HighlightingStyle(Utils.ColorFromString("#EFBEE6"),
                                                  ColorPens.GetPen(Colors.Purple)),
      IRElementKind.UserParent =>
        new HighlightingStyle(Colors.Lavender, ColorPens.GetPen(Colors.Purple)),
      IRElementKind.Block => new HighlightingStyle(Colors.LightBlue, ColorPens.GetPen(Colors.LightBlue)),
      _                   => new HighlightingStyle(Colors.Gray)
    };
  }

  private async void DebugService_OnElementMarked(object sender, MarkElementRequest e) {
    if (!await WaitForSessionInitialization().ConfigureAwait(false)) {
      return;
    }

    if (addressTag_ == null) {
      return;
    }

    if (addressTag_.AddressToElementMap.TryGetValue(e.ElementAddress, out var element)) {
      Dispatcher.BeginInvoke(new Action(async () => {
        var activeDoc = await GetDebugSessionDocument();
        var notesTag = AppendNotesTag(element, e.Label);

        if (e.Highlighting == HighlightingType.Temporary) {
          activeDoc.TextView.HighlightElement(element, HighlighingType.Selected);
        }
        else {
          HighlightingStyle style;

          if (e.Color != null && e.Color.IsValidRGBColor()) {
            style = new HighlightingStyle(e.Color.ToColor());
          }
          else if (debugCurrentStackFrame_ != null) {
            // Pick a color based on the source line number.
            style = DefaultHighlightingStyles.StyleSet.ForIndex(debugCurrentStackFrame_.LineNumber);
          }
          else {
            style = new HighlightingStyle(Colors.Gray);
          }

          activeDoc.TextView.MarkElement(element, style);
        }

        activeDoc.TextView.BringElementIntoView(element);
      }));
    }
  }

  private async Task<IRDocumentHost> GetDebugSessionDocument() {
    // Find the document with the last version of the IR loaded.
    // If there is none, use the current active document.
    var result = FindDebugSessionDocument();

    if (result != null) {
      return result;
    }

    var activeDoc = FindActiveDocumentHost();

    if (activeDoc != null) {
      return activeDoc;
    }

    return await ReopenDebugSessionDocument();
  }

  private IRDocumentHost FindDebugSessionDocument() {
    var result = sessionState_.DocumentHosts.Find(item => item.DocumentHost.Section == previousDebugSection_);

    if (result != null) {
      return result.DocumentHost;
    }

    return null;
  }

  private async Task<IRDocumentHost> ReopenDebugSessionDocument() {
    if (previousDebugSection_ == null) {
      return null;
    }

    return await OpenDocumentSectionAsync(
      new OpenSectionEventArgs(previousDebugSection_, OpenSectionKind.ReplaceCurrent));
  }

  private string ExtractFunctionName(string text) {
    int startIndex = 0;

    while (startIndex < text.Length) {
      int index = text.IndexOfAny(new[] {'\r', '\n'}, startIndex);

      if (index != -1) {
        string line = text.Substring(startIndex, index - startIndex + 1);
        startIndex = index + 1;
      }
      else {
        break;
      }
    }

    return null;
  }

  private async void DebugService_OnIRUpdated(object sender, UpdateIRRequest e) {
    if (!await WaitForSessionInitialization().ConfigureAwait(false)) {
      return;
    }

    await Dispatcher.BeginInvoke(new Action(async () => {
      if (!IsSessionStarted ||
          sessionState_.MainDocument == null) {
        return;
      }

      string funcName = ExtractFunctionName(e.Text);
      funcName ??= "Debugged function";

      if (previousDebugSection_ != null) {
        // Task.Run(() => previousDebugSection_.CompressLineMetadata());
        previousDebugSection_.CompressLineMetadata();
      }

      if (debugFunction_ == null || funcName != debugFunction_.Name) {
        debugFunction_ = new IRTextFunction(funcName);
        previousDebugSection_ = null;
        debugSectionId_ = 0;
        debugCurrentIteratorElement_ = new Dictionary<ElementIteratorId, IRElement>();
        sessionState_.MainDocument.Summary.AddFunction(debugFunction_);
      }

      string sectionName = $"Version {debugSectionId_ + 1}";

      if (debugCurrentStackFrame_ != null) {
        sectionName += $" ({debugCurrentStackFrame_.Function}:{debugCurrentStackFrame_.LineNumber})";
      }

      var section = new IRTextSection(debugFunction_, sectionName, IRPassOutput.Empty);
      string filteredText = ExtractLineMetadata(section, e.Text);

      // Ignore if nothing changed.
      try {
        if (previousDebugSection_ != null &&
            debugSections_.GetSectionText(previousDebugSection_) == filteredText) {
          return; // Text unchanged
        }
      }
      catch (Exception ex) {
        using var centerForm = new DialogCenteringHelper(this);
        MessageBox.Show($"Unexpected RPC failure: {ex.Message}\n {ex.StackTrace}");
      }

      debugFunction_.AddSection(section);
      debugSummary_.AddSection(section);
      debugSections_.AddSection(section, filteredText);

      // Force function list update when updating summary.
      await SectionPanel.SetMainSummary(debugSummary_, true);
      await SectionPanel.SelectSection(section);

      //? TODO: After switch, try to restore same position in doc
      //? Markers, bookmarks, notes, etc should be copied over
      var document = FindDebugSessionDocument();
      double horizontalOffset = 0;
      double verticalOffset = 0;

      if (previousDebugSection_ != null && document != null) {
        horizontalOffset = document.TextView.HorizontalOffset;
        verticalOffset = document.TextView.VerticalOffset;
      }

      await OpenDocumentSectionAsync(new OpenSectionEventArgs(section, OpenSectionKind.ReplaceCurrent), document);

      //? TODO: Have a proper option in the UI for diffing previous section
      if (previousDebugSection_ != null && document != null) {
        if (DiffPreviousSectionCheckbox.IsChecked.HasValue &&
            DiffPreviousSectionCheckbox.IsChecked.Value) {
          await DiffSingleDocumentSections(document, section, previousDebugSection_);
          document.TextView.ScrollToHorizontalOffset(horizontalOffset);
          document.TextView.ScrollToVerticalOffset(verticalOffset);
        }
      }

      previousDebugSection_ = section;
      debugSectionId_++;
    }));
  }

  //? TODO: Not needed anymore, parser should have extracted metadata already
  private string ExtractLineMetadata(IRTextSection section, string text) {
    var builder = new StringBuilder(text.Length);
    string[] lines = text.Split(new[] {"\r\n", "\r", "\n"}, StringSplitOptions.None);
    int metadataLines = 0;

    for (int i = 0; i < lines.Length; i++) {
      string line = lines[i];

      if (line.StartsWith("/// metadata:")) {
        section.AddLineMetadata(i - metadataLines - 1, line);
        metadataLines++;
      }
      else {
        builder.AppendLine(line);
      }
    }

    return builder.ToString();
  }

  private async void DebugService_OnSessionStarted(object sender, StartSessionRequest e) {
    if (e.ProcessId == debugProcessId_) {
      return;
    }

    // Mark that session is about to start.
    await WaitForSessionInitialization().ConfigureAwait(false);
    sessionStartTask_ = new CancelableTask();
    Trace.WriteLine("=> OnSessionStarted\n");

    Dispatcher.BeginInvoke(new Action(async () => {
      try {
        Trace.WriteLine("=> BeginInvoke OnSessionStarted\n");
        SetOptionalStatus(TimeSpan.FromSeconds(10), $"Client connected to process #{e.ProcessId}");
        await EndSession();

        FunctionAnalysisCache.DisableCache(); // Reduce memory usage.
        var result = new UILoadedDocument("Debug session", "", Guid.NewGuid());
        debugSections_ = new DebugSectionLoader(compilerInfo_.IR);
        debugSummary_ = debugSections_.LoadDocument(null);
        result.Loader = debugSections_;
        result.Summary = debugSummary_;
        result.ModuleName = "Debug session";

        SetupOpenedIRDocument(SessionKind.DebugSession, result).Wait();

        debugCurrentIteratorElement_ = new Dictionary<ElementIteratorId, IRElement>();
        debugProcessId_ = e.ProcessId;
        debugSectionId_ = 0;
        debugFunction_ = null;
        previousDebugSection_ = null;

        // Mark session started.
        Trace.WriteLine("<= OnSessionStarted\n");
        sessionStartTask_.Complete();
      }
      catch (Exception ex) {
        Trace.TraceError("Failed to start debug session: {0}", $"{ex.Message}\n{ex.StackTrace}");
        SetOptionalStatus("Failed to start debug session", ex.Message, Brushes.Red);
        sessionStartTask_.Cancel();
      }
    }), DispatcherPriority.Send);
  }

  private struct ElementIteratorId {
    public int ElementId;
    public IRElementKind ElementKind;

    public ElementIteratorId(int elementId, IRElementKind elementKind) {
      ElementId = elementId;
      ElementKind = elementKind;
    }

    public override bool Equals(object obj) {
      return obj is ElementIteratorId id &&
             ElementId == id.ElementId &&
             ElementKind == id.ElementKind;
    }

    public override int GetHashCode() {
      return HashCode.Combine(ElementId, ElementKind);
    }
  }
}