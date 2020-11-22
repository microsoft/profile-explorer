using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using IRExplorerCore;
using IRExplorerCore.Analysis;
using IRExplorerCore.Graph;

namespace IRExplorerUI.Utilities {
    class CallGraphUtils {
        public static Graph BuildCallGraphLayout(IRTextSummary summary, IRTextSection section,
                                                 LoadedDocument loadedDocument,
                                                 ICompilerInfoProvider compilerInfo,
                                                 bool buildPartialGraph) {
            var cg = GenerateCallGraph(summary, section, loadedDocument,
                                       compilerInfo, buildPartialGraph);

            var options = new CallGraphPrinterOptions() {
                //UseSingleIncomingEdge = true,
                //UseStraightLines = true,
                UseExternalNode = true
            };

            var printer = new CallGraphPrinter(cg, options);
            var result = printer.PrintGraph();
            var graphText = printer.CreateGraph(result, new CancelableTask());

            var graphReader = new GraphvizReader(GraphKind.CallGraph, graphText, printer.CreateNodeDataMap());
            var layoutGraph = graphReader.ReadGraph();
            layoutGraph.GraphOptions = options;
            return layoutGraph;
        }

        private static CallGraph GenerateCallGraph(IRTextSummary summary, IRTextSection section,
                                                   LoadedDocument loadedDocument,
                                                   ICompilerInfoProvider compilerInfo,
                                                   bool buildPartialGraph) {
            var cg = new CallGraph(summary, loadedDocument.Loader, compilerInfo.IR);
            cg.CallGraphNodeCreated += Cg_CallGraphNodeCreated;

            if (buildPartialGraph) {
                cg.Execute(section.ParentFunction, section);
            }
            else {
                cg.Execute(section);
            }

            return cg;
        }

        private static void Cg_CallGraphNodeCreated(object sender, CallGraphEventArgs e) {
            //? TODO: Should be customizable through a script
            int instrs = e.Function.InstructionCount;

            if (instrs == 0) {
                e.FunctionNode.AddTag(GraphNodeTag.MakeHeatMap2(0, 10));
            }
            else if (instrs <= 2) {
                e.FunctionNode.AddTag(GraphNodeTag.MakeHeatMap2(2, 10));
            }
            else if (instrs <= 5) {
                e.FunctionNode.AddTag(GraphNodeTag.MakeHeatMap2(4, 10));
            }
            else if (instrs <= 10) {
                e.FunctionNode.AddTag(GraphNodeTag.MakeHeatMap2(5, 10));
            }
            else if (instrs <= 20) {
                e.FunctionNode.AddTag(GraphNodeTag.MakeHeatMap2(6, 10));
            }
            else {
                e.FunctionNode.AddTag(GraphNodeTag.MakeHeatMap2(8, 10));
            }

            e.FunctionNode.GetTag<GraphNodeTag>().Label = $"{instrs} instrs";
        }
    }
}
