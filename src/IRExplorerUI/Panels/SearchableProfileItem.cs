using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace IRExplorerUI;

public class SearchableProfileItem : BindableObject {
    public bool IsMarked { get; set; }
    public TextSearchResult? SearchResult { get; set; }
    public string FunctionName { get; set; }
    public string ModuleName { get; set; }
    public double Percentage { get; set; }
    public double ExclusivePercentage { get; set; }
    public virtual TimeSpan Weight { get; set; }
    public virtual TimeSpan ExclusiveWeight { get; set; }

    private TextBlock name_;
    public TextBlock Name {
        get {
            if (name_ == null) {
                name_ = CreateOnDemandName();
            }
            return name_;
        }
    }

    public virtual void ResetCachedName() {
        SetAndNotify(ref name_, null, "Name");
    }

    protected virtual TextBlock CreateOnDemandName() {
        var textBlock = new TextBlock();

        if (IsMarked) {
            textBlock.FontWeight = FontWeights.Bold;
        }

        if (ShouldPrependModule() && !string.IsNullOrEmpty(ModuleName)) {
            textBlock.Inlines.Add(new Run(ModuleName) {
                Foreground = Brushes.DimGray,
                FontWeight = IsMarked ? FontWeights.DemiBold : FontWeights.Normal
            });

            textBlock.Inlines.Add("!");
            var nameFontWeight = IsMarked ? FontWeights.Bold : FontWeights.SemiBold;

            if (SearchResult.HasValue) {
                CreateSearchResultName(textBlock, nameFontWeight);
            }
            else {
                textBlock.Inlines.Add(new Run(FunctionName) {
                    FontWeight = nameFontWeight
                });
            }
        }
        else {
            var nameFontWeight = IsMarked ? FontWeights.DemiBold : FontWeights.Normal;

            if (SearchResult.HasValue) {
                CreateSearchResultName(textBlock, nameFontWeight);
            }
            else {
                textBlock.Inlines.Add(new Run(FunctionName) {
                    FontWeight = nameFontWeight
                });
            }
        }

        return textBlock;
    }

    protected virtual bool ShouldPrependModule() {
        return true;
    }

    private void CreateSearchResultName(TextBlock textBlock, FontWeight nameFontWeight) {
        if (SearchResult.Value.Offset > 0) {
            textBlock.Inlines.Add(new Run(FunctionName.Substring(0, SearchResult.Value.Offset)) {
                FontWeight = nameFontWeight
            });
        }

        textBlock.Inlines.Add(new Run(FunctionName.Substring(SearchResult.Value.Offset, SearchResult.Value.Length)) {
            Background = Brushes.Khaki
        });

        int remainingLength = FunctionName.Length - (SearchResult.Value.Offset + SearchResult.Value.Length);

        if (remainingLength > 0) {
            textBlock.Inlines.Add(new Run(FunctionName.Substring(FunctionName.Length - remainingLength, remainingLength)) {
                FontWeight = nameFontWeight
            });
        }
    }

}