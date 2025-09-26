// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using ProfileExplorer.UI.Controls;
using ProfileExplorer.UI.Windows;

namespace ProfileExplorer.UI.Services;

public class SyntaxHighlightingFileCache : ISyntaxHighlightingFileCache {
  public string GetInternalSyntaxHighlightingFilePath(string name) {
    return App.GetInternalSyntaxHighlightingFilePath(name, App.Session.CompilerInfo.CompilerIRName);
  }

  public string GetSyntaxHighlightingFilePath(string path) {
    return App.GetSyntaxHighlightingFilePath(path);
  }

  public List<SyntaxFileInfo> ReloadSyntaxHighlightingFiles() {
    return App.ReloadSyntaxHighlightingFiles(App.Session.CompilerInfo.CompilerIRName);
  }
}