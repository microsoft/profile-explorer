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
using ProfileExplorerCore.IR;

namespace ProfileExplorer.UI;

public static class CollectionExtensionMethods {
  public static List<T> CloneList<T>(this List<T> list) {
    if (list == null) {
      return null;
    }

    return list.ConvertAll(item => item);
  }

  public static bool AreEqual<T>(this List<T> list, List<T> other) {
    if (ReferenceEquals(list, other)) {
      return true;
    }
    else if (list == null || other == null ||
             list.Count != other.Count) {
      return false;
    }

    return list.SequenceEqual(other);
  }

  public static bool AreEqual(this IList list, IList other) {
    if (ReferenceEquals(list, other)) {
      return true;
    }
    else if (list == null || other == null ||
             list.Count != other.Count) {
      return false;
    }

    for (int i = 0; i < list.Count; i++) {
      if (!Equals(list[i], other[i])) {
        return false;
      }
    }

    return true;
  }

  public static Dictionary<TKey, TValue> CloneDictionary<TKey, TValue>(
    this Dictionary<TKey, TValue> dict) {
    if (dict == null) {
      return null;
    }

    var newDict = new Dictionary<TKey, TValue>(dict.Count);

    foreach (var item in dict) {
      newDict.Add(item.Key, item.Value);
    }

    return newDict;
  }

  public static HashSet<T> CloneHashSet<T>(this HashSet<T> hashSet) {
    if (hashSet == null) {
      return null;
    }

    var newHashSet = new HashSet<T>(hashSet.Count);

    foreach (var item in hashSet) {
      newHashSet.Add(item);
    }

    return newHashSet;
  }

  public static HashSet<T> ToHashSet<T>(this List<T> list) {
    var hashSet = new HashSet<T>(list.Count);
    list.ForEach(item => hashSet.Add(item));
    return hashSet;
  }

  public static HashSet<TOut> ToHashSet<TIn, TOut>(this List<TIn> list, Func<TIn, TOut> action)
    where TIn : class where TOut : class {
    var hashSet = new HashSet<TOut>(list.Count);
    list.ForEach(item => hashSet.Add(action(item)));
    return hashSet;
  }

  public static List<T> ToList<T>(this HashSet<T> hashSet) {
    var list = new List<T>(hashSet.Count);

    foreach (var item in hashSet) {
      list.Add(item);
    }

    return list;
  }

  public static List<TOut> ToList<TIn, TOut>(this HashSet<TIn> hashSet, Func<TIn, TOut> action) {
    var list = new List<TOut>(hashSet.Count);

    foreach (var item in hashSet) {
      list.Add(action(item));
    }

    return list;
  }

  public static List<T> ToList<T>(this TextSegmentCollection<T> segments) where T : TextSegment {
    var list = new List<T>(segments.Count);

    foreach (var item in segments) {
      list.Add(item);
    }

    return list;
  }

