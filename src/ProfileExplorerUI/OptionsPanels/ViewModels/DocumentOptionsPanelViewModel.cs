// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Diagnostics.Runtime;
using ProfileExplorer.Core.Session;
using ProfileExplorer.Core.Settings;
using ProfileExplorer.UI.Controls;
using ProfileExplorer.UI.Services;
using ProfileExplorer.UI.Windows;

namespace ProfileExplorer.UI.OptionsPanels;

public partial class SyntaxFileInfoViewModel : ObservableObject {
  private readonly SyntaxFileInfo syntaxFileInfo_;

  [ObservableProperty]
  private string name_;

  [ObservableProperty]
  private string path_;

  public SyntaxFileInfoViewModel(SyntaxFileInfo syntaxFileInfo) {
    syntaxFileInfo_ = syntaxFileInfo;
    Name_ = syntaxFileInfo.Name;
    Path_ = syntaxFileInfo.Path;
  }
}

public partial class SyntaxHighlightingColorPickerViewModel : ObservableObject {
  [ObservableProperty]
  private string name_;

  [ObservableProperty]
  private Color value_;

  public SyntaxHighlightingColorPickerViewModel(string name, Color value) {
    Name_ = name;
    Value_ = value;
  }
}

public class DocumentColorStyle {
  public DocumentColorStyle(string name) {
    Name = name;
    Colors = new Dictionary<string, Color>();
  }

  public string Name { get; set; }
  public Dictionary<string, Color> Colors { get; set; }
}

public partial class StyleButtonContextMenuViewModel : ObservableObject {
  private DocumentOptionsPanelViewModel parent_;

  [ObservableProperty]
  private string header_;

  [ObservableProperty]
  private DocumentColorStyle tag_;

  public StyleButtonContextMenuViewModel(DocumentOptionsPanelViewModel parent, DocumentColorStyle style) { 
    parent_ = parent;
    Header_ = style.Name; 
    Tag_ = style;
  }

  [RelayCommand]
  public void SelectItem() {
    parent_.BackgroundColor_ = Tag_.Colors["BackgroundColor"];
    parent_.AlternateBackgroundColor_ = Tag_.Colors["AlternateBackgroundColor"];
    parent_.MarginBackgroundColor_ = Tag_.Colors["MarginBackgroundColor"];
    parent_.BlockSeparatorColor_ = Tag_.Colors["BlockSeparatorColor"];
    parent_.TextColor_ = Tag_.Colors["TextColor"];
    parent_.SelectedValueColor_ = Tag_.Colors["SelectedValueColor"];
    parent_.DefinitionValueColor_ = Tag_.Colors["DefinitionValueColor"];
    parent_.UseValueColor_ = Tag_.Colors["UseValueColor"];
    parent_.BorderColor_ = Tag_.Colors["BorderColor"];
  }
}

public partial class DocumentOptionsPanelViewModel : OptionsPanelBaseViewModel<DocumentSettings> {
  private const string DocumentStylesFilePath = @"documentStyles.xml";

  private ISyntaxHighlightingFileCache syntaxHighlightingFileCache_;
  private ISettingsDirectoryLauncher settingsDirectoryLauncher_;
  private ISettingsFileLauncher settingsFileLauncher_;
  private IConfirmationProvider confirmationProvider_;
  private DocumentColorStyle syntaxHighlightingStyle_;

  [ObservableProperty]
  private DocumentProfilingOptionsPanelViewModel documentProfilingOptionsPanelViewModel_;

  [ObservableProperty]
  private ObservableCollection<SyntaxFileInfoViewModel> syntaxFileInfoOptions_;

  [ObservableProperty]
  private SyntaxFileInfoViewModel selectedSyntaxFileInfo_;

  [ObservableProperty]
  private bool editSyntaxButtonIsChecked_;

  [ObservableProperty]
  private Visibility syntaxHighlightingPanelVisibility_;

  [ObservableProperty]
  private ObservableCollection<SyntaxHighlightingColorPickerViewModel> syntaxHighlightingColorPickers_;

  [ObservableProperty]
  private string fontName_;

  [ObservableProperty]
  private double fontSize_;

