// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using ProfileExplorer.Core;
using ProfileExplorer.Core.Binary;
using ProfileExplorer.Core.Providers;

namespace ProfileExplorer.UI.Compilers.Default;

public enum FilteredSectionNameKind {
  TrimPrefix,
  TrimSuffix,
  TrimWhitespace,
  RemoveSubstring,
  ReplaceSubstring
}

public class FilteredSectionName {
  public FilteredSectionName(string text, FilteredSectionNameKind filterKind) {
    Text = text;
    FilterKind = filterKind;
  }

  public string Text { get; set; }
  public string ReplacementText { get; set; }
  public FilteredSectionNameKind FilterKind { get; set; }
}

public sealed class DefaultNameProvider : INameProvider {
  private static List<FilteredSectionName> sectionNameFilters_;
  private static ConcurrentDictionary<string, string> demangledNameMap_;
  private static ConcurrentDictionary<string, string> functionNameMap_;

  static DefaultNameProvider() {
    demangledNameMap_ = new ConcurrentDictionary<string, string>();
    functionNameMap_ = new ConcurrentDictionary<string, string>();
    sectionNameFilters_ = new List<FilteredSectionName>();
    sectionNameFilters_.Add(new FilteredSectionName("* ", FilteredSectionNameKind.TrimPrefix));
    sectionNameFilters_.Add(new FilteredSectionName(" *", FilteredSectionNameKind.TrimSuffix));
    sectionNameFilters_.Add(new FilteredSectionName("", FilteredSectionNameKind.TrimWhitespace));

    sectionNameFilters_.Add(
      new FilteredSectionName("pass", FilteredSectionNameKind.RemoveSubstring));
  }

  public bool IsDemanglingSupported => true;
  public bool IsDemanglingEnabled => IsDemanglingSupported && App.Settings.SectionSettings.ShowDemangledNames;
  public FunctionNameDemanglingOptions GlobalDemanglingOptions => App.Settings.SectionSettings.DemanglingOptions;

  public string GetSectionName(IRTextSection section, bool includeNumber) {
    string sectionName = section.Name;

    if (string.IsNullOrEmpty(sectionName)) {
      string funcName = section.ParentFunction.Name;

      if (!string.IsNullOrEmpty(funcName)) {
        return funcName.Length <= 24 ? funcName : $"{funcName.Substring(0, 24)}...";
      }

      return "<UNTITLED>";
    }

    foreach (var nameFilter in sectionNameFilters_) {
      if (string.IsNullOrEmpty(nameFilter.Text) &&
          nameFilter.FilterKind != FilteredSectionNameKind.TrimWhitespace) {
        continue;
      }

      switch (nameFilter.FilterKind) {
        case FilteredSectionNameKind.TrimPrefix: {
          if (sectionName.StartsWith(nameFilter.Text, StringComparison.Ordinal)) {
            sectionName = sectionName.Substring(nameFilter.Text.Length);
          }

          break;
        }
        case FilteredSectionNameKind.TrimSuffix: {
          if (sectionName.EndsWith(nameFilter.Text, StringComparison.Ordinal)) {
            sectionName = sectionName.Substring(0, sectionName.Length - nameFilter.Text.Length - 1);
          }

          break;
        }
        case FilteredSectionNameKind.TrimWhitespace: {
          sectionName = sectionName.Trim();
          break;
        }
        case FilteredSectionNameKind.RemoveSubstring: {
          if (sectionName.Contains(nameFilter.Text, StringComparison.Ordinal)) {
            sectionName = sectionName.Replace(nameFilter.Text, "", StringComparison.Ordinal);
          }

          break;
        }
        case FilteredSectionNameKind.ReplaceSubstring: {
          if (sectionName.Contains(nameFilter.Text, StringComparison.Ordinal)) {
            sectionName = sectionName.Replace(nameFilter.Text, nameFilter.ReplacementText,
                                              StringComparison.Ordinal);
          }

          break;
        }
        default:
          throw new ArgumentOutOfRangeException();
      }
    }

    if (includeNumber) {
      return $"({section.Number}) {sectionName}";
    }

    return sectionName;
  }

  public string GetFunctionName(IRTextFunction function) {
    return function.Name;
  }

  public string DemangleFunctionName(string name, FunctionNameDemanglingOptions options) {
    if (!demangledNameMap_.TryGetValue(name, out string demangledName)) {
      demangledName = PDBDebugInfoProvider.DemangleFunctionName(name, options);
      demangledNameMap_.TryAdd(name, demangledName);
    }

    return demangledName;
  }

  public string DemangleFunctionName(IRTextFunction function, FunctionNameDemanglingOptions options) {
    return DemangleFunctionName(function.Name, options);
  }

  public string FormatFunctionName(string name) {
    if (!IsDemanglingEnabled) {
      return name;
    }

    if (!functionNameMap_.TryGetValue(name, out string demangledName)) {
      demangledName = PDBDebugInfoProvider.DemangleFunctionName(name, FunctionNameDemanglingOptions.OnlyName);
      functionNameMap_.TryAdd(name, demangledName);
    }

    return demangledName;
  }

  public string FormatFunctionName(IRTextFunction function) {
    return FormatFunctionName(function.Name);
  }

  public void SettingsChanged() {
    demangledNameMap_.Clear();
    functionNameMap_.Clear();
  }
}