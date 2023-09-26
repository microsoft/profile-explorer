using System.Windows.Media;
using IRExplorerCore.Graph;
using IRExplorerCore.IR;

namespace IRExplorerUI.Compilers.MLIR;

public class MLIRFlowGraphStyleProvider : FlowGraphStyleProvider {
    private const double BoundingBoxEdgeThickness = 0.015;

    private Brush boundingBoxLabelColor_;
    private Pen boundingBoxPen_;
    private Brush boundingBoxColor_;
    private HighlightingStyle boundingBoxStyle_;

    public MLIRFlowGraphStyleProvider(Graph graph, FlowGraphSettings options) : base(graph, options) {
        boundingBoxPen_ = ColorPens.GetPen(Colors.Gray, BoundingBoxEdgeThickness);
        boundingBoxColor_ = ColorBrushes.GetTransparentBrush(Colors.LightGray, 10);
        boundingBoxStyle_ = new HighlightingStyle(boundingBoxColor_, boundingBoxPen_);
        boundingBoxLabelColor_ = Brushes.DarkBlue;
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