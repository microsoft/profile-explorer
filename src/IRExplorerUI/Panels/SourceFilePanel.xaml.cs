// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ClosedXML.Excel;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Rendering;
using IRExplorerCore;
using IRExplorerCore.IR;
using IRExplorerUI.Compilers;
using IRExplorerUI.Document;
using IRExplorerUI.Panels;
using IRExplorerUI.Profile;
using Microsoft.Win32;
using TextLocation = IRExplorerCore.TextLocation;

namespace IRExplorerUI;

public partial class SourceFilePanel : ToolPanelControl, INotifyPropertyChanged {
  private SourceFileFinder sourceFileFinder_;
  private IRTextSection section_;
  private IRElement element_;
  private bool sourceFileLoaded_;
  private IRTextFunction sourceFileFunc_;
  private string sourceFilePath_;
  private IRExplorerCore.IR.StackFrame currentInlinee_;

  //? TODO: Remember exclusions between sessions
  private HashSet<string> disabledSourceMappings_;

  public SourceFilePanel() {
    InitializeComponent();
    DataContext = this;
    disabledSourceMappings_ = new HashSet<string>();
  }

  public override ISession Session {
    get => base.Session;
    set {
      base.Session = value;
      TextView.Session = value;
      sourceFileFinder_ = new SourceFileFinder(value);
    }
  }

  public event PropertyChangedEventHandler PropertyChanged;

  protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null) {
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
  }

  private void ComboBox_Loaded(object sender, RoutedEventArgs e) {
    if (sender is ComboBox control) {
      Utils.PatchComboBoxStyle(control);
    }
  }

  private void ToolBar_Loaded(object sender, RoutedEventArgs e) {
    Utils.PatchToolbarStyle(sender as ToolBar);
  }

  private string BrowseSourceFile() {
    return Utils.ShowOpenFileDialog(
      "C/C++ source files|*.c;*.cpp;*.cc;*.cxx;*.h;*.hpp;*.hxx;*.hh|.NET source files|*.cs;*.vb|All Files|*.*",
      string.Empty);
  }

  private async Task<bool> LoadSourceFileImpl(string filePath, string originalFilePath, int sourceStartLine) {
    try {
      string text = await File.ReadAllTextAsync(filePath);
      TextView.SetSourceText(text, filePath);
      SetPanelName(originalFilePath);

      //? TODO: Is panel is not visible, scroll doesn't do anything,
      //? should be executed again when panel is activated
      TextView.ScrollToLine(sourceStartLine);
      return true;
    }
    catch (Exception ex) {
      Trace.TraceError($"Failed to load source file {filePath}: {ex.Message}");
      return false;
    }
  }

  private void SetPanelName(string path) {
    if (!string.IsNullOrEmpty(path)) {
      TitleSuffix = $" - {Utils.TryGetFileName(path)}";
      TitleToolTip = path;
    }
    else {
      TitleSuffix = "";
      TitleToolTip = null;
    }

    Session.UpdatePanelTitles();
  }

  private async void OpenButton_Click(object sender, RoutedEventArgs e) {
    string path = BrowseSourceFile();

    if (path != null) {
      var sourceInfo = new SourceFileDebugInfo(path, path);
      await LoadSourceFile(sourceInfo, section_.ParentFunction);
    }
  }

  private async void InlineeCombobox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
    if (e.AddedItems.Count == 1) {
      var inlinee = (IRExplorerCore.IR.StackFrame)e.AddedItems[0];
      await LoadInlineeSourceFile(inlinee);
    }
  }

  private void Button_Click_1(object sender, RoutedEventArgs e) {
    if (InlineeCombobox.ItemsSource != null &&
        InlineeCombobox.SelectedIndex > 0) {
      InlineeCombobox.SelectedIndex--;
    }
  }

  private void Button_Click_2(object sender, RoutedEventArgs e) {
    if (InlineeCombobox.ItemsSource != null &&
        InlineeCombobox.SelectedIndex < ((ListCollectionView)InlineeCombobox.ItemsSource).Count - 1) {
      InlineeCombobox.SelectedIndex++;
    }
  }

  private async void DefaultButton_Click(object sender, RoutedEventArgs e) {
    if (section_ == null) {
      return; //? TODO: Button should be disabled, needs commands
    }

    // Re-enable source mapper if it was disabled before.
    //? TODO: Should clear only the current file
    sourceFileFinder_.Reset();

    if (await LoadSourceFileForFunction(section_.ParentFunction)) {
      TextView.JumpToHottestProfiledElement();
    }
  }

  private void SourceFile_CopyPath(object sender, RoutedEventArgs e) {
    if (!string.IsNullOrEmpty(sourceFilePath_)) {
      Clipboard.SetText(sourceFilePath_);
    }
  }

  private void SourceFile_Open(object sender, RoutedEventArgs e) {
    if (!string.IsNullOrEmpty(sourceFilePath_)) {
      Utils.OpenExternalFile(sourceFilePath_);
    }
  }

        #region IToolPanel

  public override ToolPanelKind PanelKind => ToolPanelKind.Source;
  public override HandledEventKind HandledEvents => HandledEventKind.ElementSelection;

  public async Task LoadSourceFile(IRTextSection section) {
    section_ = section;
    TextView.TextView.Initalize(App.Settings.DocumentSettings, Session);

    if (await LoadSourceFileForFunction(section_.ParentFunction)) {
      TextView.JumpToHottestProfiledElement();
    }
  }

  public override async void OnDocumentSectionLoaded(IRTextSection section, IRDocument document) {
    base.OnDocumentSectionLoaded(section, document);
    section_ = section;
    TextView.AssociatedDocument = document;
    await LoadSourceFile(section_);
  }
  
  private async Task<bool> LoadSourceFileForFunction(IRTextFunction function) {
    if (sourceFileLoaded_ && sourceFileFunc_ == function) {
      return true; // Right file already loaded.
    }

    // Get the associated source file from the debug info if available,
    // since it also includes the start line number.
    FunctionProfileData funcProfile = null;
    bool funcLoaded = false;

    //? TODO: Make async too
    var (sourceInfo, debugInfo) = sourceFileFinder_.FindLocalSourceFile(function);

    if (!sourceInfo.IsUnknown) {
      funcLoaded = await LoadSourceFile(sourceInfo, function);
    }

    if (funcProfile == null) {
      // Check if there is profile info.
      // This path is taken only if there is no debug info.
    }

    if (funcLoaded) {
      funcProfile = Session.ProfileData?.GetFunctionProfile(function);

      if (funcProfile != null) {
        await TextView.AnnotateSourceFileProfilerData(funcProfile, section_, debugInfo);
      }
    }
    else {
      var failureText = $"Could not find debug info for function:\n{function.Name}";
      HandleMissingSourceFile(failureText);
    }

    return funcLoaded;
  }

  private async Task<bool> LoadSourceFile(SourceFileDebugInfo sourceInfo, IRTextFunction function) {
    if (await LoadSourceFileImpl(sourceInfo.FilePath, sourceInfo.OriginalFilePath, sourceInfo.StartLine)) {
      sourceFileLoaded_ = true;
      sourceFileFunc_ = function;
      sourceFilePath_ = sourceInfo.FilePath;
      return true;
    }

    HandleMissingSourceFile($"Could not find local copy of source file:\n{sourceInfo.FilePath}");
    return false;
  }

  private void HandleMissingSourceFile(string failureText) {
    string text = "Failed to load profile source file.";

    if (!string.IsNullOrEmpty(failureText)) {
      text += $"\n{failureText}";
    }

    TextView.SetSourceText(text, "");
    SetPanelName("");
    sourceFileLoaded_ = false;
    sourceFileFunc_ = null;
  }

  public override void OnDocumentSectionUnloaded(IRTextSection section, IRDocument document) {
    base.OnDocumentSectionUnloaded(section, document);
    ResetState();
  }

  private void ResetState() {
    TextView.SelectedLine = -1;
    section_ = null;
    sourceFileLoaded_ = false;
    sourceFileFunc_ = null;
    currentInlinee_ = null;
  }

  public override async void OnElementSelected(IRElementEventArgs e) {
    if (!sourceFileLoaded_ || e.Element == element_) {
      return;
    }

    //Trace.WriteLine($"Selected element: {element_}");
    element_ = e.Element;
    var instr = element_.ParentInstruction;
    var tag = instr?.GetTag<SourceLocationTag>();

    if (tag != null) {
      //if (tag.HasInlinees) {
      //  if (await LoadInlineeSourceFile(tag)) {
      //    return;
      //  }
      //}
      //else {
      //  ResetInlinee();
      //}

      if (await LoadSourceFileForFunction(section_.ParentFunction)) {
        TextView.ScrollToLine(tag.Line);
      }
    }
  }

  private async Task<bool> LoadInlineeSourceFile(SourceLocationTag tag) {
    var last = tag.Inlinees[0];
    InlineeCombobox.ItemsSource = new ListCollectionView(tag.Inlinees);
    InlineeCombobox.SelectedItem = last;
    return await LoadInlineeSourceFile(last);
  }

  private void ResetInlinee() {
    InlineeCombobox.ItemsSource = null;
    currentInlinee_ = null;
  }

  //? TODO: Select source line must go through inlinee mapping to select proper asm
  //     all instrs that have the line on the inlinee list for this func

  public async Task<bool> LoadInlineeSourceFile(IRExplorerCore.IR.StackFrame inlinee) {
    if (inlinee == currentInlinee_) {
      return true;
    }

    // Try to load the profile info of the inlinee.
    var summary = section_.ParentFunction.ParentSummary;

    var inlineeFunc = summary.FindFunction(funcName => {
      string demangledName = PDBDebugInfoProvider.DemangleFunctionName(funcName);
      return demangledName == inlinee.Function;
    });

    bool fileLoaded = false;

    if (inlineeFunc != null) {
      fileLoaded = await LoadSourceFileForFunction(inlineeFunc);
    }

    //? TODO: The func ASM is not needed, profile is created by mapping ASM lines in main func
    //? to corresponding lines in the selected inlinee
    if (fileLoaded) {
      TextView.ScrollToLine(inlinee.Line);
    }

    currentInlinee_ = inlinee;
    return fileLoaded;
  }

  public override void OnSessionEnd() {
    base.OnSessionEnd();
    ResetState();
    TextView.SetSourceText("", "");
  }

  private async void PanelToolbarTray_OnHelpClicked(object sender, EventArgs e) {
    await HelpPanel.DisplayPanelHelp(PanelKind, Session);
  }

        #endregion
}
