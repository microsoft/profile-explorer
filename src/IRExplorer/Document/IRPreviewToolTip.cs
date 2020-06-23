// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Windows;
using System.Windows.Controls;
using CoreLib.IR;

namespace Client {
    public class IRPreviewToolTip : ToolTip {
        private IRDocument document_;
        private IRElement element_;
        private string headerText_;

        public IRPreviewToolTip(double width, double height, IRDocument document, IRElement element,
                                string style = "IRPreviewTooltip") {
            document_ = document;
            element_ = element ?? throw new Exception();
            Style = Application.Current.FindResource(style) as Style;
            Width = width;
            Height = height;
            Loaded += IRPreviewToolTip_Loaded;
        }

        public IRElement Element => element_;

        public void Show() {
            IsOpen = true;
        }

        public void Hide() {
            IsOpen = false;
        }

        private void IRPreviewToolTip_Loaded(object sender, RoutedEventArgs e) {
            var previewer = Utils.FindChild<IRPreview>(this, "IRPreviewer");
            previewer.InitializeFromDocument(document_);
            previewer.PreviewedElement = element_;

            if (!string.IsNullOrEmpty(headerText_)) {
                previewer.HeaderText = headerText_;
            }
            else {
                previewer.HeaderText = $"Block {Utils.MakeBlockDescription(element_.ParentBlock)}";
            }

            Height = previewer.ResizeForLines(5);
            previewer.UpdateView();
        }
    }
}
