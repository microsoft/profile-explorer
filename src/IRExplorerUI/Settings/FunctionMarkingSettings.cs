using System;
using System.Collections.Generic;
using System.Windows.Media;
using ProtoBuf;

namespace IRExplorerUI;

[ProtoContract(SkipConstructor = true)]
public class FunctionMarkingSettings : SettingsBase {
  private ColorPalette modulesPalette_;

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
  
  public static List<FunctionMarkingStyle> BuiltinFunctionMarkingCategories {
    get {
      var list = new List<FunctionMarkingStyle>();
      return list;
    }
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
  }

  private void AddMarkingColor(string name, Color color, List<FunctionMarkingStyle> markingColors) {
    foreach (var pair in markingColors) {
      if (pair.Name.Length > 0 &&
          pair.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) {
        pair.Color = color;

        return;
      }
    }

    markingColors.Add(new FunctionMarkingStyle(name, color));
  }
  
  public bool GetFunctionColor(string name, out Color color) {
    return GetMarkingColor(name, FunctionColors, out color);
  }

  private bool GetMarkingColor(string name, List<FunctionMarkingStyle> markingColors, out Color color) {
    foreach (var pair in markingColors) {
      if (pair.NameMatches(name)) {
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

  public FunctionMarkingSet() {
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
  
  public FunctionMarkingStyle(string name, Color color) {
    Name = name;
    Color = color;
    IsEnabled = true;
  }

  public bool NameMatches(string candidate) {
    if (!IsEnabled || candidate.Length <= 0 || Name.Length <= 0) {
      return false;
    }

    if (IsRegex) {
      return searcher_.Includes(candidate, Name, TextSearchKind.Regex);
    }
    else {
      return searcher_.Includes(candidate, Name, TextSearchKind.CaseInsensitive);
    }
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
