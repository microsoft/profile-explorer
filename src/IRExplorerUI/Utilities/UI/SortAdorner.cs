// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace IRExplorerUI {
    class SortAdorner : Adorner {
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