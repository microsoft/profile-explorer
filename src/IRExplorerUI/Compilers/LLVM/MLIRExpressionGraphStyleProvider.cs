using System.Windows.Media;
using IRExplorerCore.Graph;

namespace IRExplorerUI.Compilers.MLIR;

public class MLIRExpressionGraphStyleProvider : ExpressionGraphStyleProvider {
    private const double DefaultEdgeThickness = 0.025;
    private const double BoldEdgeThickness = 0.04;
    private HighlightingStyle semaphoreNodeStyle_;
    private Pen semaphoreEdgeStyle_;
    private Pen defaultEdgeStyle_;

    public MLIRExpressionGraphStyleProvider(Graph graph, ExpressionGraphSettings options,
        ICompilerInfoProvider compilerInfo) : base(graph, options, compilerInfo) {
        semaphoreEdgeStyle_ = ColorPens.GetPen(Colors.MediumBlue, DefaultEdgeThickness);
        defaultEdgeStyle_ = ColorPens.GetPen(Colors.Black, BoldEdgeThickness);
        semaphoreNodeStyle_ = new HighlightingStyle(Colors.LightSteelBlue, ColorPens.GetPen(Colors.Black, DefaultEdgeThickness));
    }

    public override HighlightingStyle GetNodeStyle(Node node) {
        if (node.Label.Contains("Semaphore")) {
            return semaphoreNodeStyle_;
        }

        return base.GetNodeStyle(node);
    }

    public HighlightingStyle GetDefaultEdgeLabelStyle(Edge edge) {
        return base.GetEdgeLabelStyle(edge);
    }

    public override Pen GetEdgeStyle(GraphEdgeKind kind) {
        if (kind == GraphEdgeKind.ImmediateDominator) {
            return semaphoreEdgeStyle_;
        }

        return defaultEdgeStyle_;
    }

    public override GraphEdgeKind GetEdgeKind(Edge edge) {
        //? TODO: Find a better way to pass the info
        if (edge.Style == Edge.EdgeStyle.Dashed) {
            return GraphEdgeKind.Loop;
        }
        else if (edge.Style == Edge.EdgeStyle.Dotted) {
            return GraphEdgeKind.ImmediateDominator;
        }

        return base.GetEdgeKind(edge);
    }
}