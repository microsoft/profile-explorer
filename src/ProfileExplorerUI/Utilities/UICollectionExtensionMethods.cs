// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Data;
using ICSharpCode.AvalonEdit.Document;
using ProfileExplorer.Core.Controls;
using ProfileExplorer.Core.IR;

namespace ProfileExplorer.UI;

public static class UICollectionExtensionMethods {
  public static List<T> ToList<T>(this TextSegmentCollection<T> segments) where T : TextSegment {
    var list = new List<T>(segments.Count);

    foreach (var item in segments) {
      list.Add(item);
    }

    return list;
  }

  public static List<T> ToList<T>(this ListCollectionView view) {
    var list = new List<T>(view.Count);

    foreach (T item in view) {
      list.Add(item);
    }

    return list;
  }
}