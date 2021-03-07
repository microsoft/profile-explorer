// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;

namespace IRExplorerUI {
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

        [Category("Text")]
        [DisplayName("Searched Text"), Display(Order=1, Description = "TODO ADD DESCRIPTION")]
        public string SearchedText { get; set; }

        [Category("Text")]
        [DisplayName("Search Kind"), Display(Order = 2, Description = "TODO ADD DESCRIPTION")]
        public TextSearchKind SearchKind { get; set; }

        [Category("Style")]
        [DisplayName("Text Color"), Display(Order = 3)]
        public Color TextColor { get; set; }

        [Category("Style")]
        [DisplayName("Separator Color"), Display(Order = 4)]
        public Color SeparatorColor { get; set; }
        
        [DisplayName("Separator weight before"), Display(Order = 5)]
        [Category("Style")]
        public int BeforeSeparatorWeight { get; set; }
        
        [DisplayName("Separator weight after"), Display(Order = 6)]
        [Category("Style")]
        public int AfterSeparatorWeight { get; set; }
        
        [DisplayName("Indentation level"), Display(Order = 7)]
        [Category("Style")]
        public int IndentationLevel { get; set; }
        
        public override string ToString() {
            return !string.IsNullOrEmpty(SearchedText) ? SearchedText : "<untitled>";
        }
    }
}