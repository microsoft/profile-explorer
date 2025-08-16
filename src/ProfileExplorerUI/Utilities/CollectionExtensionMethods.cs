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
using ProfileExplorer.Core.IR;

namespace ProfileExplorer.UI;

public static class CollectionExtensionMethods {

  public static List<Tuple<K2, V>> ToList<K1, K2, V>(this Dictionary<K1, V> dict)
    where K1 : IRElement where K2 : IRElementReference {
    var list = new List<Tuple<K2, V>>(dict.Count);

    foreach (var item in dict) {
      list.Add(new Tuple<K2, V>((K2)item.Key, item.Value));
    }

    return list;
  }

  public static Dictionary<K2, V> ToDictionary<K1, K2, V>(this List<Tuple<K1, V>> list)
    where K1 : IRElementReference where K2 : IRElement {
    var dict = new Dictionary<K2, V>(list.Count);

    foreach (var item in list) {
      if (item.Item1 != null) {
        dict.Add((K2)item.Item1, item.Item2);
      }
    }

    return dict;
  }
}