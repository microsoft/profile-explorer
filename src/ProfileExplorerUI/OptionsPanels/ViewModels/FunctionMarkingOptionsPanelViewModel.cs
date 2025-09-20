// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProfileExplorer.Core.Settings;
using ProfileExplorer.UI.Controls;
using ProfileExplorer.UI.Services;
using ProfileExplorer.UI.Windows;

namespace ProfileExplorer.UI.OptionsPanels;

public partial class FunctionMarkingStyleViewModel : ObservableObject {
  [ObservableProperty]
  private string name_;

  [ObservableProperty]
  private Color color_;

  [ObservableProperty]
  private bool isRegex_;

  [ObservableProperty]
  private bool isEnabled_;

  [ObservableProperty]
  private bool isFocused_;

  [ObservableProperty]
  private bool isSelected_;

  public FunctionMarkingStyleViewModel(string name = "", Color? color = null, bool isRegex = false) {
    Name_ = name;
    Color_ = color ?? Colors.White;
    IsRegex_ = isRegex;
    IsEnabled_ = isEnabled_;
  }

  public FunctionMarkingStyleViewModel(FunctionMarkingStyle markingStyle) {
    Name_ = markingStyle.Name;
    Color_ = markingStyle.Color;
    IsRegex_ = markingStyle.IsRegex;
    IsEnabled_ = markingStyle.IsEnabled;
  }

  public FunctionMarkingStyle ToModel() {
    return new FunctionMarkingStyle(Name_, Color_, null, IsRegex_, IsEnabled_);
  }
}

public partial class FunctionMarkingSetViewModel : ObservableObject {
  [ObservableProperty]
  private string title_;

  [ObservableProperty]
  private bool isSelected_;

  [ObservableProperty]
  private ObservableCollection<FunctionMarkingStyleViewModel> functionColors_;

  [ObservableProperty]
  private ObservableCollection<FunctionMarkingStyleViewModel> moduleColors_;

  public FunctionMarkingSetViewModel(string title = "", 
                                    IEnumerable<FunctionMarkingStyleViewModel> functionColors = null,
                                    IEnumerable<FunctionMarkingStyleViewModel> moduleColors = null) {
    Title_ = title;
    FunctionColors_ = new ObservableCollection<FunctionMarkingStyleViewModel>(functionColors ?? Enumerable.Empty<FunctionMarkingStyleViewModel>());
    ModuleColors_ = new ObservableCollection<FunctionMarkingStyleViewModel>(moduleColors ?? Enumerable.Empty<FunctionMarkingStyleViewModel>());
  }

  public FunctionMarkingSetViewModel(FunctionMarkingSet markingSet) {
    Title_ = markingSet.Title;
    
    FunctionColors_ = new ObservableCollection<FunctionMarkingStyleViewModel>();
    foreach (var functionColor in markingSet.FunctionColors) {
      FunctionColors_.Add(new FunctionMarkingStyleViewModel(functionColor));
    }

    ModuleColors_ = new ObservableCollection<FunctionMarkingStyleViewModel>();
    foreach (var moduleColor in markingSet.ModuleColors) {
      ModuleColors_.Add(new FunctionMarkingStyleViewModel(moduleColor));
    }
  }

  public FunctionMarkingSet ToModel() {
    var set = new FunctionMarkingSet {
      Title = Title_
    };

    foreach (var functionColorViewModel in FunctionColors_) {
      set.FunctionColors.Add(functionColorViewModel.ToModel());
    }

    foreach (var moduleColorViewModel in ModuleColors_) {
      set.ModuleColors.Add(moduleColorViewModel.ToModel());
    }

    return set;
  }
}

public partial class FunctionMarkingOptionsPanelViewModel : OptionsPanelBaseViewModel<FunctionMarkingSettings> {
  private IDialogService _dialogService;

  // Observable properties mirroring FunctionMarkingSettings
  [ObservableProperty]
  private bool useAutoModuleColors_;

  [ObservableProperty]
  private string modulesColorPalette_;

  [ObservableProperty]
  private bool useModuleColors_;

  [ObservableProperty]
  private bool useFunctionColors_;

  [ObservableProperty]
  private ObservableCollection<FunctionMarkingStyleViewModel> moduleColors_;

  [ObservableProperty]
  private ObservableCollection<FunctionMarkingStyleViewModel> functionColors_;

  [ObservableProperty]
  private ObservableCollection<FunctionMarkingSetViewModel> savedSets_;

  // Computed properties that access settings
  public bool InputControlsEnabled => true; // Always enabled for options panels

  public override void Initialize(FrameworkElement parent, FunctionMarkingSettings settings, IUISession session) {
    base.Initialize(parent, settings, session);
    
    // Initialize the dialog service with the parent element now that we have it
    _dialogService = new DialogService(parent);
    
    // Populate view model properties from settings
    PopulateFromSettings(settings);
  }

