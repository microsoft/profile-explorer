using System;
using System.Collections.Generic;
using ICSharpCode.AvalonEdit.Document;
using IRExplorerCore.IR;

namespace IRExplorerUI;

public static class CollectionExtensionMethods {
  public static List<T> CloneList<T>(this List<T> list) {
    return list.ConvertAll(item => item);
  }

  public static Dictionary<TKey, TValue> CloneDictionary<TKey, TValue>(
    this Dictionary<TKey, TValue> dict) {
    var newDict = new Dictionary<TKey, TValue>(dict.Count);

    foreach (var item in dict) {
      newDict.Add(item.Key, item.Value);
    }

    return newDict;
  }

  public static HashSet<T> CloneHashSet<T>(this HashSet<T> hashSet) {
    var newHashSet = new HashSet<T>(hashSet.Count);

    foreach (var item in hashSet) {
      hashSet.Add(item);
    }

    return newHashSet;
  }

  public static HashSet<T> ToHashSet<T>(this List<T> list) where T : class {
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

  public static List<T> ToList<T>(this HashSet<T> hashSet) where T : class {
    var list = new List<T>(hashSet.Count);

    foreach (var item in hashSet) {
      list.Add(item);
    }

    return list;
  }

  public static List<TOut> ToList<TIn, TOut>(this HashSet<TIn> hashSet, Func<TIn, TOut> action)
    where TIn : class {
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
    where K1 : IRElement where K2 : IRElementReference where V : class {
    var list = new List<Tuple<K2, V>>(dict.Count);

    foreach (var item in dict) {
      list.Add(new Tuple<K2, V>((K2)item.Key, item.Value));
    }

    return list;
  }

  public static Dictionary<K, V> ToDictionary<K, V>(this List<Tuple<K, V>> list)
    where K : class where V : class {
    var dict = new Dictionary<K, V>(list.Count);

    foreach (var item in list) {
      dict.Add(item.Item1, item.Item2);
    }

    return dict;
  }

  public static Dictionary<K2, V> ToDictionary<K1, K2, V>(this List<Tuple<K1, V>> list)
    where K1 : IRElementReference where K2 : IRElement where V : class {
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
      TValue value2;
      if (!second.TryGetValue(kvp.Key, out value2))
        return false;
      if (!valueComparer.Equals(kvp.Value, value2))
        return false;
    }

    return true;
  }

  public static int AccumulateValue<K>(this Dictionary<K, int> dict, K key, int value) {
    if (dict.TryGetValue(key, out int currentValue)) {
      int newValue = currentValue + value;
      dict[key] = newValue;
      return newValue;
    }

    dict[key] = value;
    return value;
  }

  public static long AccumulateValue<K>(this Dictionary<K, long> dict, K key, long value) {
    if (dict.TryGetValue(key, out long currentValue)) {
      long newValue = currentValue + value;
      dict[key] = newValue;
      return newValue;
    }

    dict[key] = value;
    return value;
  }

  public static TimeSpan AccumulateValue<K>(this Dictionary<K, TimeSpan> dict, K key, TimeSpan value) {
    if (dict.TryGetValue(key, out var currentValue)) {
      // The TimeSpan + operator does an overflow check that is not relevant
      // (and an exception undesirable), avoid it for some speedup.
      long sum = currentValue.Ticks + value.Ticks;
      var newValue = TimeSpan.FromTicks(sum);
      dict[key] = newValue;
      return newValue;
    }

    dict[key] = value;
    return value;
  }

  public static int CollectMaxValue<K>(this Dictionary<K, int> dict, K key, int value) {
    if (dict.TryGetValue(key, out int currentValue)) {
      if (value > currentValue) {
        dict[key] = value;
        return value;
      }

      return currentValue;
    }

    dict[key] = value;
    return value;
  }

  public static double CollectMaxValue<K>(this Dictionary<K, double> dict, K key, double value) {
    if (dict.TryGetValue(key, out double currentValue)) {
      if (value > currentValue) {
        dict[key] = value;
        return value;
      }

      return currentValue;
    }

    dict[key] = value;
    return value;
  }

  public static TimeSpan CollectMaxValue<K>(this Dictionary<K, TimeSpan> dict, K key, TimeSpan value) {
    if (dict.TryGetValue(key, out var currentValue)) {
      if (value > currentValue) {
        dict[key] = value;
        return value;
      }

      return currentValue;
    }

    dict[key] = value;
    return value;
  }
}