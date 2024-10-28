// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Controls;

namespace ProfileExplorer.UI.Controls;

public class FileSystemTextBox : AutoCompleteBox {
  public string ExtensionFilter { get; set; }
  public bool ShowOnlyDirectories { get; set; }

  protected override void OnInitialized(EventArgs e) {
    base.OnInitialized(e);
    ExtensionFilter = "*.*";
    Populating += FileSystemTextBox_Populating;
  }

  private void FileSystemTextBox_Populating(object sender, PopulatingEventArgs e) {
    var box = sender as FileSystemTextBox;

    try {
      string text = box.Text;
      string dirname = Path.GetDirectoryName(text);

      if (dirname != null && Directory.Exists(dirname)) {
        string[] files = Directory.GetFiles(dirname, ExtensionFilter, SearchOption.TopDirectoryOnly);
        string[] dirs = Directory.GetDirectories(dirname, "*", SearchOption.TopDirectoryOnly);
        var candidates = new List<string>(files.Length + dirs.Length);

        foreach (string f in dirs) {
          candidates.Add(f);
        }

        if (!ShowOnlyDirectories) {
          foreach (string f in files) {
            candidates.Add(f);
          }
        }

        box.ItemsSource = candidates;
        box.PopulateComplete();
        return;
      }
    }
    catch { }

    box.ItemsSource = null;
    box.PopulateComplete();
  }
}