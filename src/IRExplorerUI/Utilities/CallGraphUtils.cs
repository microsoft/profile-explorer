using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using IRExplorerCore;
using IRExplorerCore.Analysis;
using IRExplorerCore.Graph;
using IRExplorerCore.IR.Tags;
using IRExplorerUI.Profile;

namespace IRExplorerUI.Utilities {
    class CallGraphUtils {
        public static Graph BuildCallGraphLayout(IRTextSummary summary, IRTextSection section,
            LoadedDocument loadedDocument,
            ICompilerInfoProvider compilerInfo,
            ProfileData profileData,
            bool buildPartialGraph) {
            var cg = GenerateCallGraph(summary, section, loadedDocument,
                                       compilerInfo, profileData, buildPartialGraph);

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
                                                   ProfileData profileData,
                                                   bool buildPartialGraph) {
            var cg = new CallGraph(summary, loadedDocument.Loader, compilerInfo.IR);
            cg.CallGraphNodeCreated += (sender, e) => {
                //? TODO: Should be customizable through a script
               
                var funcProfile = profileData?.GetFunctionProfile(e.TextFunction);

                if (funcProfile != null) {
                    double weightPercentage = profileData.ScaleFunctionWeight(funcProfile.Weight);
                    var tooltip = $"{Math.Round(weightPercentage * 100, 2)}% ({Math.Round(funcProfile.Weight.TotalMilliseconds, 2)} ms)";

                    int colorIndex = (int)Math.Floor(10 * (1.0 - weightPercentage));
                    e.FunctionNode.AddTag(GraphNodeTag.MakeHeatMap(colorIndex, 10));
                    e.FunctionNode.GetOrAddTag<GraphNodeTag>().Label = tooltip;
                }

                var metadataTag = e.Function.GetTag<AssemblyMetadataTag>();

                if (metadataTag != null) {
                    int instrCount = metadataTag.ElementSizeMap.Count;
                    var tooltip = $"{instrCount} instr\n{metadataTag.FunctionSize} b";

                    int colorIndex = (int)Math.Clamp(Math.Log2(instrCount), 0, 10);
                    e.FunctionNode.AddTag(GraphNodeTag.MakeHeatMap(colorIndex, 10));
                    e.FunctionNode.GetOrAddTag<GraphNodeTag>().Label = tooltip;
                }

                //int instrs = e.Function.InstructionCount;
                //
                //if (instrs == 0) {
                //    e.FunctionNode.AddTag(GraphNodeTag.MakeHeatMap2(0, 10));
                //}
                //else if (instrs <= 2) {
                //    e.FunctionNode.AddTag(GraphNodeTag.MakeHeatMap2(2, 10));
                //}
                //else if (instrs <= 5) {
                //    e.FunctionNode.AddTag(GraphNodeTag.MakeHeatMap2(4, 10));
                //}
                //else if (instrs <= 10) {
                //    e.FunctionNode.AddTag(GraphNodeTag.MakeHeatMap2(5, 10));
                //}
                //else if (instrs <= 20) {
                //    e.FunctionNode.AddTag(GraphNodeTag.MakeHeatMap2(6, 10));
                //}
                //else {
                //    e.FunctionNode.AddTag(GraphNodeTag.MakeHeatMap2(8, 10));
                //}

            };

            if (buildPartialGraph) {
                cg.Execute(section.ParentFunction, section);
            }
            else {
                cg.Execute(section);
            }

            return cg;
        }
    }
}
