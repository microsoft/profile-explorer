using System.Windows.Media;
using IRExplorerCore.Graph;
using IRExplorerCore.IR;

namespace IRExplorerUI.Compilers.MLIR;

public class MLIRExpressionGraphStyleProvider : ExpressionGraphStyleProvider {
    private const double DefaultEdgeThickness = 0.025;
    private const double BoldEdgeThickness = 0.04;
    private const double BoundingBoxEdgeThickness = 0.015;

    private HighlightingStyle semaphoreNodeStyle_;
    private Pen semaphoreEdgeStyle_;
    private Pen defaultEdgeStyle_;
    private Pen boundingBoxPen_;
    private Brush boundingBoxColor_;
    private HighlightingStyle boundingBoxStyle_;
    private Brush boundingBoxLabelColor_;

    public MLIRExpressionGraphStyleProvider(Graph graph, ExpressionGraphSettings options,
        ICompilerInfoProvider compilerInfo) : base(graph, options, compilerInfo) {
        semaphoreEdgeStyle_ = ColorPens.GetPen(Colors.MediumBlue, DefaultEdgeThickness);
        defaultEdgeStyle_ = ColorPens.GetPen(Colors.Black, BoldEdgeThickness);
        semaphoreNodeStyle_ = new HighlightingStyle(Colors.LightSteelBlue, ColorPens.GetPen(Colors.Black, DefaultEdgeThickness));
        boundingBoxPen_ = ColorPens.GetPen(Colors.Gray, BoundingBoxEdgeThickness);
        boundingBoxColor_ = ColorBrushes.GetTransparentBrush(Colors.LightGray, 10);
        boundingBoxStyle_ = new HighlightingStyle(boundingBoxColor_, boundingBoxPen_);
        boundingBoxLabelColor_ = Brushes.DarkBlue;
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

    public override HighlightingStyle GetBoundingBoxStyle(Node node) {
        Brush fillColor = null;

        if (node.Data is RegionIR region) {
            if (region.Owner is InstructionIR instr) {
                //? TODO: Read from JSON file
                var label = instr.OpcodeText.ToString();

                if (label.Contains("scf")) {
                    fillColor = ColorBrushes.GetTransparentBrush(Colors.SkyBlue, 20);
                }
                else if (label.Contains("linalg")) {
                    fillColor = ColorBrushes.GetTransparentBrush(Colors.Orchid, 20);
                }
                else if (label.Contains("affine")) {
                    fillColor = ColorBrushes.GetTransparentBrush(Colors.PaleGreen, 20);
                }
            }
        }

        return fillColor != null ? new HighlightingStyle(fillColor, boundingBoxPen_) : boundingBoxStyle_;
    }


    public override HighlightingStyle GetBoundingBoxLabelStyle(Node node) {
        if (node.ElementData is BlockIR) {
            return HighlightingStyle.Empty;
        }

        return GetBoundingBoxStyle(node);
    }

    public override Brush GetBoundingBoxLabelColor(Node node) {
        return boundingBoxLabelColor_;
    }
}