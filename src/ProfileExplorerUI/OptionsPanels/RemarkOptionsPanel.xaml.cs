// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using ProfileExplorer.Core.Settings;

namespace ProfileExplorer.UI.OptionsPanels;

public partial class RemarkOptionsPanel : OptionsPanelBase {
  public const double LeftMargin = 200;
  private List<CheckBox> kindCheckboxes_;
  private List<CheckBox> categoryCheckboxes_;

  public RemarkOptionsPanel() {
    InitializeComponent();
    kindCheckboxes_ = new List<CheckBox>();
    kindCheckboxes_.Add(OptimizationCheckbox);
    kindCheckboxes_.Add(AnalysisCheckbox);
    kindCheckboxes_.Add(StandardCheckbox);
    kindCheckboxes_.Add(VerboseCheckbox);
    kindCheckboxes_.Add(TraceCheckbox);
  }

  public override void Initialize(FrameworkElement parent, SettingsBase settings, IUISession session) {
    base.Initialize(parent, settings, session);
    PopulateCategoryList();
  }

  private void SetCheckboxesState(List<CheckBox> list, bool state) {
    list.ForEach(item => item.IsChecked = state);
  }

  private bool PopulateCategoryList() {
    var remarkSettings = (RemarkSettings)DataContext;
    categoryCheckboxes_ = new List<CheckBox>();

    bool initialLoad = !remarkSettings.HasCategoryFilters;
    var categories = App.Session.CompilerInfo.RemarkProvider.RemarkCategories;

    if (categories == null) {
      using var centerForm = new DialogCenteringHelper(Parent);
      MessageBox.Show("Failed to load remark settings file,\ncheck JSON for any syntax errors!", "Profile Explorer",
                      MessageBoxButton.OK, MessageBoxImage.Error);
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

  private void SetAllKindCheckboxesButton_Click(object sender, RoutedEventArgs e) {
    SetCheckboxesState(kindCheckboxes_, true);
  }

  private void ResetAllKindCheckboxesButton_Click(object sender, RoutedEventArgs e) {
    SetCheckboxesState(kindCheckboxes_, false);
  }

  private void EditButton_Click(object sender, RoutedEventArgs e) {
    string settingsPath = App.GetRemarksDefinitionFilePath(App.Session.CompilerInfo.CompilerIRName);
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