  private void PopulateFromSettings(FunctionMarkingSettings settings) {
    // Populate simple properties
    UseAutoModuleColors_ = settings.UseAutoModuleColors;
    ModulesColorPalette_ = settings.ModulesColorPalette;
    UseModuleColors_ = settings.UseModuleColors;
    UseFunctionColors_ = settings.UseFunctionColors;

    // Populate wrapper collections
    ModuleColors_ = new ObservableCollection<FunctionMarkingStyleViewModel>();
    if (settings.ModuleColors != null) {
      foreach (var moduleColor in settings.ModuleColors) {
        ModuleColors_.Add(new FunctionMarkingStyleViewModel(moduleColor));
      }
    }

    FunctionColors_ = new ObservableCollection<FunctionMarkingStyleViewModel>();
    if (settings.FunctionColors != null) {
      foreach (var functionColor in settings.FunctionColors) {
        FunctionColors_.Add(new FunctionMarkingStyleViewModel(functionColor));
      }
    }

    SavedSets_ = new ObservableCollection<FunctionMarkingSetViewModel>();
    foreach (var savedSet in settings.SavedSets) {
      SavedSets_.Add(new FunctionMarkingSetViewModel(savedSet));
    }
  }

  [RelayCommand]
  private void ModuleAdd() {
    foreach (var item in ModuleColors_) {
      item.IsSelected_ = false;
    }

    var newItem = new FunctionMarkingStyleViewModel("", Colors.White);
    newItem.IsSelected_ = true;
    ModuleColors_.Add(newItem);

    // Set focus on the text box for the newly added item
    newItem.IsFocused_ = true;
  }

  [RelayCommand]
  private void ModuleRemove() {
    var toRemove = ModuleColors_.Where(item => item.IsSelected_).ToList();
    foreach (var item in toRemove) {
      ModuleColors_.Remove(item);
    }
  }

  [RelayCommand]
  private async Task ClearModule() {
    if (await _dialogService.ShowYesNoMessageBoxAsync("Do you want to clear the list?")) {
      ModuleColors_.Clear();
    }
  }

  [RelayCommand]
  private void FunctionAdd() {
    foreach (var item in FunctionColors_) {
      item.IsSelected_ = false;
    }
    var newItem = new FunctionMarkingStyleViewModel("", Colors.White);
    newItem.IsSelected_ = true;

    // Add the new item to the collection
    FunctionColors_.Add(newItem);
    
    // Set focus on the text box for the newly added item
    newItem.IsFocused_ = true;
  }

  [RelayCommand]
  private void FunctionRemove() {
    var toRemove = FunctionColors_.Where(item => item.IsSelected_).ToList();
    foreach (var item in toRemove) {
      FunctionColors_.Remove(item);
    }
  }

  [RelayCommand]
  private async Task ClearFunction() {
    if (await _dialogService.ShowYesNoMessageBoxAsync("Do you want to clear the list?")) {
      FunctionColors_.Clear();
    }
  }

  [RelayCommand]
  private async Task MarkingClear() {
    if (await _dialogService.ShowYesNoMessageBoxAsync("Do you want to clear the list?")) {
      SavedSets_.Clear();
    }
  }

  [RelayCommand]
  private void MarkingRemove() {
    var toRemove = SavedSets_.Where(item => item.IsSelected_).ToList();
    foreach (var item in toRemove) {
      SavedSets_.Remove(item);
    }
  }

  [RelayCommand]
  private async Task MarkingImport() {
    var (success, data, errorMessage) = settings_.LoadMarkingsFromFile(parent_);
    
    if (success && data != null) {
      // Apply the imported data to the ViewModel collections
      ApplyImportedMarkings(data);
    }
  }

  [RelayCommand]
  private void MarkingExport() {
    // Create the current set from ViewModel data
    var currentSet = CreateCurrentSetFromViewModel();
    
    // Create saved sets from ViewModel data
    var savedSetsList = SavedSets_.Select(vm => vm.ToModel()).ToList();
    
    // Export using the new method that takes data as parameters
    settings_.ExportMarkingsFromData(currentSet, savedSetsList, parent_);
  }

  [RelayCommand]
  private async Task MarkingSave() {
    var result = await _dialogService.ShowTextInputDialogAsync("Save marked functions/modules", "Saved marking set name:");
    
    if (!string.IsNullOrEmpty(result)) {
      // Create a new saved set from the current view model state
      var currentSet = CreateCurrentSetFromViewModel();
      currentSet.Title = result;
      
      // Remove any existing set with the same title
      var existingSet = SavedSets_.FirstOrDefault(vm => vm.Title_ == result);
      if (existingSet != null) {
        SavedSets_.Remove(existingSet);
      }
      
      // Add the new set
      SavedSets_.Add(new FunctionMarkingSetViewModel(currentSet));
    }
  }

