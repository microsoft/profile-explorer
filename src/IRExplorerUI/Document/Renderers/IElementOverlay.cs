// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using IRExplorerCore.IR;
using ProtoBuf;

namespace IRExplorerUI.Document {
    [ProtoContract]
    [ProtoInclude(100, typeof(ElementOverlayBase))]
    public interface IElementOverlay {
        public IRElement Element { get; set; }
        public HorizontalAlignment AlignmentX { get; }
        public VerticalAlignment AlignmentY { get; }
        public double MarginX { get; }
        public double MarginY { get; }
        public double Padding { get; }
        Size Size { get; }
        public bool IsMouseOver { get; set; }
        public bool IsSelected { get; set; }
        public void Draw(Rect elementRect, IRElement element, DrawingContext drawingContext);

        public bool CheckIsMouseOver(Point point);
        public bool MouseClicked(MouseEventArgs e);
        public bool KeyPressed(KeyEventArgs e);

        // event EventHandler OnHover;
        public event MouseEventHandler OnClick;
        public event KeyEventHandler OnKeyPress;
    }
}
