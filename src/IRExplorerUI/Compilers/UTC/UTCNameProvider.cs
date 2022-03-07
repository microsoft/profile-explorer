// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using IRExplorerCore;
using IRExplorerUI.Compilers;

namespace IRExplorerUI.UTC {
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

    class UTCNameProvider : INameProvider {
        private static List<FilteredSectionName> sectionNameFilters_;

        static UTCNameProvider() {
            sectionNameFilters_ = new List<FilteredSectionName>();
            sectionNameFilters_.Add(new FilteredSectionName("* ", FilteredSectionNameKind.TrimPrefix));
            sectionNameFilters_.Add(new FilteredSectionName(" *", FilteredSectionNameKind.TrimSuffix));
            sectionNameFilters_.Add(new FilteredSectionName("", FilteredSectionNameKind.TrimWhitespace));

            sectionNameFilters_.Add(
                new FilteredSectionName("tuples after", FilteredSectionNameKind.RemoveSubstring));
        }

        public bool IsDemanglingSupported => true;
        public FunctionNameDemanglingOptions GlobalDemanglingOptions => App.Settings.SectionSettings.DemanglingOptions;

        public string GetSectionName(IRTextSection section, bool includeNumber) {
            string sectionName = section.Name;

            if (string.IsNullOrEmpty(sectionName)) { 
                var funcName = section.ParentFunction.Name;
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

            if(includeNumber) {
                return $"({section.Number}) {sectionName}";
            }

            return sectionName;
        }

        public string GetFunctionName(IRTextFunction function) {
            return function.Name;
        }

        public string DemangleFunctionName(string name, FunctionNameDemanglingOptions options) {
            return PDBDebugInfoProvider.DemangleFunctionName(name, options);
        }

        public string DemangleFunctionName(IRTextFunction function, FunctionNameDemanglingOptions options) {
            return DemangleFunctionName(function.Name, options);
        }
    }
}
