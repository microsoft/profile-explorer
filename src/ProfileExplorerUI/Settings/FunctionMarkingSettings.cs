// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using ProfileExplorerCore.Settings;
using ProtoBuf;

namespace ProfileExplorer.UI;

[ProtoContract(SkipConstructor = true)]
public class FunctionMarkingSettings : SettingsBase {
  private ColorPalette modulesPalette_;
  private FunctionMarkingSet builtinMarking_;

  public FunctionMarkingSettings() {
    Reset();
  }

  [ProtoMember(1)][OptionValue("")]
  public string Title { get; set; }
  [ProtoMember(2)][OptionValue(true)]
  public bool UseAutoModuleColors { get; set; }
  [ProtoMember(3)][OptionValue(false)]
  public bool UseModuleColors { get; set; }
  [ProtoMember(4)][OptionValue(false)]
  public bool UseFunctionColors { get; set; }
  [ProtoMember(5)][OptionValue("LightPastels")]
  public string ModulesColorPalette { get; set; }
  [ProtoMember(6)][OptionValue()]
  public FunctionMarkingSet CurrentSet { get; set; }
  [ProtoMember(7)][OptionValue()]
  public List<FunctionMarkingSet> SavedSets { get; set; }
  public List<FunctionMarkingStyle> ModuleColors => CurrentSet?.ModuleColors;
  public List<FunctionMarkingStyle> FunctionColors => CurrentSet?.FunctionColors;
  public bool HasEnabledFunctionMarkings => UseFunctionColors &&
                                            FunctionColors.Find(item => item.IsEnabled) != null;
  public bool HasEnabledModuleMarkings => UseModuleColors &&
                                          ModuleColors.Find(item => item.IsEnabled) != null;

  public FunctionMarkingSet BuiltinMarkingCategories {
    get {
      if (builtinMarking_ != null) {
        return builtinMarking_;
      }

      string markingsFile = App.GetFunctionMarkingsFilePath(App.Session.CompilerInfo.CompilerIRName);

      if (!JsonUtils.DeserializeFromFile<FunctionMarkingSet>(markingsFile, out builtinMarking_)) {
        builtinMarking_ = new FunctionMarkingSet();
      }

      return builtinMarking_;
    }
  }

  public bool ImportMarkings(FrameworkElement owner) {
    string filePath = Utils.ShowOpenFileDialog("JSON files|*.json", "*.*", "Import markings from file");

    if (filePath != null) {
      (bool result, string failureText) = LoadFromFile(filePath);

      if (!result) {
        Utils.ShowWarningMessageBox($"Failed to import markings from {filePath}.\n{failureText}", owner);
        return false;
      }

      return true;
    }

    return false;
  }

  public bool ExportMarkings(FrameworkElement owner) {
    string filePath = Utils.ShowSaveFileDialog("JSON files|*.json", "*.*", "Export markings to file");

    if (filePath != null) {
      if (!SaveToFile(filePath)) {
        Utils.ShowWarningMessageBox($"Failed to export markings to {filePath}", owner);
        return false;
      }

      return true;
    }

    return false;
  }

  public void SwitchMarkingSet(FunctionMarkingSet set) {
    CurrentSet = set.Clone();
  }

  public void AppendMarkingSet(FunctionMarkingSet set) {
    foreach (var marking in set.FunctionColors) {
      AddFunctionColor(marking);
    }

    foreach (var marking in set.ModuleColors) {
      AddModuleColor(marking);
    }
  }

  public void SaveCurrentMarkingSet(string title) {
    SavedSets.RemoveAll(set => set.Title == title);
    var clone = CurrentSet.Clone();
    clone.Title = title;
    SavedSets.Add(clone);
  }

  public void RemoveMarkingSet(FunctionMarkingSet markingSet) {
    SavedSets.RemoveAll(set => set.Title == markingSet.Title);
  }

  public bool SaveToFile(string filePath) {
    var markings = new Markings(CurrentSet, SavedSets);
    return JsonUtils.SerializeToFile(markings, filePath);
  }

  public (bool, string) LoadFromFile(string filePath) {
    if (!JsonUtils.DeserializeFromFile(filePath, out Markings data)) {
      return (false, "Failed to read markings file");
    }

    if (!ValidateMarkings(data, out string failureText)) {
      return (false, failureText);
    }

    CurrentSet.MergeWith(data.Current);

    foreach (var savedSet in data.Saved) {
      var existingSet = SavedSets.Find(set => set.Title == savedSet.Title);

      if (existingSet != null) {
        existingSet.MergeWith(savedSet);
      }
      else {
        SavedSets.Add(savedSet);
      }
    }

    return (true, null);
  }

