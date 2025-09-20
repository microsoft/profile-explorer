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
using System.Windows.Input;
using System.Windows.Navigation;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using ProfileExplorer.Core.Binary;
using ProfileExplorer.Core.Settings;
using ProfileExplorer.UI.Controls;
using ProfileExplorer.UI.Services;

namespace ProfileExplorer.UI.OptionsPanels;

public partial class BinaryFileDescriptorViewModel : ObservableObject {
  private BinaryFileDescriptor binaryFileDescriptor_;

  public BinaryFileDescriptorViewModel(BinaryFileDescriptor binaryFileDescriptor) {
    binaryFileDescriptor_ = binaryFileDescriptor;
    imageName_ = binaryFileDescriptor.ImageName;
    imagePath_ = binaryFileDescriptor.ImagePath;
    imageSize_ = binaryFileDescriptor.ImageSize;
    timeStamp_ = binaryFileDescriptor.TimeStamp;
  }

  [ObservableProperty]
  private string imageName_;

  [ObservableProperty]
  private string imagePath_;

  [ObservableProperty]
  private long imageSize_;

  [ObservableProperty]
  private int timeStamp_;

  [ObservableProperty]
  private bool isSelected_;

  public BinaryFileDescriptor ToModel() {
    binaryFileDescriptor_.ImageName = imageName_;
    binaryFileDescriptor_.ImagePath = imagePath_;
    binaryFileDescriptor_.ImageSize = imageSize_;
    binaryFileDescriptor_.TimeStamp = timeStamp_;
    return binaryFileDescriptor_;
  }
}

public partial class SymbolFileDescriptorViewModel : ObservableObject {
  private SymbolFileDescriptor symbolFileDescriptor_;

  public SymbolFileDescriptorViewModel(SymbolFileDescriptor symbolFileDescriptor) {
    symbolFileDescriptor_ = symbolFileDescriptor;
    symbolName_ = symbolFileDescriptor.SymbolName;
    fileName_ = symbolFileDescriptor.FileName;
    id_ = symbolFileDescriptor.Id;
    age_ = symbolFileDescriptor.Age;
  }

  [ObservableProperty]
  private string symbolName_;

  [ObservableProperty]
  private string fileName_;

  [ObservableProperty]
  private Guid id_;

  [ObservableProperty]
  private int age_;

  [ObservableProperty]
  private bool isSelected_;

  public SymbolFileDescriptor ToModel() {
    symbolFileDescriptor_.FileName = fileName_;
    symbolFileDescriptor_.Id = id_;
    symbolFileDescriptor_.Age = age_;
    return symbolFileDescriptor_;
  }
}

public partial class SymbolPathViewModel : ObservableObject {
  private readonly IDialogService _dialogService;

  public SymbolPathViewModel(string path, IDialogService dialogService) {
    _dialogService = dialogService;
    Path_ = path;
  }

  [ObservableProperty]
  private string path_;

  [ObservableProperty]
  private string isSelected_;

  [RelayCommand]
  private async Task Browse() {
    var selectedPath = await _dialogService.ShowOpenFolderDialogAsync(
      "Select symbols directory",
      Path_);

    if (!string.IsNullOrEmpty(selectedPath)) {
      Path_ = selectedPath;
    }
  }
}

public partial class SymbolOptionsPanelViewModel : OptionsPanelBaseViewModel<SymbolFileSourceSettings> {
  private IDialogService _dialogService;

  [ObservableProperty]
  private ObservableCollection<SymbolPathViewModel> symbolPaths_;

  [ObservableProperty]
  private int selectedSymbolPathIndex_;

  [ObservableProperty]
  private ObservableCollection<BinaryFileDescriptorViewModel> rejectedBinaryFiles_;

  [ObservableProperty]
  private ObservableCollection<SymbolFileDescriptorViewModel> rejectedSymbolFiles_;

  [ObservableProperty]
  private bool sourceServerEnabled_;

  [ObservableProperty]
  private bool authorizationTokenEnabled_;

  [ObservableProperty]
  private string authorizationToken_ = "";

  [ObservableProperty]
  private bool useEnvironmentVarSymbolPaths_;

  [ObservableProperty]
  private bool skipLowSampleModules_;

  [ObservableProperty]
  private bool rejectPreviouslyFailedFiles_;

  [ObservableProperty]
  private bool includeSymbolSubdirectories_;

