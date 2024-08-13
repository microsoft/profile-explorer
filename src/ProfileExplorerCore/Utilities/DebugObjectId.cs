// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace ProfileExplorer.Core;

public static class ObjectTracker {
  private static ConditionalWeakTable<object, DebugObjectId> DebugTaskId = new();

  public static DebugObjectId Track(object value) {
    if (value == null) {
      return new DebugObjectId();
    }

    return DebugTaskId.GetValue(value, obj => new DebugObjectId(obj));
  }

  public class DebugObjectId {
    private static Dictionary<string, int> PrefixNumbers = new();
    private static object LockObject = new();

    public DebugObjectId() {
      Id = "<null>";
    }

    public DebugObjectId(object value) {
      string typeName = value.GetType().Name.ToLower();
      string prefix = typeName.Substring(0, Math.Min(12, typeName.Length));
      int number = 1;

      lock (LockObject) {
        if (PrefixNumbers.TryGetValue(prefix, out number)) {
          ++number;
          PrefixNumbers[prefix] = number;
        }
        else {
          PrefixNumbers[prefix] = number;
        }
      }

      Id = $"{prefix}-{number}";
    }

    public string Id { get; set; }

    public override string ToString() {
      return Id;
    }
  }
}