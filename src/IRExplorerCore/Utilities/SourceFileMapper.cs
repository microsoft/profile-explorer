﻿// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.IO;

namespace IRExplorerCore;

public class SourceFileMapper {
  private readonly Dictionary<string, DirectoryInfo> map_ = new Dictionary<string, DirectoryInfo>();
  private readonly HashSet<string> missingFilesSet_ = new HashSet<string>();
  private readonly object lockObject_ = new object();

  public string Map(string sourceFile, Func<string> lookup) {
    lock (lockObject_) {
      if (TryLookupInMap(sourceFile, out string result)) {
        return result;
      }

      if (missingFilesSet_.Contains(sourceFile)) {
        return null;
      }

      result = lookup();

      if (result != null) {
        UpdateMap(sourceFile, result);
      }
      else {
        // Remember that the file couldn't be found so next time
        // it won't ask again for the same one.
        missingFilesSet_.Add(sourceFile);
      }

      return result;
    }
  }

  public void Reset() {
    lock (lockObject_) {
      missingFilesSet_.Clear();
    }
  }

  private bool TryLookupInMap(string sourceFile, out string result) {
    int index = sourceFile.LastIndexOf(Path.DirectorySeparatorChar);

    while (index != -1) {
      if (map_.TryGetValue(sourceFile.Substring(0, index), out var mappedDirectory)) {
        result = Path.Combine(mappedDirectory.FullName, sourceFile.Substring(index + 1));
        return true;
      }

      index = sourceFile.LastIndexOf(Path.DirectorySeparatorChar, index - 1);
    }

    result = null;
    return false;
  }

  private void UpdateMap(string originalPath, string mappedPath) {
    int prevOriginalPath = originalPath.Length;
    int prevMappedPath = mappedPath.Length;
    int originalPathIndex = originalPath.LastIndexOf(Path.DirectorySeparatorChar);
    int mappedPathIndex = mappedPath.LastIndexOf(Path.DirectorySeparatorChar);

    while (originalPathIndex != -1 && mappedPathIndex != -1) {
      if (originalPath.Substring(originalPathIndex, prevOriginalPath - originalPathIndex) !=
          mappedPath.Substring(mappedPathIndex, prevMappedPath - mappedPathIndex)) {
        return;
      }

      map_[originalPath.Substring(0, originalPathIndex)] = new DirectoryInfo(mappedPath.Substring(0, mappedPathIndex));
      prevOriginalPath = originalPathIndex;
      prevMappedPath = mappedPathIndex;

      originalPathIndex = originalPath.LastIndexOf(Path.DirectorySeparatorChar, prevOriginalPath - 1);
      mappedPathIndex = mappedPath.LastIndexOf(Path.DirectorySeparatorChar, prevMappedPath - 1);
    }
  }
}