  [ObservableProperty]
  private bool cacheSymbolFiles_;

  [ObservableProperty]
  private double lowSampleModuleCutoff_;

  // Computed properties that access settings
  public long SymbolCacheDirectorySizeMB => settings_.SymbolCacheDirectorySizeMB;
  public bool InputControlsEnabled => true; // Always enabled for options panels

  public override void Initialize(FrameworkElement parent, SymbolFileSourceSettings settings, IUISession session) {
    base.Initialize(parent, settings, session);

    // Initialize the dialog service with the parent element now that we have it
    _dialogService = new DialogService(parent);

    // Populate view model properties from settings
    PopulateFromSettings(settings);
  }

  private void PopulateFromSettings(SymbolFileSourceSettings settings) {
    // Populate wrapper collections
    SymbolPaths_ = new ObservableCollection<SymbolPathViewModel>();
    foreach (string path in settings.SymbolPaths) {
      SymbolPaths_.Add(new SymbolPathViewModel(path, _dialogService));
    }

    RejectedBinaryFiles_ = new ObservableCollection<BinaryFileDescriptorViewModel>();
    foreach (var binaryFile in settings.RejectedBinaryFiles) {
      RejectedBinaryFiles_.Add(new BinaryFileDescriptorViewModel(binaryFile));
    }

    RejectedSymbolFiles_ = new ObservableCollection<SymbolFileDescriptorViewModel>();
    foreach (var symbolFile in settings.RejectedSymbolFiles) {
      RejectedSymbolFiles_.Add(new SymbolFileDescriptorViewModel(symbolFile));
    }

    // Populate simple properties
    UseEnvironmentVarSymbolPaths_ = settings.UseEnvironmentVarSymbolPaths;
    SkipLowSampleModules_ = settings.SkipLowSampleModules;
    RejectPreviouslyFailedFiles_ = settings.RejectPreviouslyFailedFiles;
    IncludeSymbolSubdirectories_ = settings.IncludeSymbolSubdirectories;
    CacheSymbolFiles_ = settings.CacheSymbolFiles;
    LowSampleModuleCutoff_ = settings.LowSampleModuleCutoff;
  }

  [RelayCommand]
  private async Task ClearRejected() {
    if (await _dialogService.ShowYesNoMessageBoxAsync("Do you want to remove all excluded binaries and symbols?")) {
      RejectedBinaryFiles_.Clear();
      RejectedSymbolFiles_.Clear();
    }
  }

  [RelayCommand]
  private void RemoveRejectedBinaries() {
    var toRemove = RejectedBinaryFiles_.Where(b => b.IsSelected_).ToList();
    foreach (var item in toRemove) {
      RejectedBinaryFiles_.Remove(item);
    }
  }

  [RelayCommand]
  private async Task ClearRejectedBinaries() {
    if (await _dialogService.ShowYesNoMessageBoxAsync("Do you want to remove all excluded binaries?")) {
      RejectedBinaryFiles_.Clear();
    }
  }

  [RelayCommand]
  private void RemoveRejectedSymbols(IList selectedItems) {
    var toRemove = RejectedSymbolFiles_.Where(b => b.IsSelected_).ToList();
    foreach (var item in toRemove) {
      RejectedSymbolFiles_.Remove(item);
    }
  }

  [RelayCommand]
  private async Task ClearRejectedSymbols() {
    if (await _dialogService.ShowYesNoMessageBoxAsync("Do you want to remove all excluded symbols?")) {
      RejectedSymbolFiles_.Clear();
    }
  }

  [RelayCommand]
  private async Task ClearSymbolCache() {
    if (await _dialogService.ShowYesNoMessageBoxAsync("Do you want to remove all cached symbol files?")) {
      settings_.ClearSymbolFileCache();
    }
  }

  [RelayCommand]
  private void OpenSymbolCache() {
    Utils.OpenExplorerAtFile(settings_.SymbolCacheDirectoryPath);
  }

  [RelayCommand]
  private void AddPrivateSymbolServer() {
    string symbolServer = @"https://symweb.azurefd.net";
    string defaultCachePath = @"C:\Symbols";
    string path = $"srv*{defaultCachePath}*{symbolServer}";

    if (!SymbolPaths_.Any(item => item.Path_.Equals(path, StringComparison.OrdinalIgnoreCase))) {
      SymbolPaths_.Add(new SymbolPathViewModel(path, _dialogService));
    }
  }

