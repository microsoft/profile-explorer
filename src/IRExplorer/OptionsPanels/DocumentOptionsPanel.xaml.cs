// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using IRExplorer.OptionsPanels;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
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
                return new FontFamily((string) value);
            }
            catch (Exception ex) {
                return null;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            return ((FontFamily) value).Source;
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

        public DocumentOptionsPanel() {
            InitializeComponent();
            PreviewMouseUp += DocumentOptionsPanel_PreviewMouseUp;
            PreviewKeyUp += DocumentOptionsPanel_PreviewKeyUp;
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
            var style = ((MenuItem) sender).Tag as DocumentColorStyle;
            ApplyDocumentStyle(style);
        }

        private void ApplyDocumentStyle(DocumentColorStyle style) {
            var settings = (DocumentSettings) DataContext;
            settings.BackgroundColor = style.Colors["BackgroundColor"];
            settings.AlternateBackgroundColor = style.Colors["AlternateBackgroundColor"];
            settings.MarginBackgroundColor = style.Colors["MarginBackgroundColor"];
            settings.BlockSeparatorColor = style.Colors["BlockSeparatorColor"];
            settings.TextColor = style.Colors["TextColor"];
            settings.SelectedValueColor = style.Colors["SelectedValueColor"];
            settings.DefinitionValueColor = style.Colors["DefinitionValueColor"];
            settings.UseValueColor = style.Colors["UseValueColor"];
            settings.BorderColor = style.Colors["BorderColor"];
            DataContext = null;
            DataContext = settings;
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
            if (syntaxHighlightingStyle_ == null) {
                return false;
            }

            foreach (var info in syntaxHighlightingColors_) {
                syntaxHighlightingStyle_.Colors[info.Name] = info.Value;
            }

            var settings = (DocumentSettings) DataContext;
            string inputFile = App.GetDefaultSyntaxHighlightingFilePath();

            if (string.IsNullOrEmpty(settings.SyntaxHighlightingFilePath)) {
                string docFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

                settings.SyntaxHighlightingFilePath =
                    Path.Combine(docFolder, $"{syntaxHighlightingStyle_.Name}.xshd");
            }

            ProcessSyntaxHighlightingStyles(inputFile, settings.SyntaxHighlightingFilePath,
                                            syntaxHighlightingStyle_);

            return true;
        }

        private DocumentColorStyle ProcessSyntaxHighlightingStyles(string stylePath,
                                                                   string outputStylePath = null,
                                                                   DocumentColorStyle replacementStyles =
                                                                       null) {
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

        private async void SyntaxEditButton_Click(object sender, RoutedEventArgs e) {
            if (!syntaxEditPanelVisible_) {
                ShowSyntaxEditPanel(null);
            }
            else {
                HideSyntaxEditPanel();
            }
        }

        private void ShowSyntaxEditPanel(string filePath) {
            filePath ??= App.GetSyntaxHighlightingFilePath();
            var utcStyle = ProcessSyntaxHighlightingStyles(filePath);
            PopulateSyntaxHighlightingColorPickers(utcStyle);
            SyntaxHighlightingPanel.Visibility = Visibility.Visible;
            SyntaxEditButton.IsChecked = true;
            syntaxEditPanelVisible_ = true;
        }

        private void HideSyntaxEditPanel(bool reset = false) {
            SyntaxHighlightingPanel.Visibility = Visibility.Collapsed;
            SyntaxEditButton.IsChecked = false;
            syntaxEditPanelVisible_ = false;

            if (reset) {
                syntaxHighlightingStyle_ = null;
            }
        }

        private void SyntaxStyleButton_Click(object sender, RoutedEventArgs e) {
            try {
                var themes = App.GetSyntaxHighlightingThemes();
                SyntaxStyleContextMenu.Items.Clear();

                foreach (var theme in themes) {
                    var menuItem = new MenuItem();
                    menuItem.Header = theme.Name;
                    menuItem.Tag = theme;
                    menuItem.Click += SyntaxStyleContextMenuItem_Click;
                    SyntaxStyleContextMenu.Items.Add(menuItem);
                }

                SyntaxStyleContextMenu.IsOpen = true;
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed to load syntax theme list: {ex}");
            }
        }

        private void SyntaxStyleContextMenuItem_Click(object sender, RoutedEventArgs e) {
            var theme = ((MenuItem) sender).Tag as SyntaxThemeInfo;
            ShowSyntaxEditPanel(theme.Path);

            //? TODO: There must be an association between theme and preferred style,
            //? another XMl doc describing the themes, including the IR they apply to
            var styles = LoadDocumentStyles(DocumentStylesFilePath);
            var darkStyle = styles.Find(item => item.Name == "Dark");
            ApplyDocumentStyle(darkStyle);
            NotifySettingsChanged();
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
    }
}
