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
using IRExplorerUI.OptionsPanels;
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
  private OptionsPanelHostWindow optionsPanelWindow_;
  private SourceFileSettings settings_;

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

      if (value != null) {
        sourceFileFinder_ = value.CompilerInfo.SourceFileFinder;
        Settings = App.Settings.SourceFileSettings;
      }
    }
  }

  public SourceFileSettings Settings {
    get => settings_;
    set {
      settings_ = value;
      TextView.TextView.Initialize(App.Settings.DocumentSettings, Session);

      if (!settings_.SyncWithDocument) {
        // Override font and colors.
        TextView.TextView.FontFamily = new FontFamily(settings_.FontName);
        TextView.TextView.FontSize = settings_.FontSize;
        TextView.TextView.Background = ColorBrushes.GetBrush(settings_.BackgroundColor);
        TextView.TextView.Foreground = ColorBrushes.GetBrush(settings_.TextColor);
      }
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
      var debugInfo = await Session.GetDebugInfoProvider(section_.ParentFunction);

      if (await TextView.LoadSourceFile(sourceInfo, section_, debugInfo)) {
        HandleLoadedSourceFile(sourceInfo, section_.ParentFunction);
      }
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

  private async void ResetButton_Click(object sender, RoutedEventArgs e) {
    // Re-enable source mapper if it was disabled before.
    //? TODO: Should clear only the current file?
    if (Utils.ShowYesNoMessageBox("Do you want to reset all file mappings and exclusions?", this) ==
        MessageBoxResult.No) {
      return;
    }

    sourceFileFinder_.Reset();

    // Try to reload source file.
    await ReloadSourceFile();
  }

  private async Task ReloadSourceFile() {
    if (section_ == null) {
      return;
    }

    if (await LoadSourceFileForFunction(section_.ParentFunction, true)) {
      JumpToFunctionStart();
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

  private void ShowOptionsPanel() {
    if (optionsPanelWindow_ != null) {
      optionsPanelWindow_.Close();
      optionsPanelWindow_ = null;
      return;
    }

    FrameworkElement relativeControl = TextView;
    optionsPanelWindow_ = OptionsPanelHostWindow.Create<SourceFileOptionsPanel, SourceFileSettings>(
      settings_.Clone(), relativeControl, Session,
      (newSettings, commit) => {
        if (!newSettings.Equals(settings_)) {
          App.Settings.SourceFileSettings = newSettings;
          Settings = newSettings;
          ReloadSourceFile();

          if (commit) {
            App.SaveApplicationSettings();
          }
          return settings_.Clone();
        }

        return null;
      },
      () => optionsPanelWindow_ = null);
  }
        #region IToolPanel

  public override ToolPanelKind PanelKind => ToolPanelKind.Source;
  public override HandledEventKind HandledEvents => HandledEventKind.ElementSelection;

  public async Task LoadSourceFile(IRTextSection section) {
    section_ = section;
    TextView.ProfileMarkerSettings = settings_.ProfileMarkerSettings;
    TextView.ColumnSettings = settings_.ColumnSettings;

    if (await LoadSourceFileForFunction(section_.ParentFunction)) {
      await JumpToFunctionStart();
    }
  }

  private async Task JumpToFunctionStart() {
    if (settings_.ProfileMarkerSettings.JumpToHottestElement) {
      TextView.JumpToHottestProfiledElement();
    }
    else {
      var (firstSourceLineIndex, lastSourceLineIndex) = 
        await TextView.FindFunctionSourceLineRange(section_.ParentFunction);

      if (firstSourceLineIndex != 0) {
        TextView.SelectLine(firstSourceLineIndex);
      }
    }
  }

  public override async void OnDocumentSectionLoaded(IRTextSection section, IRDocument document) {
    base.OnDocumentSectionLoaded(section, document);
    TextView.AssociatedDocument = document;
    await LoadSourceFile(section);
  }

  private async Task<bool> LoadSourceFileForFunction(IRTextFunction function, bool force = false) {
    if (!force && sourceFileLoaded_ && sourceFileFunc_ == function) {
      return true; // Right file already loaded.
    }

    // Get the associated source file from the debug info if available,
    // since it also includes the start line number.
    string failureText = null;
    var (sourceInfo, debugInfo) = await sourceFileFinder_.FindLocalSourceFile(function);

    if (!sourceInfo.IsUnknown) {
      if (await TextView.LoadSourceFile(sourceInfo, section_, debugInfo)) {
        HandleLoadedSourceFile(sourceInfo, function);
        return true;
      }

      failureText = $"Could not find local copy of source file:\n{sourceInfo.FilePath}";
    }
    else {
      failureText = $"Could not find debug info for function:\n{function.Name}";
    }

    HandleMissingSourceFile(failureText);
    return false;
  }

  private void HandleLoadedSourceFile(SourceFileDebugInfo sourceInfo, IRTextFunction function) {
    SetPanelName(sourceInfo.OriginalFilePath);
    sourceFileLoaded_ = true;
    sourceFileFunc_ = function;
    sourceFilePath_ = sourceInfo.FilePath;
  }

  private void HandleMissingSourceFile(string failureText) {
    TextView.HandleMissingSourceFile(failureText);
    SetPanelName("");
    sourceFileLoaded_ = false;
    sourceFileFunc_ = null;
  }

  public override void OnDocumentSectionUnloaded(IRTextSection section, IRDocument document) {
    base.OnDocumentSectionUnloaded(section, document);
    ResetState();
  }

  private void ResetState() {
    TextView.Reset();
    section_ = null;
    sourceFileLoaded_ = false;
    sourceFileFunc_ = null;
    currentInlinee_ = null;
  }

  public override async void OnElementSelected(IRElementEventArgs e) {
    if (!sourceFileLoaded_) {
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
        TextView.SelectLine(tag.Line);
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
      TextView.SelectLine(inlinee.Line);
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

  private void PanelToolbarTray_OnSettingsClicked(object sender, EventArgs e) {
    ShowOptionsPanel();
  }

  public override async Task OnReloadSettings() {
    await TextView.ReloadSettings();
  }
}