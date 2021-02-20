// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using IRExplorerUI.Controls;

namespace IRExplorerUI.OptionsPanels {
    public partial class RemarkOptionsPanel : OptionsPanelBase {
        public const double DefaultHeight = 480;
        public const double MinimumHeight = 300;
        public const double DefaultWidth = 350;
        public const double MinimumWidth = 350;
        public const double LeftMargin = 200;

        private ICompilerInfoProvider compilerInfo_;

        public RemarkOptionsPanel(ICompilerInfoProvider compilerInfo) {
            InitializeComponent();
            compilerInfo_ = compilerInfo;

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

        private void NotifySettingsChanged(bool force = false) {
            DelayedAction.StartNew(TimeSpan.FromMilliseconds(100), () => {
                RaiseSettingsChanged(force);
            });
        }

        private void SetAllKindCheckboxesButton_Click(object sender, RoutedEventArgs e) {
            SetCheckboxesState(kindCheckboxes_, true);
        }

        private void ResetAllKindCheckboxesButton_Click(object sender, RoutedEventArgs e) {
            SetCheckboxesState(kindCheckboxes_, false);
        }

        private void ShowDefinitionEditor(RemarkValueManager.ValueType valueType, string title) {
            var valueManager = new RemarkValueManager(valueType, compilerInfo_);
            valueManager.ValueChanged += (sender, e) => {
                NotifySettingsChanged(true);
            };

            var editorPopup = PropertyEditorPopup.ShowOverPanel(this, valueManager, title, 600, 400);
            editorPopup.Closed += (sender, args) => {
                if (valueManager.HasChanges) {
                    valueManager.SaveValues(editorPopup.Values);
                }
            };
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

        private void EditButton_Click(object sender, RoutedEventArgs e) {
            ShowDefinitionEditor(RemarkValueManager.ValueType.Category, "Remark categories");
        }

        private void EditBoundaryButton_OnClick(object sender, RoutedEventArgs e) {
            ShowDefinitionEditor(RemarkValueManager.ValueType.Boundary, "Remark section boundaries");
        }

        private void HighlightEditButton_OnClick(object sender, RoutedEventArgs e) {
            ShowDefinitionEditor(RemarkValueManager.ValueType.Highlight, "Remark panel text highlighting");
        }
    }
}
