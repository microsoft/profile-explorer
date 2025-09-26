// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProfileExplorer.UI.Services;

public interface ISyntaxHighlightingFileCache {
  string GetSyntaxHighlightingFilePath(string path);

  string GetInternalSyntaxHighlightingFilePath(string name);

  List<SyntaxFileInfo> ReloadSyntaxHighlightingFiles();
}
  