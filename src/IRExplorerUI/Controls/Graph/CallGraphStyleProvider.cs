using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using IRExplorerCore.GraphViz;

namespace IRExplorerUI {
    public class CallGraphStyleProvider : IGraphStyleProvider {
        private const double DefaultEdgeThickness = 0.025;
        private const double BoldEdgeThickness = 0.05;

        private HighlightingStyle branchBlockStyle_;
        private Pen branchEdgeStyle_;
        private Brush defaultNodeBackground_;
        private HighlightingStyle defaultNodeStyle_;
        private Brush defaultTextColor_;
        private Pen edgeStyle_;

        public CallGraphStyleProvider() {
            defaultTextColor_ = ColorBrushes.GetBrush(Colors.Black);
            defaultNodeBackground_ = ColorBrushes.GetBrush(Colors.Gainsboro);
            defaultNodeStyle_ = new HighlightingStyle(defaultNodeBackground_,
                                                      Pens.GetPen(Colors.Gray,
                                                                  DefaultEdgeThickness));
            edgeStyle_ = Pens.GetPen(Colors.DarkBlue, DefaultEdgeThickness);
        }

        public Brush GetDefaultNodeBackground() {
            return defaultNodeBackground_;
        }

        public HighlightingStyle GetDefaultNodeStyle() {
            return defaultNodeStyle_;
        }

        public Brush GetDefaultTextColor() {
            return defaultTextColor_;
        }

        public GraphEdgeKind GetEdgeKind(Edge edge) {
            return GraphEdgeKind.Default;
        }

        public Pen GetEdgeStyle(GraphEdgeKind kind) {
            return edgeStyle_;
        }

        public HighlightingStyle GetNodeStyle(Node node) {
            return defaultNodeStyle_;
        }

        public bool ShouldRenderEdges(GraphEdgeKind kind) {
            return true;
        }
    }
}
