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
using IRExplorerCore;
using IRExplorerCore.IR;
using IRExplorerUI.Compilers;
using IRExplorerUI.Controls;
using IRExplorerUI.Document;
using IRExplorerUI.OptionsPanels;
using IRExplorerUI.Panels;
using IRExplorerUI.Profile;

namespace IRExplorerUI;

public partial class SourceFilePanel : ToolPanelControl, INotifyPropertyChanged {
  private SourceFileFinder sourceFileFinder_;
  private IRTextSection section_;
  private IRElement element_;
  private bool sourceFileLoaded_;
  private IRTextFunction sourceFileFunc_;
  private string sourceFilePath_;
  private SourceStackFrame currentInlinee_;
  private OptionsPanelHostWindow optionsPanelWindow_;
  private SourceFileSettings settings_;
  private bool disableInlineeComboboxEvents_;

  public SourceFilePanel() {
    InitializeComponent();
    DataContext = this;
  }

  public override ToolPanelKind PanelKind => ToolPanelKind.Source;
  public override HandledEventKind HandledEvents => HandledEventKind.ElementSelection;
  public bool HasInlinees => InlineeComboBox.Items.Count > 0;

  public override ISession Session {
    get => base.Session;
    set {
      base.Session = value;
      ProfileTextView.Session = value;
      Settings = App.Settings.SourceFileSettings;
    }
  }

  public SourceFileSettings Settings {
    get => settings_;
    set {
      settings_ = value;
      sourceFileFinder_ = Session.CompilerInfo.SourceFileFinder;
      OnPropertyChanged();

      if (settings_.SyncStyleWithDocument) {
        // Patch the settings with the document settings.
        var clone = settings_.Clone();
        clone.FontName = App.Settings.DocumentSettings.FontName;
        clone.FontSize = App.Settings.DocumentSettings.FontSize;
        clone.BackgroundColor = App.Settings.DocumentSettings.BackgroundColor;
        clone.TextColor = App.Settings.DocumentSettings.TextColor;
        ProfileTextView.Initialize(clone);
      }
      else {
        ProfileTextView.Initialize(settings_);
      }
    }
  }

  public event PropertyChangedEventHandler PropertyChanged;

  protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null) {
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
  }

  private void InlineeComboBox_Loaded(object sender, RoutedEventArgs e) {
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

      if (await ProfileTextView.LoadSourceFile(sourceInfo, section_)) {
        HandleLoadedSourceFile(sourceInfo, section_.ParentFunction);
      }
    }
  }

  private async void InlineeCombobox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
    if (InlineeComboBox.SelectedItem != null &&
        !disableInlineeComboboxEvents_) {
      var inlinee = (SourceStackFrame)InlineeComboBox.SelectedItem;

      if (InlineeComboBox.SelectedIndex > 0) {
        await LoadInlineeSourceFile(inlinee);
      }
      else {
        // First item means "no inlinee".
        await LoadSourceFileForFunction(section_.ParentFunction, ProfileTextView.ProfileFilter);
      }
    }
  }

  private void InlineUpButton_Click(object sender, RoutedEventArgs e) {
    if (InlineeComboBox.ItemsSource != null &&
        InlineeComboBox.SelectedIndex > 0) {
      InlineeComboBox.SelectedIndex--;
    }
  }

  private void InlineDownButton_Click(object sender, RoutedEventArgs e) {
    if (InlineeComboBox.ItemsSource != null &&
        InlineeComboBox.SelectedIndex < ((ListCollectionView)InlineeComboBox.ItemsSource).Count - 1) {
      InlineeComboBox.SelectedIndex++;
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

    sourceFileLoaded_ = false; // Force a reload.
    await LoadSourceFileForFunction(section_.ParentFunction, null);
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

    FrameworkElement relativeControl = ProfileTextView;
    optionsPanelWindow_ = OptionsPanelHostWindow.Create<SourceFileOptionsPanel, SourceFileSettings>(
      settings_.Clone(), relativeControl, Session,
      async (newSettings, commit) => {
        if (!newSettings.Equals(settings_)) {
          Settings = newSettings;
          await ReloadSourceFile();
          App.Settings.SourceFileSettings = newSettings;

          if (commit) {
            App.SaveApplicationSettings();
          }
          return settings_.Clone();
        }

        return null;
      },
      () => optionsPanelWindow_ = null);
  }

  public async Task LoadSourceFile(IRTextSection section, ProfileSampleFilter profileFilter = null) {
    section_ = section;
    await LoadSourceFileForFunction(section_.ParentFunction, profileFilter);
  }

  public override async void OnDocumentSectionLoaded(IRTextSection section, IRDocument document) {
    base.OnDocumentSectionLoaded(section, document);
    ProfileTextView.AssociatedDocument = document;
    await LoadSourceFile(section);
  }

  private async Task<bool> LoadSourceFileForFunction(IRTextFunction function, ProfileSampleFilter profileFilter = null) {
    if (!ShouldReloadFunction(function, profileFilter)) {
      return true;
    }

    // Get the associated source file from the debug info if available,
    // since it also includes the start line number.
    string failureText = null;
    var (sourceInfo, debugInfo) = await sourceFileFinder_.FindLocalSourceFile(function);

    if (!sourceInfo.IsUnknown) {
      if (await ProfileTextView.LoadSourceFile(sourceInfo, section_, profileFilter)) {
        HandleLoadedSourceFile(sourceInfo, function);
        return true;
      }

      failureText = $"Could not find local copy of source file:\n{sourceInfo.FilePath}";
    }
    else {
      failureText = $"Could not find debug info for function:\n{function.Name}";
    }

    await HandleMissingSourceFile(failureText);
    return false;
  }

  private async Task<bool> LoadSourceFileForInlinee(SourceStackFrame inlinee, ProfileSampleFilter profileFilter = null) {
    if (!ShouldReloadInlinee(inlinee, profileFilter)) {
      return true;
    }

    // Get the associated source file from the debug info if available,
    // since it also includes the start line number.
    string failureText = null;
    var inlineeSourceInfo = new SourceFileDebugInfo(inlinee.FilePath, inlinee.FilePath);
    var (sourceInfo, debugInfo) = await sourceFileFinder_.FindLocalSourceFile(inlineeSourceInfo);

    if (!sourceInfo.IsUnknown) {
      if (await ProfileTextView.LoadSourceFile(sourceInfo, section_, profileFilter, inlinee)) {
        HandleLoadedSourceFile(sourceInfo, null);
        return true;
      }

      failureText = $"Could not find local copy of source file:\n{inlinee.FilePath}";
    }
    else {
      failureText = $"Could not find debug info for function:\n{inlinee.Function}";
    }

    await HandleMissingSourceFile(failureText);
    return false;
  }

  private bool ShouldReloadFunction(IRTextFunction function, ProfileSampleFilter profileFilter) {
    if (!sourceFileLoaded_) {
      return true;
    }

    if (sourceFileFunc_ != function) {
      return true;
    }

    return HasProfileFilterChange(profileFilter);
  }

  private bool ShouldReloadInlinee(SourceStackFrame inlinee, ProfileSampleFilter profileFilter) {
    if (!sourceFileLoaded_ || currentInlinee_ == null) {
      return true;
    }

    if (!currentInlinee_.HasSameFunction(inlinee)) {
      return true;
    }

    return HasProfileFilterChange(profileFilter);
  }

  private bool HasProfileFilterChange(ProfileSampleFilter profileFilter) {
    if (profileFilter != null) {
      return !profileFilter.Equals(ProfileTextView.ProfileFilter);
    }
    else {
      // Filter is being removed.
      return ProfileTextView.ProfileFilter != null;
    }
  }

    private void HandleLoadedSourceFile(SourceFileDebugInfo sourceInfo, IRTextFunction function) {
    SetPanelName(sourceInfo.OriginalFilePath);
    sourceFileLoaded_ = true;
    sourceFileFunc_ = function;
    sourceFilePath_ = sourceInfo.FilePath;
  }

  private async Task HandleMissingSourceFile(string failureText) {
    await ProfileTextView.HandleMissingSourceFile(failureText);
    SetPanelName("");
    sourceFileLoaded_ = false;
    sourceFileFunc_ = null;
  }

  public override void OnDocumentSectionUnloaded(IRTextSection section, IRDocument document) {
    base.OnDocumentSectionUnloaded(section, document);
    ResetState();
  }

  private void ResetState() {
    ProfileTextView.Reset();
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
      if (tag.HasInlinees && settings_.SyncInlineeWithDocument) {
        // Display deepest inlinee instead.
        if (await LoadInlineeSourceFile(tag)) {
          return;
        }
      }
      else {
        ResetInlinee();
      }

      if (await LoadSourceFileForFunction(section_.ParentFunction, ProfileTextView.ProfileFilter)) {
        if (settings_.SyncLineWithDocument) {
          ProfileTextView.SelectLine(tag.Line);
        }
      }
    }
  }

  private async Task<bool> LoadInlineeSourceFile(SourceLocationTag tag) {
    var selectedInlinee = PopulateInlineePicker(tag);
    return await LoadInlineeSourceFile(selectedInlinee);
  }

  private SourceStackFrame PopulateInlineePicker(SourceLocationTag tag) {
    disableInlineeComboboxEvents_ = true;
    var inlinees = tag.InlineesReversed;

    // Add an entry that means "no inlinee" in the front.
    inlinees.Insert(0, new SourceStackFrame("No Inlinee", "", 0,0));
    InlineeComboBox.ItemsSource = new ListCollectionView(inlinees);
    OnPropertyChanged(nameof(HasInlinees));

    var selectedInlinee = inlinees[^1];
    InlineeComboBox.SelectedItem = selectedInlinee;
    disableInlineeComboboxEvents_ = false;
    return selectedInlinee;
  }

  private void ResetInlinee() {
    InlineeComboBox.ItemsSource = null;
    currentInlinee_ = null;
    OnPropertyChanged(nameof(HasInlinees));
  }

  public async Task<bool> LoadInlineeSourceFile(SourceStackFrame inlinee) {
    if (inlinee == currentInlinee_) {
      return true;
    }

    bool fileLoaded = await LoadSourceFileForInlinee(inlinee);

    if (fileLoaded) {
      ProfileTextView.SelectLine(inlinee.Line);
    }

    currentInlinee_ = inlinee;
    return fileLoaded;
  }

  public override void OnSessionEnd() {
    base.OnSessionEnd();
    ResetState();
    ProfileTextView.SetSourceText("", "");
  }

  private async void PanelToolbarTray_OnHelpClicked(object sender, EventArgs e) {
    await HelpPanel.DisplayPanelHelp(PanelKind, Session);
  }

  private void PanelToolbarTray_OnSettingsClicked(object sender, EventArgs e) {
    ShowOptionsPanel();
  }

  public override async Task OnReloadSettings() {
    Settings = App.Settings.SourceFileSettings;
  }

  private void CopySelectedLinesAsHtmlExecuted(object sender, ExecutedRoutedEventArgs e) {
    //ProfileTextView.CopySelectionDetails();
  }

  private async void OpenPopupButton_Click(object sender, RoutedEventArgs e) {
    if (ProfileTextView.Section == null) {
      return; //? TODO: Button should rather be disabled
    }

    await IRDocumentPopupInstance.ShowPreviewPopup(ProfileTextView.Section.ParentFunction, "",
                                                   this, Session, ProfileTextView.ProfileFilter, true);

  }
}