using System.Windows.Media;
using IRExplorerCore.Graph;

namespace IRExplorerUI.Compilers.LLVM;

public class MLIRExpressionGraphStyleProvider : ExpressionGraphStyleProvider {
    public MLIRExpressionGraphStyleProvider(Graph graph, ExpressionGraphSettings options,
        ICompilerInfoProvider compilerInfo) : base(graph, options, compilerInfo) {

    }

    public override HighlightingStyle GetNodeStyle(Node node) {
        return base.GetNodeStyle(node);
    }

    public override Pen GetEdgeStyle(GraphEdgeKind kind) {
        return base.GetEdgeStyle(kind);
    }

    public override GraphEdgeKind GetEdgeKind(Edge edge) {
        return base.GetEdgeKind(edge);
    }
}