  [RelayCommand]
  private void MarkingLoad() {
    var toSave = SavedSets_.Where(item => item.IsSelected_).ToList();
    if (toSave.Count == 1) {
      var set = toSave[0];
      MergeMarkingStylesIntoCollection(set.FunctionColors_, FunctionColors_);
      MergeMarkingStylesIntoCollection(set.ModuleColors_, ModuleColors_);
    }
  }

  private void ApplyImportedMarkings(FunctionMarkingSettings.Markings data) {
    // Merge current set data into existing collections
    MergeMarkingStylesIntoCollection(data.Current.FunctionColors, FunctionColors_);
    MergeMarkingStylesIntoCollection(data.Current.ModuleColors, ModuleColors_);
    
    // Add new saved sets (merge with existing if same title)
    foreach (var savedSet in data.Saved) {
      var existingSetVm = SavedSets_.FirstOrDefault(vm => vm.Title_ == savedSet.Title);
      
      if (existingSetVm != null) {
        // Merge with existing set
        MergeMarkingStylesIntoCollection(savedSet.FunctionColors, existingSetVm.FunctionColors_);
        MergeMarkingStylesIntoCollection(savedSet.ModuleColors, existingSetVm.ModuleColors_);
      } else {
        // Add as new set
        SavedSets_.Add(new FunctionMarkingSetViewModel(savedSet));
      }
    }
  }

  private void MergeMarkingStylesIntoCollection(List<FunctionMarkingStyle> sourceStyles, 
                                               ObservableCollection<FunctionMarkingStyleViewModel> targetCollection) {
    foreach (var style in sourceStyles) {
      // Remove existing item with same name
      var existingVm = targetCollection.FirstOrDefault(vm => vm.Name_.Equals(style.Name, StringComparison.Ordinal));
      if (existingVm != null) {
        targetCollection.Remove(existingVm);
      }
      
      // Add the new style
      targetCollection.Add(new FunctionMarkingStyleViewModel(style));
    }
  }

  private void MergeMarkingStylesIntoCollection(ObservableCollection<FunctionMarkingStyleViewModel> sourceStyles,
                                               ObservableCollection<FunctionMarkingStyleViewModel> targetCollection) {
    foreach (var style in sourceStyles) {
      // Remove existing item with same name
      var existingVm = targetCollection.FirstOrDefault(vm => vm.Name_.Equals(style.Name_, StringComparison.Ordinal));
      if (existingVm != null) {
        targetCollection.Remove(existingVm);
      }

      // Add the new style
      targetCollection.Add(style);
    }
  }

  private FunctionMarkingSet CreateCurrentSetFromViewModel() {
    var currentSet = new FunctionMarkingSet();
    
    // Add function colors from ViewModel
    foreach (var functionColorVm in FunctionColors_) {
      currentSet.FunctionColors.Add(functionColorVm.ToModel());
    }
    
    // Add module colors from ViewModel
    foreach (var moduleColorVm in ModuleColors_) {
      currentSet.ModuleColors.Add(moduleColorVm.ToModel());
    }
    
    return currentSet;
  }

  public override void SaveSettings() {
    if (Settings_ != null) {
      // Save simple boolean and string properties
      Settings_.UseAutoModuleColors = UseAutoModuleColors_;
      Settings_.ModulesColorPalette = ModulesColorPalette_;
      Settings_.UseModuleColors = UseModuleColors_;
      Settings_.UseFunctionColors = UseFunctionColors_;

      // Save collections - need to update the CurrentSet collections
      if (Settings_.CurrentSet != null) {
        Settings_.CurrentSet.ModuleColors.Clear();
        foreach (var moduleColorViewModel in ModuleColors_) {
          Settings_.CurrentSet.ModuleColors.Add(moduleColorViewModel.ToModel());
        }

        Settings_.CurrentSet.FunctionColors.Clear();
        foreach (var functionColorViewModel in FunctionColors_) {
          Settings_.CurrentSet.FunctionColors.Add(functionColorViewModel.ToModel());
        }
      }

      // Sync SavedSets collection from view model to settings
      Settings_.SavedSets.Clear();
      foreach (var savedSetViewModel in SavedSets_) {
        Settings_.SavedSets.Add(savedSetViewModel.ToModel());
      }
    }
  }

  /// <summary>
  /// Gets the current settings with all changes applied
  /// </summary>
  public FunctionMarkingSettings GetCurrentSettings() {
    SaveSettings(); // Ensure all changes are saved to the settings object
    return Settings_;
  }

  /// <summary>
  /// Resets the settings to default values
  /// </summary>
  public void ResetSettings() {
    if (Settings_ != null) {
      // Reset to defaults - create a new default settings object
      var defaultSettings = new FunctionMarkingSettings();
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