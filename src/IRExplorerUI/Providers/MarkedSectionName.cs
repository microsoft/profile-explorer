// unset

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

        [DisplayName("Searched Text"), Display(Order=1, Description = "TODO ADD DESCRIPTION")]
        public string SearchedText { get; set; }
        [DisplayName("Searched Kind"), Display(Order = 2, Description = "TODO ADD DESCRIPTION")]
        public TextSearchKind SearchKind { get; set; }
        [DisplayName("Text Color"), Display(Order = 3)]
        public Color TextColor { get; set; }
        [DisplayName("Separator Color"), Display(Order = 4)]
        public Color SeparatorColor { get; set; }
        [DisplayName("Separator weight before"), Display(Order = 5)]
        public int BeforeSeparatorWeight { get; set; }
        [DisplayName("Separator weight after"), Display(Order = 6)]
        public int AfterSeparatorWeight { get; set; }
        [DisplayName("Indentation level"), Display(Order = 7)]
        public int IndentationLevel { get; set; }

        public override string ToString() {
            if (!string.IsNullOrEmpty(SearchedText)) {
                return SearchedText;
            }

            return "<untitled>";
        }
    }
}