  public static List<(K, V)> ToList<K, V>(this IDictionary<K, V> dict) {
    var list = new List<(K, V)>(dict.Count);

    foreach (var item in dict) {
      list.Add((item.Key, item.Value));
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

  public static List<K> ToKeyList<K, V>(this IDictionary<K, V> dict) {
    var list = new List<K>(dict.Count);

    foreach (var item in dict) {
      list.Add(item.Key);
    }

    return list;
  }

  public static List<V> ToValueList<K, V>(this IDictionary<K, V> dict) {
    var list = new List<V>(dict.Count);

    foreach (var item in dict) {
      list.Add(item.Value);
    }

    return list;
  }

  public static List<Tuple<K2, V>> ToList<K1, K2, V>(this Dictionary<K1, V> dict)
    where K1 : IRElement where K2 : IRElementReference {
    var list = new List<Tuple<K2, V>>(dict.Count);

    foreach (var item in dict) {
      list.Add(new Tuple<K2, V>((K2)item.Key, item.Value));
    }

    return list;
  }

  public static Dictionary<K, V> ToDictionary<K, V>(this List<Tuple<K, V>> list) {
    var dict = new Dictionary<K, V>(list.Count);

    foreach (var item in list) {
      dict.Add(item.Item1, item.Item2);
    }

    return dict;
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

  public static bool AreEqual<TKey, TValue>(this Dictionary<TKey, TValue> first,
                                            Dictionary<TKey, TValue> second) {
    if (first == second)
      return true;
    if (first == null || second == null)
      return false;
    if (first.Count != second.Count)
      return false;

    var valueComparer = EqualityComparer<TValue>.Default;

    foreach (var kvp in first) {
      if (!second.TryGetValue(kvp.Key, out var value2))
        return false;
      if (!valueComparer.Equals(kvp.Value, value2))
        return false;
    }

    return true;
  }

  public static bool AreEqual(this IDictionary first, IDictionary second) {
    if (first == second)
      return true;
    if (first == null || second == null)
      return false;
    if (first.Count != second.Count)
      return false;

    foreach (DictionaryEntry kvp in first) {
      if (!second.Contains(kvp.Key))
        return false;
      if (!Equals(kvp.Value, second[kvp.Key]))
        return false;
    }

    return true;
  }

  public static bool AreEqual<T>(this HashSet<T> first, HashSet<T> second) {
    if (first == second)
      return true;
    if (first == null || second == null)
      return false;
    if (first.Count != second.Count)
      return false;

    var valueComparer = EqualityComparer<T>.Default;

    foreach (var value in first) {
      if (!second.TryGetValue(value, out var value2))
        return false;
      if (!valueComparer.Equals(value, value2))
        return false;
    }

    return true;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static void AccumulateValue<K>(this Dictionary<K, int> dict, K key, int value) {
    ref int currentValue = ref CollectionsMarshal.GetValueRefOrAddDefault(dict, key, out bool exists);
    currentValue += value;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static void AccumulateValue<K>(this Dictionary<K, long> dict, K key, long value) {
    ref long currentValue = ref CollectionsMarshal.GetValueRefOrAddDefault(dict, key, out bool exists);
    currentValue += value;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static void AccumulateValue<K>(this Dictionary<K, TimeSpan> dict, K key, TimeSpan value) {
    ref var currentValue = ref CollectionsMarshal.GetValueRefOrAddDefault(dict, key, out bool exists);

    // The TimeSpan + operator does an overflow check that is not relevant
    // (and an exception undesirable), avoid it for some speedup.
    long sum = currentValue.Ticks + value.Ticks;
    var newValue = TimeSpan.FromTicks(sum);
    currentValue = newValue;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static void
    AccumulateValue<K>(this Dictionary<K, (TimeSpan, TimeSpan)> dict, K key,
                       TimeSpan value1, TimeSpan value2) {
    ref var currentValue = ref CollectionsMarshal.GetValueRefOrAddDefault(dict, key, out bool exists);

    // The TimeSpan + operator does an overflow check that is not relevant
    // (and an exception undesirable), avoid it for some speedup.
    long sum1 = currentValue.Item1.Ticks + value1.Ticks;
    long sum2 = currentValue.Item2.Ticks + value2.Ticks;
    var newValue1 = TimeSpan.FromTicks(sum1);
    var newValue2 = TimeSpan.FromTicks(sum2);
    currentValue.Item1 = newValue1;
    currentValue.Item2 = newValue2;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static int CollectMaxValue<K>(this Dictionary<K, int> dict, K key, int value) {
    ref int currentValue = ref CollectionsMarshal.GetValueRefOrAddDefault(dict, key, out bool exists);

    if (exists) {
      if (value > currentValue) {
        currentValue = value;
        return value;
      }

      return currentValue;
    }

    currentValue = value;
    return value;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static double CollectMaxValue<K>(this Dictionary<K, double> dict, K key, double value) {
    ref double currentValue = ref CollectionsMarshal.GetValueRefOrAddDefault(dict, key, out bool exists);

    if (exists) {
      if (value > currentValue) {
        currentValue = value;
        return value;
      }

      return currentValue;
    }

    currentValue = value;
    return value;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static TimeSpan CollectMaxValue<K>(this Dictionary<K, TimeSpan> dict, K key, TimeSpan value) {
    ref var currentValue = ref CollectionsMarshal.GetValueRefOrAddDefault(dict, key, out bool exists);

    if (exists) {
      if (value > currentValue) {
        currentValue = value;
        return value;
      }

      return currentValue;
    }

    currentValue = value;
    return value;
  }
}