  private bool ValidateMarkings(Markings data, out string failureText) {
    failureText = null;

    if (!data.Current.ValidateMarkings(out failureText)) {
      return false;
    }

    foreach (var set in data.Saved) {
      if (!set.ValidateMarkings(out failureText)) {
        return false;
      }
    }

    return true;
  }

  public override void Reset() {
    InitializeReferenceMembers();
    ResetAllOptions(this);
    ResetCachedPalettes();
  }

  [ProtoAfterDeserialization]
  private void InitializeReferenceMembers() {
    InitializeReferenceOptions(this);
  }

  public FunctionMarkingSettings Clone() {
    byte[] serialized = StateSerializer.Serialize(this);
    return StateSerializer.Deserialize<FunctionMarkingSettings>(serialized);
  }

  public void AddModuleColor(string moduleName, Color color) {
    AddMarkingColor(moduleName, color, ModuleColors);
  }

  public void AddFunctionColor(string functionName, Color color) {
    AddMarkingColor(functionName, color, FunctionColors);
  }

  public void AddModuleColor(FunctionMarkingStyle style) {
    AddMarkingColor(style, ModuleColors);
  }

  public void AddFunctionColor(FunctionMarkingStyle style) {
    AddMarkingColor(style, FunctionColors);
  }

  private void AddMarkingColor(string name, Color color, List<FunctionMarkingStyle> markingColors) {
    AddMarkingColor(new FunctionMarkingStyle(name, color), markingColors);
  }

  private void AddMarkingColor(FunctionMarkingStyle style, List<FunctionMarkingStyle> markingColors) {
    // Overwrite marking by removing exact matches.
    var existing = markingColors.Find(item => item.Name.Equals(style.Name, StringComparison.Ordinal));

    if (existing != null) {
      if (existing.IsEnabled) {
        style.IsEnabled = true; // Keep enabled state.
      }

      markingColors.Remove(existing);
    }

    markingColors.Add(style);
  }

  public bool GetFunctionColor(string name, out Color color) {
    return GetMarkingColor(name, FunctionColors, out color);
  }

  private bool GetMarkingColor(string name, List<FunctionMarkingStyle> markingColors, out Color color) {
    foreach (var pair in markingColors) {
      if (pair.IsEnabled && pair.NameMatches(name)) {
        color = pair.Color;
        return true;
      }
    }

    color = default(Color);
    return false;
  }

  public Brush GetAutoModuleBrush(string name) {
    if (modulesPalette_ == null) {
      modulesPalette_ = ColorPalette.GetPalette(ModulesColorPalette);
    }

    if (modulesPalette_ != null) {
      return modulesPalette_.PickBrush(name);
    }

    return ColorBrushes.Transparent;
  }

  public bool GetModuleColor(string name, out Color color) {
    return GetMarkingColor(name, ModuleColors, out color);
  }

  public bool GetModuleBrush(string name, out Brush brush) {
    if (GetMarkingColor(name, ModuleColors, out var color)) {
      brush = ColorBrushes.GetBrush(color);
      return true;
    }

    brush = ColorBrushes.Transparent;
    return false;
  }

  public Brush GetMarkedNodeBrush(string funcName, string moduleName) {
    if (UseFunctionColors &&
        GetFunctionColor(funcName, out var color)) {
      return ColorBrushes.GetBrush(color);
    }
    else if (UseModuleColors &&
             GetModuleColor(moduleName, out var color2)) {
      return ColorBrushes.GetBrush(color2);
    }

    return null;
  }

  public void ResetCachedPalettes() {
    modulesPalette_ = null;
  }

  public override bool Equals(object obj) {
    return AreOptionsEqual(this, obj);
  }

  public override string ToString() {
    return PrintOptions(this);
  }

  private record Markings(FunctionMarkingSet Current, List<FunctionMarkingSet> Saved);
}

[ProtoContract(SkipConstructor = true)]
public class FunctionMarkingSet : SettingsBase {
  public FunctionMarkingSet() {
    InitializeReferenceMembers();
  }