  [RelayCommand]
  private void AddPublicSymbolServer() {
    string symbolServer = @"https://msdl.microsoft.com/download/symbols";
    string defaultCachePath = @"C:\Symbols";
    string path = $"srv*{defaultCachePath}*{symbolServer}";

    if (!SymbolPaths_.Any(item => item.Path_.Equals(path, StringComparison.OrdinalIgnoreCase))) {
      SymbolPaths_.Add(new SymbolPathViewModel(path, _dialogService));
    }
  }

  [RelayCommand]
  private void AddSymbolPath() {
    var newPath = new SymbolPathViewModel("", _dialogService);
    SymbolPaths_.Add(newPath);
    
    // Select the newly added empty path for editing
    SelectedSymbolPathIndex_ = SymbolPaths_.Count - 1;
  }

  [RelayCommand]
  private void RemoveSymbolPath() {
    while (SelectedSymbolPathIndex_ != -1) {
      SymbolPaths_.RemoveAt(SelectedSymbolPathIndex_);
    }
  }

  [RelayCommand]
  private void MoveSymbolPathUp() {
    if (SelectedSymbolPathIndex_ <= 0 || SelectedSymbolPathIndex_ >= SymbolPaths_.Count) {
      return; // Cannot move an item up if it is already at the top or nothing is selected
    }

    int currentIndex = SelectedSymbolPathIndex_;
    var selectedItem = SymbolPaths_[currentIndex];
    
    SymbolPaths_.RemoveAt(currentIndex);
    SymbolPaths_.Insert(currentIndex - 1, selectedItem);
    
    // Update the selected index to follow the moved item
    SelectedSymbolPathIndex_ = currentIndex - 1;
  }

  [RelayCommand]
  private void MoveSymbolPathDown() {
    if (SelectedSymbolPathIndex_ < 0 || SelectedSymbolPathIndex_ >= SymbolPaths_.Count - 1) {
      return; // Cannot move an item down if it is already at the bottom or nothing is selected
    }

    int currentIndex = SelectedSymbolPathIndex_;
    var selectedItem = SymbolPaths_[currentIndex];

    SymbolPaths_.RemoveAt(currentIndex);
    SymbolPaths_.Insert(currentIndex + 1, selectedItem);

    // Update the selected index to follow the moved item
    SelectedSymbolPathIndex_ = currentIndex + 1;
  }

  [RelayCommand]
  private void NavigateHyperlink(string url) {
    Process.Start(new ProcessStartInfo(url) {
      UseShellExecute = true
    });
  }

  [RelayCommand]
  private void ResetFilterModuleSamples() {
    LowSampleModuleCutoff_ = SymbolFileSourceSettings.DefaultLowSampleModuleCutoff;
  }
  
  public override void SaveSettings() {
    if (Settings_ != null) {
      // Save simple boolean and numeric properties
      Settings_.UseEnvironmentVarSymbolPaths = UseEnvironmentVarSymbolPaths_;
      Settings_.SkipLowSampleModules = SkipLowSampleModules_;
      Settings_.RejectPreviouslyFailedFiles = RejectPreviouslyFailedFiles_;
      Settings_.IncludeSymbolSubdirectories = IncludeSymbolSubdirectories_;
      Settings_.CacheSymbolFiles = CacheSymbolFiles_;
      Settings_.LowSampleModuleCutoff = LowSampleModuleCutoff_;
      Settings_.SourceServerEnabled = SourceServerEnabled_;
      Settings_.AuthorizationTokenEnabled = AuthorizationTokenEnabled_;
      Settings_.AuthorizationToken = AuthorizationToken_;

      // Save collections
      Settings_.SymbolPaths.Clear();
      foreach (var pathViewModel in SymbolPaths_) {
        if (!string.IsNullOrWhiteSpace(pathViewModel.Path_)) {
          Settings_.SymbolPaths.Add(pathViewModel.Path_);
        }
      }

      Settings_.RejectedBinaryFiles.Clear();
      foreach (var binaryFile in RejectedBinaryFiles_) {
        Settings_.RejectedBinaryFiles.Add(binaryFile.ToModel());
      }

      Settings_.RejectedSymbolFiles.Clear();
      foreach (var symbolFile in RejectedSymbolFiles_) {
        Settings_.RejectedSymbolFiles.Add(symbolFile.ToModel());
      }
    }
  }
}