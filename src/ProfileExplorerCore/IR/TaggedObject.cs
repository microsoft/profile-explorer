// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System.Collections.Generic;
using System.Text;
using ProfileExplorer.Core.Utilities;

namespace ProfileExplorer.Core.IR;

public class TaggedObject {
  public List<ITag> Tags { get; set; }

  public void AddTag(ITag tag) {
    Tags ??= new List<ITag>();
    tag.Owner = this;
    Tags.Add(tag);
  }

  public T GetTag<T>() where T : class {
    if (Tags != null) {
      foreach (var tag in Tags) {
        if (tag is T value) {
          return value;
        }
      }
    }

    return null;
  }

  public bool HasTag<T>() where T : class {
    return GetTag<T>() != null;
  }

  public bool TryGetTag<T>(out T result) where T : class {
    result = GetTag<T>();
    return result != null;
  }

  public T GetOrAddTag<T>() where T : class, new() {
    var result = GetTag<T>();

    if (result != null) {
      return result;
    }

    var tag = new T();
    AddTag(tag as ITag);
    return tag;
  }

  public bool RemoveTag<T>() where T : class {
    if (Tags != null) {
      return Tags.RemoveAll(tag => tag is T) > 0;
    }

    return false;
  }

  public override string ToString() {
    if (Tags == null || Tags.Count == 0) {
      return "";
    }

    var builder = new StringBuilder();
    builder.AppendLine($"{Tags.Count} tags:");

    foreach (var tag in Tags) {
      builder.AppendLine($"  o {tag}".Indent(4));
    }

    return builder.ToString();
  }
}