  [ObservableProperty]
  private bool highlightCurrentLine_;

  [ObservableProperty]
  private bool showBlockSeparatorLine_;

  [ObservableProperty]
  private bool showBlockFolding_;

  [ObservableProperty]
  private bool highlightSourceDefinition_;

  [ObservableProperty]
  private bool filterSourceDefinitions_;

  [ObservableProperty]
  private bool highlightDestinationUses_;

  [ObservableProperty]
  private bool filterDestinationUses_;

  [ObservableProperty]
  private bool highlightInstructionOperands_;

  [ObservableProperty]
  private bool showInfoOnHover_;

  [ObservableProperty]
  private bool showPreviewPopup_;

  [ObservableProperty]
  private bool showInfoOnHoverWithModifier_;

  [ObservableProperty]
  private bool styleButtonContextMenuIsOpen_;

  [ObservableProperty]
  private ObservableCollection<StyleButtonContextMenuViewModel> styleButtonContextMenuItems_;

  [ObservableProperty]
  private Color backgroundColor_;

  [ObservableProperty]
  private Color alternateBackgroundColor_;

  [ObservableProperty]
  private Color textColor_;

  [ObservableProperty]
  private Color currentLineBorderColor_;

  [ObservableProperty]
  private Color blockSeparatorColor_;

  [ObservableProperty]
  private Color marginBackgroundColor_;

  [ObservableProperty]
  private Color selectedValueColor_;

  [ObservableProperty]
  private Color definitionValueColor_;

  [ObservableProperty]
  private Color useValueColor_;

  [ObservableProperty]
  private Color borderColor_;

  [ObservableProperty]
  private bool annotateSourceLines_;

  [ObservableProperty]
  private bool annotateInlinees_;

  [ObservableProperty]
  private bool markCallTargets_;

  [ObservableProperty]
  private Color sourceLineTextColor_;

  [ObservableProperty]
  private Color sourceLineBackColor_;

  [ObservableProperty]
  private Color inlineeOverlayTextColor_;

  [ObservableProperty]
  private Color inlineeOverlayBackColor_;

  [ObservableProperty]
  private Color callTargetTextColor_;

  [ObservableProperty]
  private Color callTargetBackColor_;

  public override void Initialize(FrameworkElement parent, DocumentSettings settings, IUISession session) {
    base.Initialize(parent, settings, session);

    syntaxHighlightingFileCache_ = new SyntaxHighlightingFileCache();
    settingsDirectoryLauncher_ = new SettingsDirectoryLauncher();
    settingsFileLauncher_ = new SettingsFileLauncher();
    confirmationProvider_ = new DialogConfirmationProvider(parent);

    DocumentProfilingOptionsPanelViewModel_ = new DocumentProfilingOptionsPanelViewModel();
    DocumentProfilingOptionsPanelViewModel_.Initialize(parent, settings.ProfileMarkerSettings, session);
    DocumentProfilingOptionsPanelViewModel_.ShowsDocumentSettings_ = true;

    try {
      var docStyles = LoadDocumentStyles(DocumentStylesFilePath);
      StyleButtonContextMenuItems_ = new ObservableCollection<StyleButtonContextMenuViewModel>(docStyles
        .Select(x => new StyleButtonContextMenuViewModel(this, x)));
    }
    catch (Exception ex) {
      Trace.TraceError($"Failed to load style document XML: {ex}");
    }

    SyntaxFileInfoOptions_ = new ObservableCollection<SyntaxFileInfoViewModel>(syntaxHighlightingFileCache_.ReloadSyntaxHighlightingFiles()
      .Select(x => new SyntaxFileInfoViewModel(x)));
    SyntaxHighlightingPanelVisibility_ = Visibility.Collapsed;

    // Populate view model properties from settings
    PopulateFromSettings(settings);
  }

