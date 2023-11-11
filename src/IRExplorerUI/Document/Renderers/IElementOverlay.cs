﻿// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

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
        Rect Bounds { get; }
        public bool IsMouseOver { get; set; }
        public bool IsSelected { get; set; }
        public bool ShowOnMarkerBar { get; set; }
        public bool SaveStateToFile {  get; set; }

        public void Draw(Rect elementRect, IRElement element,
                         IElementOverlay previousOverlay, DrawingContext drawingContext);

        public bool CheckIsMouseOver(Point point);
        public bool MouseClicked(MouseEventArgs e);
        public bool KeyPressed(KeyEventArgs e);

        // event EventHandler OnHover;
        public event MouseEventHandler OnClick;
        public event KeyEventHandler OnKeyPress;
    }
}
