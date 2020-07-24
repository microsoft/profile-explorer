// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml;

namespace IRExplorer.OptionsPanels {
    public class FontFamilyConverter : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            try {
                return new FontFamily((string)value);
            }
            catch (Exception ex) {
                return null;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            return ((FontFamily)value).Source;
        }
    }

    public partial class DocumentOptionsPanel : OptionsPanelBase {
        public const double DefaultHeight = 500;
        public const double MinimumHeight = 300;
        public const double DefaultWidth = 360;
        public const double MinimumWidth = 360;

        private const string DocumentStylesFilePath = @"documentStyles.xml";
        private bool syntaxEditPanelVisible_;

        private List<ColorPickerInfo> syntaxHighlightingColors_;
        private DocumentColorStyle syntaxHighlightingStyle_;
        private List<SyntaxFileInfo> syntaxFiles_;
        private SyntaxFileInfo selectedSyntaxFile_;
        private DocumentSettings settings_;

        public DocumentOptionsPanel() {
            InitializeComponent();
            PreviewMouseUp += DocumentOptionsPanel_PreviewMouseUp;
            PreviewKeyUp += DocumentOptionsPanel_PreviewKeyUp;
        }

        public override void Initialize() {
            base.Initialize();
            settings_ = (DocumentSettings)Settings;
            ReloadSyntaxHighlightingList();
        }

        private void ReloadSyntaxHighlightingList() {
            syntaxFiles_ = App.ReloadSyntaxHighlightingFiles(App.Session.CompilerInfo.CompilerIRName);
            selectedSyntaxFile_ = App.GetSyntaxHighlightingFileInfo(settings_.SyntaxHighlightingName,
                                                                    App.Session.CompilerInfo.CompilerIRName);

            // Unbind the combobox event while loading the list
            // so that it doesn't change the selected syntax file.
            IRSyntaxCombobox.SelectionChanged -= IRSyntaxCombobox_SelectionChanged;
            IRSyntaxCombobox.ItemsSource = new CollectionView(syntaxFiles_);
            IRSyntaxCombobox.SelectedItem = selectedSyntaxFile_;
            IRSyntaxCombobox.SelectionChanged += IRSyntaxCombobox_SelectionChanged;
        }

        public override void OnSettingsChanged(object newSettings) {
            settings_ = (DocumentSettings)newSettings;
            ReloadSyntaxHighlightingList();
        }

        public bool SyntaxFileChanged { get; set; }

        private void DocumentOptionsPanel_PreviewKeyUp(object sender, KeyEventArgs e) {
            NotifySettingsChanged();
        }

        private void DocumentOptionsPanel_PreviewMouseUp(object sender, MouseButtonEventArgs e) {
            NotifySettingsChanged();
        }

        private void NotifySettingsChanged() {
            DelayedAction.StartNew(TimeSpan.FromMilliseconds(100), () => {
                SyntaxFileChanged = DataContext != null && UpdateSyntaxHighlightingStyle();
                RaiseSettingsChanged(null);
            });
        }

        public override void PanelClosing() {
            SyntaxFileChanged = UpdateSyntaxHighlightingStyle();
        }

        public override void PanelResetting() {
            syntaxHighlightingStyle_ = null;
            syntaxHighlightingColors_ = null;
        }

        public override void PanelResetted() {
            if (syntaxEditPanelVisible_) {
                ShowSyntaxEditPanel(null);
            }
        }

        private void StyleButton_Click(object sender, RoutedEventArgs e) {
            try {
                var docStyles = LoadDocumentStyles(DocumentStylesFilePath);
                StyleContextMenu.Items.Clear();

                foreach (var style in docStyles) {
                    var menuItem = new MenuItem();
                    menuItem.Header = style.Name;
                    menuItem.Tag = style;
                    menuItem.Click += StyleContextMenuItem_Click;
                    StyleContextMenu.Items.Add(menuItem);
                }

                StyleContextMenu.IsOpen = true;
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed to load style document XML: {ex}");
            }
        }

        private void StyleContextMenuItem_Click(object sender, RoutedEventArgs e) {
            var style = ((MenuItem)sender).Tag as DocumentColorStyle;
            ApplyDocumentStyle(style);
        }

        private void ApplyDocumentStyle(DocumentColorStyle style) {
            settings_.BackgroundColor = style.Colors["BackgroundColor"];
            settings_.AlternateBackgroundColor = style.Colors["AlternateBackgroundColor"];
            settings_.MarginBackgroundColor = style.Colors["MarginBackgroundColor"];
            settings_.BlockSeparatorColor = style.Colors["BlockSeparatorColor"];
            settings_.TextColor = style.Colors["TextColor"];
            settings_.SelectedValueColor = style.Colors["SelectedValueColor"];
            settings_.DefinitionValueColor = style.Colors["DefinitionValueColor"];
            settings_.UseValueColor = style.Colors["UseValueColor"];
            settings_.BorderColor = style.Colors["BorderColor"];
            DataContext = null;
            DataContext = settings_;
            NotifySettingsChanged();
        }

        private void PopulateSyntaxHighlightingColorPickers(DocumentColorStyle style) {
            syntaxHighlightingStyle_ = style;
            syntaxHighlightingColors_ = new List<ColorPickerInfo>();

            foreach (var pair in style.Colors) {
                syntaxHighlightingColors_.Add(new ColorPickerInfo(pair.Key, pair.Value));
            }

            SyntaxHighlightingColorPickers.ItemsSource = new CollectionView(syntaxHighlightingColors_);
        }

        private bool UpdateSyntaxHighlightingStyle() {
            if (selectedSyntaxFile_ == null) {
                return false; // Happens if there are no syntax files found.
            }

            return CreateSyntaxHighlightingStyle(selectedSyntaxFile_.Path, selectedSyntaxFile_.Path);
        }

        private bool CreateSyntaxHighlightingStyle(string inputFile, string outputFile) {
            if (syntaxHighlightingStyle_ == null) {
                return false;
            }

            foreach (var info in syntaxHighlightingColors_) {
                syntaxHighlightingStyle_.Colors[info.Name] = info.Value;
            }

            var newSyntaxFile = App.GetSyntaxHighlightingFilePath(outputFile, App.Session.CompilerInfo.CompilerIRName);
            ApplySyntaxHighlightingStyles(inputFile, newSyntaxFile, syntaxHighlightingStyle_);
            return true;
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

        private void SyntaxEditButton_Click(object sender, RoutedEventArgs e) {
            if (!syntaxEditPanelVisible_) {
                ShowSyntaxEditPanel(null);
            }
            else {
                HideSyntaxEditPanel();
            }
        }

        private void ShowSyntaxEditPanel(string filePath, bool force = false) {
            if (syntaxEditPanelVisible_ && !force) {
                return;
            }

            filePath ??= App.GetSyntaxHighlightingFilePath(selectedSyntaxFile_);
            var utcStyle = ApplySyntaxHighlightingStyles(filePath);
            PopulateSyntaxHighlightingColorPickers(utcStyle);
            SyntaxHighlightingPanel.Visibility = Visibility.Visible;
            SyntaxEditButton.IsChecked = true;
            syntaxEditPanelVisible_ = true;
        }

        private void HideSyntaxEditPanel(bool reset = false) {
            if (!syntaxEditPanelVisible_) {
                return;
            }

            SyntaxHighlightingPanel.Visibility = Visibility.Collapsed;
            SyntaxEditButton.IsChecked = false;
            syntaxEditPanelVisible_ = false;

            if (reset) {
                syntaxHighlightingStyle_ = null;
            }
        }

        private class DocumentColorStyle {
            public DocumentColorStyle(string name) {
                Name = name;
                Colors = new Dictionary<string, Color>();
            }

            public string Name { get; set; }
            public Dictionary<string, Color> Colors { get; set; }
        }

        private class ColorPickerInfo {
            public ColorPickerInfo(string name, Color value) {
                Name = name;
                Value = value;
            }

            public string Name { get; set; }
            public Color Value { get; set; }
        }

        private void IRSyntaxCombobox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (IRSyntaxCombobox.SelectedItem != null) {
                bool syntaxPanelVisible = syntaxEditPanelVisible_;
                HideSyntaxEditPanel();

                selectedSyntaxFile_ = (SyntaxFileInfo)IRSyntaxCombobox.SelectedItem;
                settings_.SyntaxHighlightingName = selectedSyntaxFile_.Name;

                if (syntaxPanelVisible) {
                    ShowSyntaxEditPanel(selectedSyntaxFile_.Path);
                }
            }
        }

        private void OpenSyntaxStyleButton_Click(object sender, RoutedEventArgs e) {
            var path = App.GetCompilerSettingsDirectoryPath(App.Session.CompilerInfo.CompilerIRName);
            App.OpenSettingsFolder(path);
        }

        private void EditSyntaxFileButton_Click(object sender, RoutedEventArgs e) {
            var path = selectedSyntaxFile_.Path;
            App.LaunchSettingsFileEditor(path);
        }

        private void ResetSyntaxStyleButton_Click(object sender, RoutedEventArgs e) {
            // Try to restore the internal syntax file.
            //? TODO: Message box shows under panel!

            //if (MessageBox.Show("Do you want to reset syntax highlighting style?", "IR Explorer",
            //    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) {
            //    return;
            //}

            var path = App.GetInternalSyntaxHighlightingFilePath(selectedSyntaxFile_.Name, App.Session.CompilerInfo.CompilerIRName);

            if (path != null) {
                ShowSyntaxEditPanel(path, true);
                UpdateSyntaxHighlightingStyle();
            }
        }

        private void ReloadSyntaxStyleButton_Click(object sender, RoutedEventArgs e) {
            ReloadSyntaxHighlightingList();
        }

        private void CloneSyntaxFileButton_Click(object sender, RoutedEventArgs e) {

        }
    }
}
