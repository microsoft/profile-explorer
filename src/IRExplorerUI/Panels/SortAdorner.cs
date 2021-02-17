// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.ComponentModel;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace IRExplorerUI {
    public partial class SectionPanel {
        public sealed class SortAdorner : Adorner {
            private static Geometry ascGeometry = Geometry.Parse("M 0 4 L 3.5 0 L 7 4 Z");

            private static Geometry descGeometry = Geometry.Parse("M 0 0 L 3.5 4 L 7 0 Z");

            public SortAdorner(UIElement element, ListSortDirection dir) : base(element) {
                Direction = dir;
            }

            public ListSortDirection Direction { get; private set; }

            protected override void OnRender(DrawingContext drawingContext) {
                base.OnRender(drawingContext);

                if (AdornedElement.RenderSize.Width < 20) {
                    return;
                }

                var transform = new TranslateTransform(AdornedElement.RenderSize.Width - 15,
                    (AdornedElement.RenderSize.Height - 5) / 2);

                drawingContext.PushTransform(transform);
                var geometry = ascGeometry;

                if (Direction == ListSortDirection.Descending) {
                    geometry = descGeometry;
                }

                drawingContext.DrawGeometry(Brushes.Black, null, geometry);
                drawingContext.Pop();
            }
        }
    }
}