// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Windows;
using System.Windows.Controls;
using IRExplorerCore.IR;

namespace IRExplorer {
    /// <summary>
    ///     Interaction logic for IRPreview.xaml
    /// </summary>
    public partial class IRPreview : UserControl {
        public static readonly DependencyProperty ListElementProperty = DependencyProperty.Register(
            "ListElement", typeof(Tuple<IRDocument, IRElement, string>), typeof(IRPreview),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender,
                                          OnElementChanged));

        private string headerText_;

        private bool isDocumentOnly_;

        public IRPreview() {
            InitializeComponent();
        }

        public Tuple<IRDocument, IRElement, string> ListElement {
            get => (Tuple<IRDocument, IRElement, string>)GetValue(ListElementProperty);
            set {
                SetValue(ListElementProperty, value);

                if (value != null) {
                    TextView.ShowLineNumbers = false;
                    TextView.Background = null;
                    IsDocumentOnly = true;
                    ResizeForLines(1);
                    InitializeFromDocument(value.Item1);
                    PreviewedElement = value.Item2;
                    UpdateView();
                }
            }
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

        public static void OnElementChanged(DependencyObject sender,
                                            DependencyPropertyChangedEventArgs eventArgs) {
            var control = (IRPreview)sender;
            control.ListElement = (Tuple<IRDocument, IRElement, string>)eventArgs.NewValue;
        }

        public void InitializeFromDocument(IRDocument document) {
            TextView.InitializeFromDocument(document);
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
    }
}