  private void PopulateFromSettings(DocumentSettings settings) {
    SelectedSyntaxFileInfo_ = SyntaxFileInfoOptions_.FirstOrDefault(x => x.Name_ == settings.SyntaxHighlightingName);

    string filePath = syntaxHighlightingFileCache_.GetSyntaxHighlightingFilePath(SelectedSyntaxFileInfo_.Path_);
    syntaxHighlightingStyle_ = ApplySyntaxHighlightingStyles(filePath);
    SyntaxHighlightingColorPickers_ = new ObservableCollection<SyntaxHighlightingColorPickerViewModel>(
      syntaxHighlightingStyle_.Colors.Select(x => new SyntaxHighlightingColorPickerViewModel(x.Key, x.Value)));

    FontName_ = settings.FontName;
    FontSize_ = settings.FontSize;
    HighlightCurrentLine_ = settings.HighlightCurrentLine;
    ShowBlockSeparatorLine_ = settings.ShowBlockSeparatorLine;
    ShowBlockFolding_ = settings.ShowBlockFolding;
    HighlightSourceDefinition_ = settings.HighlightSourceDefinition;
    FilterSourceDefinitions_ = settings.FilterSourceDefinitions;
    HighlightDestinationUses_ = settings.HighlightDestinationUses;
    FilterDestinationUses_ = settings.FilterDestinationUses;
    HighlightInstructionOperands_ = settings.HighlightInstructionOperands;
    ShowInfoOnHover_ = settings.ShowInfoOnHover;
    ShowInfoOnHoverWithModifier_ = settings.ShowInfoOnHoverWithModifier;
    ShowPreviewPopup_ = settings.ShowPreviewPopup;

    BackgroundColor_ = settings.BackgroundColor;
    AlternateBackgroundColor_ = settings.AlternateBackgroundColor;
    TextColor_ = settings.TextColor;
    CurrentLineBorderColor_ = settings.CurrentLineBorderColor;
    BlockSeparatorColor_ = settings.BlockSeparatorColor;
    MarginBackgroundColor_ = settings.MarginBackgroundColor;
    SelectedValueColor_ = settings.SelectedValueColor;
    DefinitionValueColor_ = settings.DefinitionValueColor;
    UseValueColor_ = settings.UseValueColor;
    BorderColor_ = settings.BorderColor;

    AnnotateSourceLines_ = settings.SourceMarkerSettings.AnnotateSourceLines;
    AnnotateInlinees_ = settings.SourceMarkerSettings.AnnotateInlinees;
    MarkCallTargets_ = settings.SourceMarkerSettings.MarkCallTargets;
    SourceLineTextColor_ = settings.SourceMarkerSettings.SourceLineTextColor;
    SourceLineBackColor_ = settings.SourceMarkerSettings.SourceLineBackColor;
    InlineeOverlayTextColor_ = settings.SourceMarkerSettings.InlineeOverlayTextColor;
    InlineeOverlayBackColor_ = settings.SourceMarkerSettings.InlineeOverlayBackColor;
    CallTargetTextColor_ = settings.SourceMarkerSettings.CallTargetTextColor;
    CallTargetBackColor_ = settings.SourceMarkerSettings.CallTargetBackColor;
  }

  partial void OnSelectedSyntaxFileInfo_Changed(SyntaxFileInfoViewModel value) {
    if (value == null) {
      return;
    }

    string filePath = syntaxHighlightingFileCache_.GetSyntaxHighlightingFilePath(value.Path_);
    var syntaxHighlightingStyle = ApplySyntaxHighlightingStyles(filePath);
    SyntaxHighlightingColorPickers_ = new ObservableCollection<SyntaxHighlightingColorPickerViewModel>(
      syntaxHighlightingStyle.Colors.Select(x => new SyntaxHighlightingColorPickerViewModel(x.Key, x.Value)));
  }

  partial void OnEditSyntaxButtonIsChecked_Changed(bool value) {
    if (EditSyntaxButtonIsChecked_) {
      SyntaxHighlightingPanelVisibility_ = Visibility.Visible;
    }
    else {
      SyntaxHighlightingPanelVisibility_ = Visibility.Collapsed;
    }
  }

  [RelayCommand]
  public void OpenSyntaxStyle() {
    settingsDirectoryLauncher_.LaunchCompilerSettingsDirectory();
  }

