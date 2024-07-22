// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using IRExplorerUI.Profile;

namespace IRExplorerUI.Panels;

public partial class StartPagePanel : UserControl {
  public StartPagePanel() {
    InitializeComponent();
  }

  public event EventHandler<string> OpenRecentDocument;
  public event EventHandler<Tuple<string, string>> OpenRecentDiffDocuments;
  public event EventHandler<RecordingSession> OpenRecentProfileSession;
  public event EventHandler OpenFile;
  public event EventHandler CompareFiles;
  public event EventHandler ClearRecentDocuments;
  public event EventHandler ClearRecentDiffDocuments;
  public event EventHandler ClearRecentProfileSessions;
  public event EventHandler LoadProfile;
  public event EventHandler RecordProfile;

  public void ReloadFileList() {
    RecentFilesListBox.ItemsSource = new ListCollectionView(App.Settings.RecentFiles);
    RecentFilesListBox.Visibility = App.Settings.RecentFiles.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    RecentDiffFilesListBox.ItemsSource = new ListCollectionView(App.Settings.RecentComparedFiles);
    RecentDiffFilesListBox.Visibility =
      App.Settings.RecentComparedFiles.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    var profileSettings = App.Settings.ProfileOptions;
    var sessionList = new List<RecordingSession>();
    
    foreach (var prevSession in profileSettings.PreviousLoadedSessions) {
      sessionList.Add(new RecordingSession(prevSession, true));
    }

    RecentProfilesListBox.ItemsSource = new ListCollectionView(sessionList);
    RecentProfilesListBox.Visibility = profileSettings.PreviousLoadedSessions.Count > 0 ?
      Visibility.Visible : Visibility.Collapsed;
  }

  private void RecentFilesListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e) {
    InvokeOpenRecentDocument();
    e.Handled = true;
  }

  private void RecentFilesListBox_KeyDown(object sender, KeyEventArgs e) {
    if (e.Key == Key.Enter) {
      InvokeOpenRecentDocument();
      e.Handled = true;
    }
  }

  private void RecentProfilesListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e) {
    InvokeOpenRecentProfileSession();
    e.Handled = true;
  }
  
  private void RecentProfilesListBox_KeyDown(object sender, KeyEventArgs e) {
    if (e.Key == Key.Enter) {
      InvokeOpenRecentProfileSession();
      e.Handled = true;
    }
  }
  
  private void RecentDiffFilesListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e) {
    InvokeOpenRecentDiffDocuments();
    e.Handled = true;
  }

  private void RecentDiffFilesListBox_KeyDown(object sender, KeyEventArgs e) {
    if (e.Key == Key.Enter) {
      InvokeOpenRecentDiffDocuments();
      e.Handled = true;
    }
  }

  private void InvokeOpenRecentDocument() {
    if (RecentFilesListBox.SelectedItem != null) {
      OpenRecentDocument?.Invoke(this, (string)RecentFilesListBox.SelectedItem);
    }
  }

  private void InvokeOpenRecentDiffDocuments() {
    if (RecentDiffFilesListBox.SelectedItem != null) {
      OpenRecentDiffDocuments?.Invoke(this, (Tuple<string, string>)RecentDiffFilesListBox.SelectedItem);
    }
  }
  
  private void InvokeOpenRecentProfileSession() {
    if (RecentProfilesListBox.SelectedItem != null) {
      OpenRecentProfileSession?.Invoke(this, (RecordingSession)RecentProfilesListBox.SelectedItem);
    }
  }
  
  private void TextBlock_MouseDown(object sender, MouseButtonEventArgs e) {
    App.InstallExtension();
  }

  private void TextBlock_MouseDown_1(object sender, MouseButtonEventArgs e) {
    App.OpenDocumentation();
  }

  private void OpenFileButton_Click(object sender, RoutedEventArgs e) {
    OpenFile?.Invoke(this, null);
  }

  private void OpenBaseDiffFilesButton_Click(object sender, RoutedEventArgs e) {
    CompareFiles?.Invoke(this, null);
  }

  private void ClearButton_Click(object sender, RoutedEventArgs e) {
    ClearRecentDocuments?.Invoke(this, null);
  }

  private void ClearDiffButton_Click(object sender, RoutedEventArgs e) {
    ClearRecentDiffDocuments?.Invoke(this, null);
  }

  private void TextSearch_Populating(object sender, PopulatingEventArgs e) {
    var box = (AutoCompleteBox)sender;
    box.ItemsSource = null;
    box.ItemsSource = App.Settings.RecentFiles;
    box.PopulateComplete();
  }

  private void TextSearch_KeyDown(object sender, KeyEventArgs e) {
    if (e.Key == Key.Escape) {
      TextSearch.Text = "";
    }
    else if (e.Key == Key.Enter) {
      RecentFilesListBox.SelectedItem = TextSearch.Text;
      InvokeOpenRecentDocument();
    }
  }
  
  private void ProfileTextSearch_Populating(object sender, PopulatingEventArgs e) {
    var box = (AutoCompleteBox)sender;
    box.ItemsSource = null;
    box.ItemsSource = App.Settings.ProfileOptions.PreviousLoadedSessions;
    box.PopulateComplete();
  }

  private void ProfileTextSearch_KeyDown(object sender, KeyEventArgs e) {
    if (e.Key == Key.Escape) {
      TextSearch.Text = "";
    }
    else if (e.Key == Key.Enter) {
      RecentProfilesListBox.SelectedItem = TextSearch.Text;
      InvokeOpenRecentProfileSession();
    }
  }

  private void RemoveButton_OnClick(object sender, RoutedEventArgs e) {
    if (((Button)sender).DataContext is string item) {
      App.Settings.RemoveRecentFile(item);
      ReloadFileList();
    }
  }

  private void DiffRemoveButton_OnClick(object sender, RoutedEventArgs e) {
    if (((Button)sender).DataContext is Tuple<string, string> item) {
      App.Settings.RemoveRecentComparedFiles(item);
      ReloadFileList();
    }
  }
  
  private void RemoveProfileButton_OnClick(object sender, RoutedEventArgs e) {
    if (((Button)sender).DataContext is RecordingSession session) {
      App.Settings.RemoveLoadedProfileSession(session.Report);
      ReloadFileList();
    }
  }

  private void RecordProfileButton_Click(object sender, RoutedEventArgs e) {
    RecordProfile?.Invoke(this, null);
  }

  private void LoadProfileButton_Click(object sender, RoutedEventArgs e) {
    LoadProfile?.Invoke(this, null);
  }
}
