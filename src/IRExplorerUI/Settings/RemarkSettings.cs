// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using ProtoBuf;

namespace IRExplorerUI {
    [ProtoContract(SkipConstructor = true)]
    public class RemarkSettings : SettingsBase {
        public RemarkSettings() {
            Reset();
        }

        public override void Reset() {
            ShowRemarks = true;
            ShowPreviousSections = true;
            StopAtSectionBoundaries = true;
            SectionHistoryDepth = 4;
            ShowPreviousOptimizationRemarks = true;
            ShowPreviousAnalysisRemarks = false;
            ShowActionButtonOnHover = true;
            ShowActionButtonWithModifier = false;
            ShowMarginRemarks = true;
            ShowDocumentRemarks = true;
            UseRemarkBackground = true;
            UseTransparentRemarkBackground = true;
            RemarkBackgroundOpacity = 25;
            Default = true;
            Verbose = true;
            Optimization = true;
            Analysis = true;
            Trace = false;
            ShowRemarks = true;
            CategoryFilter = new Dictionary<string, bool>();
        }

        [ProtoMember(1)]
        public bool ShowRemarks { get; set; }

        [ProtoMember(2)]
        public bool ShowPreviousSections { get; set; }

        [ProtoMember(3)]
        public bool StopAtSectionBoundaries { get; set; }

        [ProtoMember(4)]
        public int SectionHistoryDepth { get; set; }

        [ProtoMember(5)]
        public bool ShowPreviousOptimizationRemarks { get; set; }

        [ProtoMember(6)]
        public bool ShowActionButtonOnHover { get; set; }

        [ProtoMember(7)]
        public bool ShowActionButtonWithModifier { get; set; }

        [ProtoMember(8)]
        public bool ShowMarginRemarks { get; set; }

        [ProtoMember(9)]
        public bool ShowDocumentRemarks { get; set; }

        [ProtoMember(10)]
        public bool UseRemarkBackground { get; set; }

        [ProtoMember(11)]
        public bool UseTransparentRemarkBackground { get; set; }

        [ProtoMember(12)]
        public int RemarkBackgroundOpacity { get; set; }

        [ProtoMember(13)]
        public bool Default { get; set; }

        [ProtoMember(14)]
        public bool Verbose { get; set; }

        [ProtoMember(15)]
        public bool Trace { get; set; }

        [ProtoMember(16)]
        public bool Analysis { get; set; }

        [ProtoMember(17)]
        public bool Optimization { get; set; }

        [ProtoMember(18)]
        public Dictionary<string, bool> CategoryFilter { get; set; }

        [ProtoMember(19)]
        public bool ShowPreviousAnalysisRemarks { get; set; }

        public string SearchedText { get; set; }
        public bool HasCategoryFilters => CategoryFilter != null && CategoryFilter.Count > 0;

        public RemarkSettings Clone() {
            var serialized = StateSerializer.Serialize(this);
            return StateSerializer.Deserialize<RemarkSettings>(serialized);
        }

        public override bool Equals(object obj) {
            return obj is RemarkSettings state &&
                   ShowRemarks == state.ShowRemarks &&
                   ShowPreviousSections == state.ShowPreviousSections &&
                   StopAtSectionBoundaries == state.StopAtSectionBoundaries &&
                   SectionHistoryDepth == state.SectionHistoryDepth &&
                   ShowPreviousOptimizationRemarks == state.ShowPreviousOptimizationRemarks &&
                   ShowPreviousAnalysisRemarks == state.ShowPreviousAnalysisRemarks &&
                   ShowActionButtonOnHover == state.ShowActionButtonOnHover &&
                   ShowActionButtonWithModifier == state.ShowActionButtonWithModifier &&
                   ShowMarginRemarks == state.ShowMarginRemarks &&
                   ShowDocumentRemarks == state.ShowDocumentRemarks &&
                   UseRemarkBackground == state.UseRemarkBackground &&
                   UseTransparentRemarkBackground == state.UseTransparentRemarkBackground &&
                   RemarkBackgroundOpacity == state.RemarkBackgroundOpacity &&
                   Default == state.Default &&
                   Verbose == state.Verbose &&
                   Trace == state.Trace &&
                   Analysis == state.Analysis &&
                   Optimization == state.Optimization &&
                   HasCategoryFilters == state.HasCategoryFilters &&
                   CategoryFilter.AreEqual(state.CategoryFilter);
        }
    }
}
