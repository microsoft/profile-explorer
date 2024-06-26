// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Navigation;
using System.Windows.Threading;
using IRExplorerUI.Compilers;
using IRExplorerUI.Controls;
using Microsoft.Win32;

namespace IRExplorerUI.OptionsPanels;

public partial class SymbolOptionsPanel : OptionsPanelBase, INotifyPropertyChanged {
  public const double DefaultHeight = 320;
  public const double MinimumHeight = 200;
  public const double DefaultWidth = 350;
  public const double MinimumWidth = 350;

  private SymbolFileSourceSettings symbolSettings_;
  
  public SymbolOptionsPanel() {
    InitializeComponent();
  }

  public event PropertyChangedEventHandler PropertyChanged;
  
  public override void Initialize(FrameworkElement parent, SettingsBase settings, ISession session) {
    base.Initialize(parent, settings, session);
    symbolSettings_ = (SymbolFileSourceSettings)Settings;
    ReloadSymbolPathsList();
  }

  public override void OnSettingsChanged(object newSettings) {
    symbolSettings_ = (SymbolFileSourceSettings)newSettings;
  }
  
  private void ClearRejectedButton_Click(object sender, RoutedEventArgs e) {
    if (Utils.ShowYesNoMessageBox("Do you want to remove all excluded binaries and symbols?", this) ==
        MessageBoxResult.Yes) {
      symbolSettings_.ClearRejectedFiles();
      ReloadSettings();
    }
  }

  private void RemoveRejectedBinariesButton_Click(object sender, RoutedEventArgs e) {
    foreach (object item in RejectedBinariesList.SelectedItems) {
      symbolSettings_.RejectedBinaryFiles.Remove(item as BinaryFileDescriptor);
    }

    ReloadSettings();
  }

  private void ClearRejectedBinariesButton_Click(object sender, RoutedEventArgs e) {
    if (Utils.ShowYesNoMessageBox("Do you want to remove all excluded binaries?", this) ==
        MessageBoxResult.Yes) {
      symbolSettings_.RejectedBinaryFiles.Clear();
      ReloadSymbolPathsList();
    }
  }

  private void RemoveRejectedSymbolsButton_Click(object sender, RoutedEventArgs e) {
    foreach (object item in RejectedSymbolsList.SelectedItems) {
      symbolSettings_.RejectedSymbolFiles.Remove(item as SymbolFileDescriptor);
    }

    ReloadSymbolPathsList();
  }

  private void ClearRejectedSymbolsButton_Click(object sender, RoutedEventArgs e) {
    if (Utils.ShowYesNoMessageBox("Do you want to remove all excluded symbols?", this) ==
        MessageBoxResult.Yes) {
      symbolSettings_.RejectedSymbolFiles.Clear();
      ReloadSymbolPathsList();
    }
  }

  private void SymbolPathBrowseButton_Click(object sender, RoutedEventArgs e) {
    var listViewItem = Utils.FocusParentListViewItem(sender as Control, SymbolPathsList);
    var textBox = Utils.FindChild<FileSystemTextBox>(listViewItem);

    using var centerForm = new DialogCenteringHelper(this);
    var dialog = new OpenFolderDialog();
    dialog.Title = "Select symbols directory";

    if (dialog.ShowDialog() == true) {
      textBox.Text = dialog.FolderName;
      UpdateSymbolPath(textBox);
    }
  }

  private void ClearSymbolCacheButton_Click(object sender, RoutedEventArgs e) {
    if (Utils.ShowYesNoMessageBox("Do you want to remove all cached symbol files?", this) ==
        MessageBoxResult.Yes) {
      symbolSettings_.ClearSymbolFileCache();
      ReloadSettings();
    }
  }

  private void OpenSymbolCacheButton_Click(object sender, RoutedEventArgs e) {
    Utils.OpenExplorerAtFile(symbolSettings_.SymbolCacheDirectoryPath);
  }
  
  
  private void SymbolPath_LostFocus(object sender, RoutedEventArgs e) {
    var textBox = sender as FileSystemTextBox;
    UpdateSymbolPath(textBox);
  }

  private void SymbolPath_KeyDown(object sender, KeyEventArgs e) {
    if (e.Key == Key.Enter) {
      var textBox = sender as FileSystemTextBox;
      UpdateSymbolPath(textBox);
    }
  }

