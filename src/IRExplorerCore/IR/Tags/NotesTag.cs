// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;

namespace IRExplorerCore.IR.Tags;

public class NotesTag : ITag {
  public NotesTag() {
    Title = "";
    Notes = new List<string>();
  }

  public NotesTag(string title) : this() {
    Title = title;
  }

  public string Title { get; set; }
  public List<string> Notes { get; set; }
  public string Name => "Notes";
  public TaggedObject Owner { get; set; }

  public override bool Equals(object obj) {
    return obj is NotesTag tag &&
           Title == tag.Title &&
           EqualityComparer<List<string>>.Default.Equals(Notes, tag.Notes);
  }

  public override int GetHashCode() {
    return HashCode.Combine(Title, Notes);
  }

  public override string ToString() {
    return $"Notes title: {Title}";
  }
}
