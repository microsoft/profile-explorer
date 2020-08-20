using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using IRExplorerCore.Analysis;
using IRExplorerCore.Graph;

namespace IRExplorerUI {
    public class CallGraphStyleProvider : IGraphStyleProvider {
        private const double DefaultEdgeThickness = 0.025;
        private const double BoldEdgeThickness = 0.05;

        private Brush defaultNodeBackground_;
        private HighlightingStyle defaultNodeStyle_;
        private HighlightingStyle leafNodeStyle_;
        private HighlightingStyle entryNodeStyle_;
        private HighlightingStyle externalNodeStyle_;
        private Brush defaultTextColor_;
        private Pen edgeStyle_;

        public CallGraphStyleProvider() {
            defaultTextColor_ = ColorBrushes.GetBrush(Colors.Black);
            defaultNodeBackground_ = ColorBrushes.GetBrush(Colors.Gainsboro);
            defaultNodeStyle_ = new HighlightingStyle(defaultNodeBackground_,
                                                      Pens.GetPen(Colors.DimGray, DefaultEdgeThickness));
            leafNodeStyle_ = new HighlightingStyle(ColorBrushes.GetBrush(Colors.LightBlue),
                                                      Pens.GetPen(Colors.DimGray, DefaultEdgeThickness));
            entryNodeStyle_ = new HighlightingStyle(ColorBrushes.GetBrush(Colors.LightGreen),
                                                      Pens.GetPen(Colors.DimGray, BoldEdgeThickness));
            externalNodeStyle_ = new HighlightingStyle(ColorBrushes.GetBrush(Colors.Moccasin),
                                                      Pens.GetPen(Colors.DimGray, DefaultEdgeThickness));
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

            if (callNode == null) {
                return defaultNodeStyle_;
            }

            // Check for a tag that overrides the style.
            var graphTag = callNode.GetTag<GraphNodeTag>();

            if (graphTag != null) {
                var background = graphTag.BackgroundColor ?? Colors.Gainsboro;
                var borderColor = graphTag.BorderColor ?? Colors.DimGray;
                var borderThickness = graphTag.BorderThickness != 0 ? graphTag.BorderThickness : DefaultEdgeThickness;
                return new HighlightingStyle(background, Pens.GetPen(borderColor, borderThickness));
            }

            if (callNode.IsExternal) {
                return externalNodeStyle_;
            }
            else if (!callNode.HasCallers) {
                return entryNodeStyle_;
            }
            else if (!callNode.HasCallees) {
                return leafNodeStyle_;
            }

            return defaultNodeStyle_;
        }

        public bool ShouldRenderEdges(GraphEdgeKind kind) {
            return true;
        }

        public bool ShouldUsePolylines() {
            return false;
        }
    }
}
