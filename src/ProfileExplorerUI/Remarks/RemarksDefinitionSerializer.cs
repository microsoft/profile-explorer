// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System.Collections.Generic;

namespace ProfileExplorer.UI;

class RemarksDefinitionSerializer {
  public bool Save(List<RemarkCategory> categories,
                   List<RemarkSectionBoundary> boundaries,
                   List<RemarkTextHighlighting> highlighting,
                   string path) {
    var data = new SerializedData {
      RemarkCategoryList = categories,
      SectionBoundaryList = boundaries
    };

    return JsonUtils.SerializeToFile(data, path);
  }

  public bool Load(string path,
                   out List<RemarkCategory> categories,
                   out List<RemarkSectionBoundary> boundaries,
                   out List<RemarkTextHighlighting> highlighting) {
    if (JsonUtils.DeserializeFromFile(path, out SerializedData data)) {
      categories = data.RemarkCategoryList;
      boundaries = data.SectionBoundaryList;
      highlighting = data.RemarkHighlightingList;
      return categories != null &&
             boundaries != null &&
             highlighting != null;
    }

    categories = new List<RemarkCategory>();
    boundaries = new List<RemarkSectionBoundary>();
    highlighting = new List<RemarkTextHighlighting>();
    return false;
  }

  private class SerializedData {
    public string Comment1 =>
      "Add remark definitions to the list found below, including the kind, searched text that should be part of the remark, a type of search to perform (simple/regex,etc) and how to highlight the remark, using an underline and/or marker on the left-side margin with a certain color";
    public string Comment2 =>
      "Remark categories are defined in the RemarkCategoryList, remark section stop boundaries in SectionBoundaryList";
    public string Comment3 =>
      "Use the KindValues/SearchKindValues strings listed below as the enumeration values for the corresponding members";
    public string[] KindValues =>
      new[] {
        "default", "optimization", "analysis", "verbose", "trace"
      };
    public string[] SearchKindValues =>
      new[] {
        "default", "regex", "caseSensitive", "wholeWord"
      };
    public List<RemarkCategory> RemarkCategoryList { get; set; }
    public List<RemarkSectionBoundary> SectionBoundaryList { get; set; }
    public List<RemarkTextHighlighting> RemarkHighlightingList { get; set; }
  }
}