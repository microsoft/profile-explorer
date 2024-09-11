// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.IO;

namespace ProfileExplorer.Core;

public class SourceFileMapper {
  private readonly Dictionary<string, string> map_;
  private readonly HashSet<string> missingFilesSet_;
  private readonly object lockObject_ = new();

  public SourceFileMapper(Dictionary<string, string> map = null) {
    map_ = map;
    map_ ??= new Dictionary<string, string>();
    missingFilesSet_ = new HashSet<string>();
  }

  public Dictionary<string, string> SourceMap => map_;

  public string Map(string sourceFile, Func<string> lookup = null) {
    if (string.IsNullOrEmpty(sourceFile)) {
      return null;
    }

    lock (lockObject_) {
      if (TryLookupInMap(sourceFile, out string result)) {
        return result;
      }

      if (missingFilesSet_.Contains(sourceFile)) {
        return null;
      }

      if (lookup == null) {
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
      map_.Clear();
      missingFilesSet_.Clear();
    }
  }

  public void ResetMissingFiles() {
    lock (lockObject_) {
      missingFilesSet_.Clear();
    }
  }

  public void ResetMissingFile(string filePath) {
    lock (lockObject_) {
      missingFilesSet_.Remove(filePath);
    }
  }

  private bool TryLookupInMap(string sourceFile, out string result) {
    int index = sourceFile.LastIndexOf(Path.DirectorySeparatorChar);

    while (index > 0) {
      if (map_.TryGetValue(sourceFile.Substring(0, index), out string mappedDirectory)) {
        result = Path.Combine(mappedDirectory, sourceFile.Substring(index + 1));
        return true;
      }

      index = sourceFile.LastIndexOf(Path.DirectorySeparatorChar, index - 1);
    }

    result = null;
    return false;
  }

  public void UpdateMap(string originalPath, string mappedPath) {
    if (string.IsNullOrEmpty(originalPath) ||
        string.IsNullOrEmpty(mappedPath)) {
      return;
    }

    int prevOriginalPath = originalPath.Length;
    int prevMappedPath = mappedPath.Length;
    int originalPathIndex = originalPath.LastIndexOf(Path.DirectorySeparatorChar);
    int mappedPathIndex = mappedPath.LastIndexOf(Path.DirectorySeparatorChar);

    while (originalPathIndex > 0 && mappedPathIndex > 0) {
      if (originalPath.Substring(originalPathIndex, prevOriginalPath - originalPathIndex) !=
          mappedPath.Substring(mappedPathIndex, prevMappedPath - mappedPathIndex)) {
        return;
      }

      map_[originalPath.Substring(0, originalPathIndex)] = mappedPath.Substring(0, mappedPathIndex);
      prevOriginalPath = originalPathIndex;
      prevMappedPath = mappedPathIndex;

      originalPathIndex = originalPath.LastIndexOf(Path.DirectorySeparatorChar, prevOriginalPath - 1);
      mappedPathIndex = mappedPath.LastIndexOf(Path.DirectorySeparatorChar, prevMappedPath - 1);
    }
  }
}