  private void SymbolPath_OnDropDownClosed(object sender, RoutedPropertyChangedEventArgs<bool> e) {
    var textBox = sender as FileSystemTextBox;
    UpdateSymbolPath(textBox);
  }

  private void UpdateSymbolPath(FileSystemTextBox textBox) {
    if (textBox == null) {
      return;
    }

    object item = textBox.DataContext;
    int index = symbolSettings_.SymbolPaths.IndexOf(item as string);

    if (index == -1) {
      return;
    }

    // Update list with the new text.
    var newSymbolPath = Utils.RemovePathQuotes(textBox.Text);
    textBox.Text = newSymbolPath;
    
    if (symbolSettings_.SymbolPaths[index] != newSymbolPath) {
      symbolSettings_.SymbolPaths[index] = newSymbolPath;
      ReloadSymbolPathsList();
    }
  }

  private void TextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
    if (sender is FileSystemTextBox textBox) {
      Utils.SelectTextBoxListViewItem(textBox, SymbolPathsList);
    }
  }

  private void AddPrivateSymbolServer_OnClick(object sender, RoutedEventArgs e) {
    symbolSettings_.AddSymbolServer(usePrivateServer: true);
    ReloadSymbolPathsList();
  }

  private void AddPublicSymbolServer_OnClick(object sender, RoutedEventArgs e) {
    symbolSettings_.AddSymbolServer(usePrivateServer: false);
    ReloadSymbolPathsList();
  }

  private void AddSymbolPathButton_Click(object sender, RoutedEventArgs e) {
    symbolSettings_.SymbolPaths.Add("");
    ReloadSymbolPathsList();

    // Wait for the UI to update
    Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () => {
      Utils.SelectEditableListViewItem(SymbolPathsList, symbolSettings_.SymbolPaths.Count - 1);
    });
  }

  private void RemoveSymbolPathButton_Click(object sender, RoutedEventArgs e) {
    foreach (object item in SymbolPathsList.SelectedItems) {
      string symbolPath = item as string;
      symbolSettings_.SymbolPaths.Remove(symbolPath);
    }

    ReloadSymbolPathsList();
  }

  private void MoveSymbolPathUpButton_Click(object sender, RoutedEventArgs e) {
    if (SymbolPathsList.SelectedItems.Count != 1) {
      return; // Only remove if there is exactly one item selected
    }

    if (SymbolPathsList.SelectedIndex == 0) {
      return; // Cannot move an item up if it is already at the top of the list
    }

    int selectedIndex = SymbolPathsList.SelectedIndex;
    string selectedItem = SymbolPathsList.SelectedItem as string;
    symbolSettings_.SymbolPaths.RemoveAt(selectedIndex);
    symbolSettings_.SymbolPaths.Insert(selectedIndex - 1, selectedItem);
    ReloadSymbolPathsList();
  }

  private void MoveSymbolPathDownButton_Click(object sender, RoutedEventArgs e) {
    if (SymbolPathsList.SelectedItems.Count != 1) {
      return; // Only remove if there is exactly one item selected
    }

    if (SymbolPathsList.SelectedIndex == SymbolPathsList.Items.Count - 1) {
      return; // Cannot move an item down if it is already at the bottom of the list
    }

    int selectedIndex = SymbolPathsList.SelectedIndex;
    string selectedItem = SymbolPathsList.SelectedItem as string;
    symbolSettings_.SymbolPaths.RemoveAt(selectedIndex);
    symbolSettings_.SymbolPaths.Insert(selectedIndex + 1, selectedItem);
    ReloadSymbolPathsList();
  }

  private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e) {
    Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) {
      UseShellExecute = true
    });
  }

  private void ReloadSymbolPathsList() {
    var list = new ObservableCollectionRefresh<string>(symbolSettings_.SymbolPaths);
    SymbolPathsList.ItemsSource = list;

    var binariesList = symbolSettings_.RejectedBinaryFiles.ToList();
    binariesList.Sort((a, b) => String.Compare(a.ImageName, b.ImageName, StringComparison.OrdinalIgnoreCase));
    RejectedBinariesList.ItemsSource = binariesList;

    var symbolList = symbolSettings_.RejectedSymbolFiles.ToList();
    symbolList.Sort((a, b) => String.Compare(a.FileName, b.FileName, StringComparison.OrdinalIgnoreCase));
    RejectedSymbolsList.ItemsSource = symbolList;
  }

  public void OnPropertyChange(string propertyname) {
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyname));
  }
}
