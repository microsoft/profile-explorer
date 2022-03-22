// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Windows;
using System.Windows.Controls;
using IRExplorerCore.IR;
using static System.Diagnostics.Trace;

namespace IRExplorerUI {
    /// <summary>
    ///     Interaction logic for IRPreview.xaml
    /// </summary>
    public partial class IRPreviewToolTip : UserControl {
        private string headerText_;
        private bool isDocumentOnly_;

        public IRPreviewToolTip() {
            InitializeComponent();
        }
        
        public IRElement PreviewedElement { get; set; }

        public bool IsDocumentOnly {
            get => isDocumentOnly_;
            set {
                if (isDocumentOnly_ != value) {
                    isDocumentOnly_ = value;

                    if (isDocumentOnly_) {
                        ViewBorder.BorderThickness = new Thickness(0);
                        ViewHeader.Visibility = Visibility.Collapsed;
                    }
                }
            }
        }

        public string HeaderText {
            get => headerText_;
            set {
                if (headerText_ != value) {
                    headerText_ = value;
                    HeaderLabel.Text = headerText_;
                }
            }
        }
        
        public void InitializeFromDocument(IRDocument document, string text = null) {
            TextView.InitializeFromDocument(document, false, text);
        }

        public void InitializeBasedOnDocument(string text, IRDocument document) {
            TextView.InitializeBasedOnDocument(text, document);
        }

        public void UpdateView(bool highlight = true) {
            if (PreviewedElement != null) {
                if (highlight) {
                    TextView.MarkElementWithDefaultStyle(PreviewedElement);
                }
                else {
                    TextView.BringElementIntoView(PreviewedElement, BringIntoViewStyle.FirstLine);
                }
            }
        }

        public double ResizeForLines(int lineCount) {
            var view = TextView.TextArea.TextView;
            double height = lineCount * view.DefaultLineHeight;
            double hostHeight = height;

            if (!isDocumentOnly_) {
                hostHeight += ViewBorder.BorderThickness.Top +
                              ViewBorder.BorderThickness.Bottom +
                              ViewHeader.ActualHeight;
            }

            TextView.Height = height;
            TextViewHost.Height = hostHeight;
            return hostHeight;
        }

        public void AdjustVerticalPosition(double amount) {
            // Make scroll bar visible, it's not by default.
            TextView.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            ScrollViewer.SetVerticalScrollBarVisibility(TextView, ScrollBarVisibility.Auto);

            amount *= TextView.TextArea.TextView.DefaultLineHeight;
            double newOffset = TextView.VerticalOffset + amount;
            TextView.ScrollToVerticalOffset(newOffset);
        }
    }
}
