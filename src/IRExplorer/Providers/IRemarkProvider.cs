// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Windows.Media;
using CoreLib;
using CoreLib.IR;

namespace Client {
    public class OptimizationRemark //: PassRemark
    {
        public string OptimizationName { get; set; }
        public object Info { get; set; }
    }

    public interface IRRemarkProvider {
        // per-section cache of remarks, don't have to re-parse all output
        // when switching sections

        string SettingsFilePath { get; }

        bool SaveSettings();
        bool LoadSettings();
        List<RemarkCategory> LoadRemarkCategories();
        List<RemarkSectionBoundary> LoadRemarkSectionBoundaries();
        public List<Remark> ExtractRemarks(string text, FunctionIR function, IRTextSection section);
        public OptimizationRemark GetOptimizationRemarkInfo(Remark remark);
    }

    class RemarksDefinitionSerializer {
        public bool Save(List<RemarkCategory> categories, List<RemarkSectionBoundary> boundaries, string path) {
            var data = new SerializedData {
                RemarkCategoryList = categories
            };

            return JsonUtils.Serialize(data, path);
        }

        public bool Load(string path, out List<RemarkCategory> categories, out List<RemarkSectionBoundary> boundaries) {
            if (JsonUtils.Deserialize(path, out SerializedData data)) {
                categories = data.RemarkCategoryList;
                boundaries = data.SectionBoundaryList;
                return categories != null && boundaries != null;
            }

            categories = new List<RemarkCategory>();
            boundaries = new List<RemarkSectionBoundary>();
            return false;
        }

        private class SerializedData {
            public string Comment1 => "Add remark definitions to the list found below, including the kind, searched text that should be part of the remark, a type of search to perform (simple/regex,etc) and how to highlight the remark, using an underline and/or marker on the left-side margin with a certain color";
            public string Comment2 => "Remark categories are defined in the RemarkCategoryList, remark section stop boundaries in SectionBoundaryList";
            public string Comment3 => "Use the KindValues/SearchKindValues strings listed below as the enumeration values for the corresponding members";

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
        }
    }
}
