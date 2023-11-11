// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace IRExplorerUI.OptionsPanels {
    /// <summary>
    /// Interaction logic for RemarkOptionsPanel.xaml
    /// </summary>
    public partial class RemarkOptionsPanel : OptionsPanelBase {
        public const double DefaultHeight = 480;
        public const double MinimumHeight = 300;
        public const double DefaultWidth = 350;
        public const double MinimumWidth = 350;
        public const double LeftMargin = 200;

        public RemarkOptionsPanel() {
            InitializeComponent();
            PreviewMouseUp += RemarkOptionsPanel_PreviewMouseUp;
            PreviewKeyUp += RemarkOptionsPanel_PreviewKeyUp;

            kindCheckboxes_ = new List<CheckBox>();
            kindCheckboxes_.Add(OptimizationCheckbox);
            kindCheckboxes_.Add(AnalysisCheckbox);
            kindCheckboxes_.Add(StandardCheckbox);
            kindCheckboxes_.Add(VerboseCheckbox);
            kindCheckboxes_.Add(TraceCheckbox);
        }

        private List<CheckBox> kindCheckboxes_;
        private List<CheckBox> categoryCheckboxes_;

        private void SetCheckboxesState(List<CheckBox> list, bool state) {
            list.ForEach((item) => item.IsChecked = state);
        }

        private void RemarkOptionsPanel_PreviewKeyUp(object sender, KeyEventArgs e) {
            NotifySettingsChanged();
        }

        private void RemarkOptionsPanel_PreviewMouseUp(object sender, MouseButtonEventArgs e) {
            NotifySettingsChanged();
        }

        public override void Initialize(FrameworkElement parent) {
            base.Initialize(parent);
            PopulateCategoryList();
        }

        private bool PopulateCategoryList() {
            var remarkSettings = (RemarkSettings)DataContext;
            categoryCheckboxes_ = new List<CheckBox>();

            bool initialLoad = !remarkSettings.HasCategoryFilters;
            var categories = App.Session.CompilerInfo.RemarkProvider.RemarkCategories;

            if (categories == null) {
                using var centerForm = new DialogCenteringHelper(Parent);
                MessageBox.Show("Failed to load remark settings file,\ncheck JSON for any syntax errors!", "IR Explorer", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            CategoriesList.Children.Clear();

            foreach (var category in categories) {
                if (!string.IsNullOrEmpty(category.Title)) {
                    var checkbox = new CheckBox();
                    checkbox.Content = category.Title;
                    checkbox.Margin = new Thickness(0, 0, 0, 2);

                    if (category.AddTextMark) {
                        checkbox.Background = ColorBrushes.GetBrush(category.TextMarkBorderColor);
                    }
                    else if (category.AddLeftMarginMark) {
                        checkbox.Background = ColorBrushes.GetBrush(category.MarkColor);
                    }

                    if (initialLoad) {
                        checkbox.IsChecked = true;
                    }
                    else if (remarkSettings.CategoryFilter.TryGetValue(category.Title, out bool state)) {
                        checkbox.IsChecked = state;
                    }

                    checkbox.Tag = category;
                    checkbox.Checked += Checkbox_CheckedChanged;
                    checkbox.Unchecked += Checkbox_CheckedChanged;
                    CategoriesList.Children.Add(checkbox);
                    categoryCheckboxes_.Add(checkbox);
                }
            }

            return true;
        }

        private void Checkbox_CheckedChanged(object sender, RoutedEventArgs e) {
            UpdateCategoryFilter();
            NotifySettingsChanged();
        }

        private void UpdateCategoryFilter() {
            var remarkSettings = (RemarkSettings)DataContext;
            remarkSettings.CategoryFilter = new Dictionary<string, bool>();

            foreach (var checkbox in categoryCheckboxes_) {
                var category = (RemarkCategory)checkbox.Tag;
                remarkSettings.CategoryFilter[category.Title] = checkbox.IsChecked.HasValue && checkbox.IsChecked.Value;
            }
        }

        private void NotifySettingsChanged() {
            DelayedAction.StartNew(TimeSpan.FromMilliseconds(100), () => {
                RaiseSettingsChanged(null);
            });
        }

        private void SetAllKindCheckboxesButton_Click(object sender, RoutedEventArgs e) {
            SetCheckboxesState(kindCheckboxes_, true);
        }

        private void ResetAllKindCheckboxesButton_Click(object sender, RoutedEventArgs e) {
            SetCheckboxesState(kindCheckboxes_, false);
        }

        private void EditButton_Click(object sender, RoutedEventArgs e) {
            var settingsPath = App.GetRemarksDefinitionFilePath(App.Session.CompilerInfo.CompilerIRName);
            App.LaunchSettingsFileEditor(settingsPath);
        }

        private void SetAllCategoryCheckboxesButton_Click(object sender, RoutedEventArgs e) {
            SetCheckboxesState(categoryCheckboxes_, true);
        }

        private void ResetAllCategoryCheckboxesButton_Click(object sender, RoutedEventArgs e) {
            SetCheckboxesState(categoryCheckboxes_, false);
        }

        private void ReloadButton_Click(object sender, RoutedEventArgs e) {
            PopulateCategoryList();
        }
    }
}
