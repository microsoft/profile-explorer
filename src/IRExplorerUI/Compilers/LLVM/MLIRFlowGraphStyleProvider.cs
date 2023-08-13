using System.Windows.Media;
using IRExplorerCore.Graph;
using IRExplorerCore.IR;

namespace IRExplorerUI.Compilers.MLIR;

public class MLIRFlowGraphStyleProvider : FlowGraphStyleProvider {
    public MLIRFlowGraphStyleProvider(Graph graph, FlowGraphSettings options) : base(graph, options)
    {
    }

    public override HighlightingStyle GetBoundingBoxStyle(Node node) {
        var border = ColorPens.GetDashedPen(Colors.Gray, DashStyles.Dot, DefaultEdgeThickness);
        var fillColor = ColorBrushes.GetTransparentBrush(Colors.LightGray, 10);

        if (node.Data is RegionIR region) {
            if (region.Owner is InstructionIR instr) {
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

        return new HighlightingStyle(fillColor, border);
    }
}