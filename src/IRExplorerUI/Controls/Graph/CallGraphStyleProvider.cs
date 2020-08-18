using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using IRExplorerCore.Analysis;
using IRExplorerCore.GraphViz;

namespace IRExplorerUI {
    public class CallGraphStyleProvider : IGraphStyleProvider {
        private const double DefaultEdgeThickness = 0.025;
        private const double BoldEdgeThickness = 0.05;

        private Pen branchEdgeStyle_;
        private Brush defaultNodeBackground_;
        private HighlightingStyle defaultNodeStyle_;
        private HighlightingStyle leafNodeStyle_;
        private Brush defaultTextColor_;
        private Pen edgeStyle_;

        public CallGraphStyleProvider() {
            defaultTextColor_ = ColorBrushes.GetBrush(Colors.Black);
            defaultNodeBackground_ = ColorBrushes.GetBrush(Colors.Gainsboro);
            defaultNodeStyle_ = new HighlightingStyle(defaultNodeBackground_,
                                                      Pens.GetPen(Colors.Gray, DefaultEdgeThickness));
            leafNodeStyle_ = new HighlightingStyle(ColorBrushes.GetBrush(Colors.Lavender),
                                                      Pens.GetPen(Colors.Gray, DefaultEdgeThickness));
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
            var callNode = (CallGraphNode)node.Data;
            if(!callNode.HasCallees) {
                return leafNodeStyle_;
            }
            return defaultNodeStyle_;
        }

        public bool ShouldRenderEdges(GraphEdgeKind kind) {
            return true;
        }
    }
}
