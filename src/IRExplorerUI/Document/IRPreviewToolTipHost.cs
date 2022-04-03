// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Windows;
using System.Windows.Controls;
using IRExplorerCore.IR;

namespace IRExplorerUI {
    public class IRPreviewToolTipHost : ToolTip {
        private IRDocument document_;
        private IRElement element_;
        private string text_;
        private IRPreviewToolTip previewer_;

        public IRPreviewToolTipHost(double width, double height, IRDocument document, IRElement element,
                                string text = null, string style = "IRPreviewTooltip") {
            document_ = document;
            element_ = element ?? throw new Exception();
            text_ = text;
            Style = Application.Current.FindResource(style) as Style;
            Width = width;
            Height = height;
            Loaded += IRPreviewToolTipHost_Loaded;
        }

        public IRElement Element => element_;

        public void Show() {
            IsOpen = true;
        }

        public void Hide() {
            IsOpen = false;
        }

        public void AdjustVerticalPosition(double amount) {
            previewer_?.AdjustVerticalPosition(amount);
            StaysOpen = true;
        }

        private void IRPreviewToolTipHost_Loaded(object sender, RoutedEventArgs e) {
            previewer_ = Utils.FindChild<IRPreviewToolTip>(this, "IRPreviewer");
            previewer_.InitializeFromDocument(document_, text_);
            previewer_.PreviewedElement = element_;
            previewer_.HeaderText = $"Block {Utils.MakeBlockDescription(element_.ParentBlock)}";

            Height = previewer_.ResizeForLines(7);
            previewer_.UpdateView();
        }
    }
}