  [RelayCommand]
  public void ReloadSyntaxStyle() {
    string syntaxHighlightingName = SelectedSyntaxFileInfo_.Name_;
    SyntaxFileInfoOptions_ = new ObservableCollection<SyntaxFileInfoViewModel>(syntaxHighlightingFileCache_.ReloadSyntaxHighlightingFiles()
      .Select(x => new SyntaxFileInfoViewModel(x)));
    SelectedSyntaxFileInfo_ = SyntaxFileInfoOptions_.FirstOrDefault(x => x.Name_ == syntaxHighlightingName);
  }

  [RelayCommand]
  public async void ResetSyntaxStyle() {
    if (!await confirmationProvider_.RequestConfirmation("Do you want to reset syntax highlighting style?", "Profile Explorer")) {
      return;
    }

    string path = syntaxHighlightingFileCache_.GetSyntaxHighlightingFilePath(SelectedSyntaxFileInfo_.Name_);

    if (path != null) {
      var syntaxHighlightingStyle_ = ApplySyntaxHighlightingStyles(path);
      SyntaxHighlightingColorPickers_ = new ObservableCollection<SyntaxHighlightingColorPickerViewModel>(
        syntaxHighlightingStyle_.Colors.Select(x => new SyntaxHighlightingColorPickerViewModel(x.Key, x.Value)));
      CreateSyntaxHighlightingStyle();
    }
  }

  private bool CreateSyntaxHighlightingStyle() {
    if (SelectedSyntaxFileInfo_ == null) {
      return false;
    }

    foreach (var colorPicker in SyntaxHighlightingColorPickers_) {
      syntaxHighlightingStyle_.Colors[colorPicker.Name_] = colorPicker.Value_;
    }

    string newSyntaxFile = syntaxHighlightingFileCache_.GetSyntaxHighlightingFilePath(SelectedSyntaxFileInfo_.Path_);
    ApplySyntaxHighlightingStyles(SelectedSyntaxFileInfo_.Path_, newSyntaxFile, syntaxHighlightingStyle_);
    return true;
  }

  [RelayCommand]
  public void EditSyntaxFile() {
    settingsFileLauncher_.LaunchSettingsFile(SelectedSyntaxFileInfo_.Path_);
  }

  private DocumentColorStyle ApplySyntaxHighlightingStyles(string stylePath,
                                                           string outputStylePath = null,
                                                           DocumentColorStyle replacementStyles = null) {
    var xmlDoc = new XmlDocument();
    xmlDoc.Load(stylePath);
    var root = xmlDoc.DocumentElement;
    string name = root.Attributes.GetNamedItem("name").InnerText;
    var docStyle = new DocumentColorStyle(name);

    foreach (XmlNode node in root.ChildNodes) {
      if (node.Name != "Color") {
        continue;
      }

      string colorName = node.Attributes.GetNamedItem("name").InnerText;
      var colorNode = node.Attributes.GetNamedItem("foreground");

      if (replacementStyles != null) {
        if (!replacementStyles.Colors.ContainsKey(colorName)) {
          continue;
        }

        var newColor = replacementStyles.Colors[colorName];
        colorNode.Value = $"#{newColor.R:X2}{newColor.G:X2}{newColor.B:X2}";
      }
      else {
        docStyle.Colors[colorName] = Utils.ColorFromString(colorNode.InnerText);
      }
    }

    if (outputStylePath != null) {
      xmlDoc.Save(outputStylePath);
    }

    return docStyle;
  }

  [RelayCommand]
  public void ShowStyleContextMenu() {
    StyleButtonContextMenuIsOpen_ = true;
  }

  private List<DocumentColorStyle> LoadDocumentStyles(string stylePath) {
    //? TODO: This should be a JSOn doc, easier to read and same foramt as other settings
    var xmlDoc = new XmlDocument();
    xmlDoc.Load(stylePath);
    var docStyles = new List<DocumentColorStyle>();
    var styles = xmlDoc.SelectNodes("/SyntaxDefinitions/SyntaxDefinition");

    foreach (XmlNode style in styles) {
      string name = style.Attributes.GetNamedItem("name").InnerText;
      var docStyle = new DocumentColorStyle(name);
      docStyles.Add(docStyle);

      foreach (XmlNode color in style.ChildNodes) {
        string colorName = color.Attributes.GetNamedItem("name").InnerText;
        string colorValue = color.Attributes.GetNamedItem("background").InnerText;
        docStyle.Colors[colorName] = Utils.ColorFromString(colorValue);
      }
    }

    return docStyles;
  }

