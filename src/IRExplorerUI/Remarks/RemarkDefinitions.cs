// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;

namespace IRExplorerUI {
    [Flags]
    public enum RemarkKind {
        None,
        Default,
        Verbose,
        Trace,
        Warning,
        Error,
        Optimization,
        Analysis
    }
    
    //? TODO: Sorting categories hack
    // https://stackoverflow.com/questions/823327/how-can-i-customize-category-sorting-on-a-propertygrid
    // public class CustomSortedCategoryAttribute : CategoryAttribute
    // {
    //     private const char NonPrintableChar = '\t';
    //
    //     public CustomSortedCategoryAttribute(   string category,
    //         ushort categoryPos,
    //         ushort totalCategories)
    //         : base(category.PadLeft(category.Length + (totalCategories - categoryPos),
    //             CustomSortedCategoryAttribute.NonPrintableChar))
    //     {
    //     }
    // }
    
    public class RemarkCategory {
        public RemarkKind Kind { get; set; }
        public string Title { get; set; } // VN,CE/BE/ALIAS, etc
        public bool HasTitle => !string.IsNullOrEmpty(Title);
        public bool ExpectInstructionIR { get; set; }
        
        [Category("Text")]
        [DisplayName("Searched Text"), Display(Order=1, Description = "TODO ADD DESCRIPTION")]
        public string SearchedText { get; set; }
        
        [Category("Text")]
        [DisplayName("Search Kind"), Display(Order = 2, Description = "TODO ADD DESCRIPTION")]
        public TextSearchKind SearchKind { get; set; }
        
        [Category("Style")]
        public bool AddTextMark { get; set; }
        
        [Category("Style")]
        public Color TextMarkBorderColor { get; set; }
        
        [Category("Style")]
        public double TextMarkBorderWeight { get; set; }
        
        [Category("Style")]
        public bool AddLeftMarginMark { get; set; }
        
        [Category("Style")]
        public Color MarkColor { get; set; }
        
        [Category("Style")]
        public int MarkIconIndex { get; set; }
        
        public override string ToString() {
            var title = !string.IsNullOrEmpty(Title) ? Title : "<untitled>";
            return $"{title}: {SearchedText}";
        }
    }
    
    public class RemarkSectionBoundary {
        public string SearchedText { get; set; }
        public TextSearchKind SearchKind { get; set; }
        
        public override string ToString() {
            return SearchedText;
        }
    }

    public class RemarkTextHighlighting {
        public string SearchedText { get; set; }
        public TextSearchKind SearchKind { get; set; }
        public bool UseBoldText { get; set; }
        public bool UseItalicText { get; set; }
        public Color TextColor { get; set; }
        public Color BackgroundColor { get; set; }
        public bool HasTextColor => TextColor != Colors.Black;
        public bool HasBackgroundColor => BackgroundColor != Colors.Black;
        
        public override string ToString() {
            return !string.IsNullOrEmpty(SearchedText) ? SearchedText : "<untitled>";
        }
    }
}