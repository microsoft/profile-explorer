// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System.Collections.Generic;
using System.Windows.Media;
using ProfileExplorer.Core;

namespace ProfileExplorer.UI;

public interface ISectionStyleProvider {
  string SettingsFilePath { get; }
  bool SaveSettings();
  bool LoadSettings();
  bool IsMarkedSection(IRTextSection section, out MarkedSectionName result);
}

public class MarkedSectionName {
  public MarkedSectionName() { }

  public MarkedSectionName(string text, TextSearchKind searchKind) {
    SearchedText = text;
    SearchKind = searchKind;
    TextColor = Colors.Transparent;
  }

  public MarkedSectionName(string text, TextSearchKind searchKind, Color textColor) {
    SearchedText = text;
    SearchKind = searchKind;
    TextColor = textColor;
  }

  public string SearchedText { get; set; }
  public TextSearchKind SearchKind { get; set; }
  public Color TextColor { get; set; }
  public Color SeparatorColor { get; set; }
  public int BeforeSeparatorWeight { get; set; }
  public int AfterSeparatorWeight { get; set; }
  public int IndentationLevel { get; set; }
}

class DummySectionStyleProvider : ISectionStyleProvider {
  public string SettingsFilePath { get; }

  public bool SaveSettings() {
    return true;
  }

  public bool LoadSettings() {
    return true;
  }

  public bool IsMarkedSection(IRTextSection section, out MarkedSectionName result) {
    result = null;
    return false;
  }
}

class SectionStyleProviderSerializer {
  public bool Save(List<MarkedSectionName> sectionNameMarkers, string path) {
    var data = new SerializedData {
      List = sectionNameMarkers
    };

    return JsonUtils.SerializeToFile(data, path);
  }

  public bool Load(string path, out List<MarkedSectionName> sectionNameMarkers) {
    if (JsonUtils.DeserializeFromFile(path, out SerializedData data)) {
      sectionNameMarkers = data.List;
      return true;
    }

    sectionNameMarkers = new List<MarkedSectionName>();
    return false;
  }

  private class SerializedData {
    public string Comment => "Description";
    public string[] SearchKindValues =>
      new[] {
        "default", "regex", "caseSensitive", "wholeWord"
      };
    public List<MarkedSectionName> List { get; set; }
  }
}