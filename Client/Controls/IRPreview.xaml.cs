// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Windows;
using System.Windows.Controls;
using Core.IR;

namespace Client {
    /// <summary>
    /// Interaction logic for IRPreview.xaml
    /// </summary>
    public partial class IRPreview : UserControl {
        public IRPreview() {
            InitializeComponent();
        }

        public Tuple<IRDocument, IRElement, string> ListElement {
            get { return (Tuple<IRDocument, IRElement, string>)this.GetValue(ListElementProperty); }
            set {
                this.SetValue(ListElementProperty, value);
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

        public static readonly DependencyProperty ListElementProperty = DependencyProperty.Register(
          "ListElement", typeof(Tuple<IRDocument, IRElement, string>), typeof(IRPreview),
          new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnElementChanged));


        public static void OnElementChanged(DependencyObject sender, DependencyPropertyChangedEventArgs eventArgs) {
            var control = (IRPreview)sender;
            control.ListElement = (Tuple<IRDocument, IRElement, string>)eventArgs.NewValue;
        }

        public IRElement PreviewedElement { get; set; }

        private bool isDocumentOnly_;
        public bool IsDocumentOnly {
            get { return isDocumentOnly_; }
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

        private string headerText_;
        public string HeaderText {
            get { return headerText_; }
            set {
                if (headerText_ != value) {
                    headerText_ = value;
                    HeaderLabel.Text = headerText_;
                }
            }
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
            var height = lineCount * view.DefaultLineHeight;
            var hostHeight = height;

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
