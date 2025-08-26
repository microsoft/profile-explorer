// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace ProfileExplorerCore.Utilities;

// Used to create an unique ID for an object, to be used
// for tracking in log files.
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