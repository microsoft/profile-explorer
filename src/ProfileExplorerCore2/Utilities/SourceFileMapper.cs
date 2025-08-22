// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.IO;

namespace ProfileExplorerCore2;

public class SourceFileMapper {
  private readonly Dictionary<string, string> sourcePathMap_;
  private readonly Dictionary<string, string> sourceFileCache_;
  private readonly object lockObject_ = new();

  public SourceFileMapper(Dictionary<string, string> sourcePathMap = null) {
    sourcePathMap_ = sourcePathMap;
    sourcePathMap_ ??= new Dictionary<string, string>(); // Saved across sessions.
    sourceFileCache_ = new Dictionary<string, string>(); // Active per session.
  }

  public Dictionary<string, string> SourceMap => sourcePathMap_;

  public string Map(string sourceFile, Func<string> lookup = null) {
    if (string.IsNullOrEmpty(sourceFile)) {
      return null;
    }

    lock (lockObject_) {
      if (TryLookupInMap(sourceFile, out string result)) {
        return result;
      }

      if (lookup == null) {
        return null;
      }

      result = lookup();

      if (result != null) {
        UpdateMap(sourceFile, result);
      }

      return result;
    }
  }

  public void Reset() {
    lock (lockObject_) {
      sourcePathMap_.Clear();
      sourcePathMap_.Clear();
    }
  }

  private bool TryLookupInMap(string sourceFile, out string result) {
    // Check the direct mapping cache first.
    if (sourceFileCache_.TryGetValue(sourceFile, out result)) {
      return true;
    }

    // Use the past directory mappings to build the equivalent
    // local path for the source file.
    int index = sourceFile.LastIndexOf(Path.DirectorySeparatorChar);

    while (index > 0) {
      if (sourcePathMap_.TryGetValue(sourceFile.Substring(0, index), out string mappedDirectory)) {
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

    sourceFileCache_[originalPath] = mappedPath;

    // Try to create a mapping between the directory paths,
    // to be used later with another source file part of the same
    // directory structure.
    int prevOriginalPath = originalPath.Length;
    int prevMappedPath = mappedPath.Length;
    int originalPathIndex = originalPath.LastIndexOf(Path.DirectorySeparatorChar);
    int mappedPathIndex = mappedPath.LastIndexOf(Path.DirectorySeparatorChar);

    while (originalPathIndex > 0 && mappedPathIndex > 0) {
      // Stop once there is a mismatch in directory names.
      // Use a case-insensitive compare for Windows paths.
      if (!originalPath.Substring(originalPathIndex, prevOriginalPath - originalPathIndex).Equals(
        mappedPath.Substring(mappedPathIndex, prevMappedPath - mappedPathIndex),
        StringComparison.OrdinalIgnoreCase)) {
        return;
      }

      sourcePathMap_[originalPath.Substring(0, originalPathIndex)] = mappedPath.Substring(0, mappedPathIndex);
      prevOriginalPath = originalPathIndex;
      prevMappedPath = mappedPathIndex;

      originalPathIndex = originalPath.LastIndexOf(Path.DirectorySeparatorChar, prevOriginalPath - 1);
      mappedPathIndex = mappedPath.LastIndexOf(Path.DirectorySeparatorChar, prevMappedPath - 1);
    }
  }
}