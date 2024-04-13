﻿using System;
using System.Collections.Generic;
using System.Windows.Media;
using ProtoBuf;

namespace IRExplorerUI;

[ProtoContract(SkipConstructor = true)]
public class FunctionMarkingSettings : SettingsBase {
  private ColorPalette modulesPalette_;
  private FunctionMarkingSet builtinMarking_;
  
  [ProtoMember(1)]
  public string Title { get; set; }
  [ProtoMember(2)]
  public bool UseAutoModuleColors { get; set; }
  [ProtoMember(3)]
  public bool UseModuleColors { get; set; }
  [ProtoMember(4)]
  public bool UseFunctionColors { get; set; }
  [ProtoMember(5)]
  public string ModulesColorPalette { get; set; }
  [ProtoMember(6)]
  public FunctionMarkingSet CurrentSet { get; set; }
  [ProtoMember(7)]
  public List<FunctionMarkingSet> SavedSets { get; set; }

  //? TODO: Rename Color to Markinng or MarkedModules
  public List<FunctionMarkingStyle> ModuleColors => CurrentSet?.ModuleColors;
  public List<FunctionMarkingStyle> FunctionColors => CurrentSet?.FunctionColors;
  
  public FunctionMarkingSet BuiltinMarkingCategories {
    get {
      if (builtinMarking_ != null) {
        return builtinMarking_;
      }

      var markingsFile = App.GetFunctionMarkingsFilePath(App.Session.CompilerInfo.CompilerIRName);

      if (!JsonUtils.DeserializeFromFile<FunctionMarkingSet>(markingsFile, out builtinMarking_)) {
        builtinMarking_ = new FunctionMarkingSet();
      }
      
      return builtinMarking_;
    }
  }

  public void Save() {
    var markingsFile = @"C:\work\markings.json";
    JsonUtils.SerializeToFile(CurrentSet, markingsFile);
  }

  public FunctionMarkingSettings() {
    Reset();
  }

  public override void Reset() {
    InitializeReferenceMembers();
    UseModuleColors = false;
    UseFunctionColors = false;
    ModulesColorPalette = ColorPalette.LightPastels.Name;
    CurrentSet = new FunctionMarkingSet();
    SavedSets.Clear();
    modulesPalette_ = null;
  }

  [ProtoAfterDeserialization]
  private void InitializeReferenceMembers() {
    CurrentSet ??= new FunctionMarkingSet();
    SavedSets ??= new List<FunctionMarkingSet>();
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
    Save();
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
    if(markingColors.Find(item => 
          item.Name.Length > 0 &&
          item.Name.Equals(style.Name, StringComparison.Ordinal)) != null) {
      return;
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
      int hash = name.GetStableHashCode();
      return modulesPalette_.PickBrush(hash % modulesPalette_.Count);
    }

    return Brushes.Transparent;
  }

  public bool GetModuleColor(string name, out Color color) {
    return GetMarkingColor(name, ModuleColors, out color);
  }

  public bool GetModuleBrush(string name, out Brush brush) {
    if (GetMarkingColor(name, ModuleColors, out var color)) {
      brush = ColorBrushes.GetBrush(color);
      return true;
    }

    brush = Brushes.Transparent;
    return false;
  }

  public Brush GetMarkedNodeBrush(string funcName, string moduleName) {
    if (UseModuleColors &&
        GetFunctionColor(funcName, out var color)) {
      return ColorBrushes.GetBrush(color);
    }
    else if (UseFunctionColors &&
             GetModuleColor(moduleName, out var color2)) {
      return ColorBrushes.GetBrush(color2);
    }

    return null;
  }

  public void ResedCachedPalettes() {
    modulesPalette_ = null;
  }
  
  public override bool Equals(object obj) {
    return obj is FunctionMarkingSettings settings &&
           Title == settings.Title &&
           ModulesColorPalette == settings.ModulesColorPalette &&
           UseAutoModuleColors == settings.UseAutoModuleColors &&
           UseModuleColors == settings.UseModuleColors &&
           ModuleColors.AreEqual(settings.ModuleColors) &&
           UseFunctionColors == settings.UseFunctionColors &&
           FunctionColors.AreEqual(settings.FunctionColors) &&
           SavedSets.AreEqual(settings.SavedSets);
  }
  
  public override string ToString() {
    return $"Title: {Title}\n" +
           $"UseAutoModuleColors: {UseAutoModuleColors}\n" +
           $"UseModuleColors: {UseModuleColors}\n" +
           $"ModuleColors: {ModuleColors}\n" +
           $"UseFunctionColors: {UseFunctionColors}\n" +
           $"FunctionColors: {FunctionColors}";
  }
}

[ProtoContract(SkipConstructor = true)]
public class FunctionMarkingSet {
  [ProtoMember(1)]
  public List<FunctionMarkingStyle> ModuleColors { get; set; }
  [ProtoMember(2)]
  public List<FunctionMarkingStyle> FunctionColors { get; set; }
  [ProtoMember(3)]
  public string Title { get; set; }

  public FunctionMarkingSet(string title = null) {
    Title = title;
    InitializeReferenceMembers();
  }
  
  [ProtoAfterDeserialization]
  private void InitializeReferenceMembers() {
    ModuleColors ??= new List<FunctionMarkingStyle>();
    FunctionColors ??= new List<FunctionMarkingStyle>();
  }

  public override bool Equals(object obj) {
    return obj is FunctionMarkingSet other &&
           ModuleColors.AreEqual(other.ModuleColors) &&
           FunctionColors.AreEqual(other.FunctionColors);
  }
}

[ProtoContract(SkipConstructor = true)]
public class FunctionMarkingStyle {
  private TextSearcher searcher_;
  private string name_;

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
  
  public FunctionMarkingStyle(string name, Color color, 
                              string title = null, bool isRegex = false) {
    Name = name;
    Color = color;
    Title = title;
    IsRegex = isRegex;
    IsEnabled = true;
  }

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
    return new FunctionMarkingStyle(Name, newColor, Title, IsRegex);
  }
  
  public FunctionMarkingStyle Clone() {
    return new FunctionMarkingStyle(Name, Color, Title, IsRegex);
  }
  
  protected bool Equals(FunctionMarkingStyle other) {
    return Name == other.Name &&
           IsEnabled == other.IsEnabled &&
           Title == other.Title &&
           IsRegex == other.IsRegex &&
           Color.Equals(other.Color);
  }

  public override bool Equals(object obj) {
    if (ReferenceEquals(null, obj))
      return false;
    if (ReferenceEquals(this, obj))
      return true;
    if (obj.GetType() != this.GetType())
      return false;
    return Equals((FunctionMarkingStyle)obj);
  }
}