  partial void OnStyleButtonContextMenuIsOpen_Changed(bool value) {
    if (parent_ is OptionsPanelHostPopup popup) {
      if (value) {
        popup.StaysOpen = true;
      }
      else {
        popup.StaysOpen = false;
      }
    }
  }

  public override void SaveSettings() {
    if (documentProfilingOptionsPanelViewModel_ != null) {
      documentProfilingOptionsPanelViewModel_.SaveSettings();
    }

    if (Settings_ != null) {
      Settings_.SyntaxHighlightingName = SelectedSyntaxFileInfo_.Name_;
      Settings_.FontName = FontName_;
      Settings_.FontSize = FontSize_;
      Settings_.HighlightCurrentLine = HighlightCurrentLine_;
      Settings_.ShowBlockSeparatorLine = ShowBlockSeparatorLine_;
      Settings_.ShowBlockFolding = ShowBlockFolding_;
      Settings_.HighlightSourceDefinition = HighlightSourceDefinition_;
      Settings_.FilterSourceDefinitions = FilterSourceDefinitions_;
      Settings_.HighlightDestinationUses = HighlightDestinationUses_;
      Settings_.FilterDestinationUses = FilterDestinationUses_;
      Settings_.HighlightInstructionOperands = HighlightInstructionOperands_;
      Settings_.ShowInfoOnHover = ShowInfoOnHover_;
      Settings_.ShowInfoOnHoverWithModifier = ShowInfoOnHoverWithModifier_;
      Settings_.ShowPreviewPopup = ShowPreviewPopup_;

      Settings_.BackgroundColor = BackgroundColor_;
      Settings_.AlternateBackgroundColor = AlternateBackgroundColor_;
      Settings_.TextColor = TextColor_;
      Settings_.CurrentLineBorderColor = CurrentLineBorderColor_;
      Settings_.BlockSeparatorColor = BlockSeparatorColor_;
      Settings_.MarginBackgroundColor = MarginBackgroundColor_;
      Settings_.SelectedValueColor = SelectedValueColor_;
      Settings_.DefinitionValueColor = DefinitionValueColor_;
      Settings_.UseValueColor = UseValueColor_;
      Settings_.BorderColor = BorderColor_;

      Settings_.SourceMarkerSettings.AnnotateSourceLines = AnnotateSourceLines_;
      Settings_.SourceMarkerSettings.AnnotateInlinees = AnnotateInlinees_;
      Settings_.SourceMarkerSettings.MarkCallTargets = MarkCallTargets_;
      Settings_.SourceMarkerSettings.SourceLineTextColor = SourceLineTextColor_;
      Settings_.SourceMarkerSettings.SourceLineBackColor = SourceLineBackColor_;
      Settings_.SourceMarkerSettings.InlineeOverlayTextColor = InlineeOverlayTextColor_;
      Settings_.SourceMarkerSettings.InlineeOverlayBackColor = InlineeOverlayBackColor_;
      Settings_.SourceMarkerSettings.CallTargetTextColor = CallTargetTextColor_;
      Settings_.SourceMarkerSettings.CallTargetBackColor = CallTargetBackColor_;
    }
  }

  /// <summary>
  /// Gets the current settings with all changes applied
  /// </summary>
  public DocumentSettings GetCurrentSettings() {
    SaveSettings(); // Ensure all changes are saved to the settings object
    return Settings_;
  }

  /// <summary>
  /// Resets the settings to default values
  /// </summary>
  public void ResetSettings() {
    if (Settings_ != null) {
      // Reset to defaults - create a new default settings object
      var defaultSettings = new DocumentSettings();
      Settings_ = defaultSettings;
      PopulateFromSettings(Settings_);
    }
  }

  /// <summary>
  /// Called when the panel is about to close
  /// </summary>
  public void PanelClosing() {
    // Ensure settings are saved before closing
    SaveSettings();
    // Any other cleanup can be added here
  }
}