  [ProtoMember(1)][OptionValue()]
  public List<FunctionMarkingStyle> ModuleColors { get; set; }
  [ProtoMember(2)][OptionValue()]
  public List<FunctionMarkingStyle> FunctionColors { get; set; }
  [ProtoMember(3)][OptionValue("")]
  public string Title { get; set; }

  public FunctionMarkingSet Clone() {
    var clone = new FunctionMarkingSet();

    foreach (var marking in FunctionColors) {
      clone.FunctionColors.Add(marking.Clone());
    }

    foreach (var marking in ModuleColors) {
      clone.ModuleColors.Add(marking.Clone());
    }

    return clone;
  }

  [ProtoAfterDeserialization]
  private void InitializeReferenceMembers() {
    InitializeReferenceOptions(this);
  }

  public override bool Equals(object obj) {
    return AreOptionsEqual(this, obj);
  }

  public override string ToString() {
    return PrintOptions(this);
  }

  public void MergeWith(FunctionMarkingSet set) {
    foreach (var marking in set.FunctionColors) {
      FunctionColors.RemoveAll(item =>
                                 item.Name.Equals(marking.Name, StringComparison.Ordinal));
      FunctionColors.Add(marking.Clone());
    }

    foreach (var marking in set.ModuleColors) {
      ModuleColors.RemoveAll(item =>
                               item.Name.Equals(marking.Name, StringComparison.Ordinal));
      ModuleColors.Add(marking.Clone());
    }
  }

  public bool ValidateMarkings(out string failureText) {
    foreach (var marking in FunctionColors) {
      if (!marking.ValidateMarking(out failureText)) {
        return false;
      }
    }

    foreach (var marking in ModuleColors) {
      if (!marking.ValidateMarking(out failureText)) {
        return false;
      }
    }

    failureText = null;
    return true;
  }
}

[ProtoContract(SkipConstructor = true)]
public class FunctionMarkingStyle : SettingsBase {
  private TextSearcher searcher_;
  private string name_;

  public FunctionMarkingStyle(string name, Color color,
                              string title = null, bool isRegex = false,
                              bool isEnabled = true) {
    Name = name;
    Color = color;
    Title = title;
    IsRegex = isRegex;
    IsEnabled = isEnabled;
  }

  [ProtoMember(1)]
  public bool IsEnabled { get; set; }
  [ProtoMember(2)]
  public string Title { get; set; }

  [ProtoMember(3)]
  public string Name {
    get => name_;
    set {
      name_ = value;
      searcher_ = new TextSearcher(); // Caches precompiled Regex.
    }
  }

  [ProtoMember(4)]
  public Color Color { get; set; }
  [ProtoMember(5)]
  public bool IsRegex { get; set; }
  public bool HasTitle => !string.IsNullOrEmpty(Title);
  public string TitleOrName => HasTitle ? Title : Name;

  public bool NameMatches(string candidate) {
    if (candidate == null ||
        candidate.Length <= 0 || Name.Length <= 0) {
      return false;
    }

    if (IsRegex) {
      // Regex allows partial match.
      return searcher_.Includes(candidate, Name, TextSearchKind.Regex);
    }
    else {
      // Otherwise accept full match only, not substring,
      // otherwise it finds all functions with a prefix/suffix too.
      return Name.Equals(candidate, StringComparison.Ordinal);
    }
  }

  public FunctionMarkingStyle CloneWithNewColor(Color newColor) {
    return new FunctionMarkingStyle(Name, newColor, Title, IsRegex, IsEnabled);
  }

  public FunctionMarkingStyle Clone() {
    return new FunctionMarkingStyle(Name, Color, Title, IsRegex, IsEnabled);
  }

  public override bool Equals(object obj) {
    return AreOptionsEqual(this, obj);
  }

  public override string ToString() {
    return PrintOptions(this);
  }

  public bool ValidateMarking(out string failureText) {
    if (IsRegex && !ValidateRegex(Name)) {
      failureText = $"Invalid Regex for marking definition.\n";
      failureText += $"  Title: {Title}\n";
      failureText += $"  Pattern: {Name}\n";
      return false;
    }

    failureText = null;
    return true;
  }

  private bool ValidateRegex(string pattern) {
    if (string.IsNullOrEmpty(pattern)) {
      return false;
    }

    try {
      var re = new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(1));
      re.IsMatch(" ");
    }
    catch { return false; }

    return true;
  }
}