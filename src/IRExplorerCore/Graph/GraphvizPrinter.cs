// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using IRExplorerCore.IR;

namespace IRExplorerCore.Graph {
    public class GraphVizPrinter {
        private int subgraphIndex_;
        private int nextInvisibleId_;

        protected string CreateNode(int id, string label, StringBuilder builder,
                                    string labelPrefix = null) {
            return CreateNode((ulong)id, label, builder, labelPrefix);
        }

        protected string CreateNode(ulong id, string label, StringBuilder builder,
                                    string labelPrefix = null) {
            string nodeName = $"n{id}";

            if (!string.IsNullOrEmpty(labelPrefix)) {
                builder.AppendFormat(CultureInfo.InvariantCulture, 
                                     "{0}[shape=rectangle, label=\"{1}{2}\"];\n", nodeName,
                                     labelPrefix, label);
            }
            else {
                builder.AppendFormat(CultureInfo.InvariantCulture,
                                     "{0}[shape=rectangle, label=\"{1}\"];\n", nodeName, label);
            }

            return nodeName;
        }

        protected string CreateNodeWithMargins(int id, string label, StringBuilder builder,
                                               double horizontalMargin, double verticalMargin,
                                               string labelPrefix = null) {
            return CreateNodeWithMargins((ulong)id, label, builder, horizontalMargin, verticalMargin, labelPrefix);
        }

        protected string CreateNodeWithMargins(ulong id, string label, StringBuilder builder,
                                               double horizontalMargin, double verticalMargin,
                                               string labelPrefix = null) {
            string nodeName = $"n{id}";

            if (!string.IsNullOrEmpty(labelPrefix)) {
                builder.AppendFormat(CultureInfo.InvariantCulture, 
                    "{0}[shape=rectangle, margin=\"{1},{2}\", label=\"{3}{4}\"];\n", nodeName,
                    horizontalMargin, verticalMargin, labelPrefix, label);
            }
            else {
                builder.AppendFormat(CultureInfo.InvariantCulture, 
                    "{0}[shape=rectangle, margin=\"{1},{2}\", label=\"{3}\"];\n", nodeName,
                    horizontalMargin, verticalMargin, label);
            }

            return nodeName;
        }

        protected string CreateInvisibleNode(StringBuilder builder) {
            string nodeName = $"inv{nextInvisibleId_++}";
            builder.AppendFormat(CultureInfo.InvariantCulture, $"{nodeName}[shape=point,width=0,height=0];\n");
            return nodeName;
        }

        protected string GetNodeName(ulong id) {
            return $"n{id}";
        }

        protected string GetNodeName(int id) {
            return $"n{id}";
        }

        protected void CreateEdge(string id1, string id2, StringBuilder builder) {
            builder.AppendFormat(CultureInfo.InvariantCulture, "{0} -> {1};\n", id1, id2);
        }

        protected void CreateEdge(ulong id1, string id2, StringBuilder builder) {
            builder.AppendFormat(CultureInfo.InvariantCulture, "n{0} -> {1};\n", id1, id2);
        }

        protected void CreateEdge(int id1, int id2, StringBuilder builder) {
            CreateEdge((ulong)id1, (ulong)id2, builder);
        }

        protected void CreateEdge(ulong id1, ulong id2, StringBuilder builder) {
            builder.AppendFormat(CultureInfo.InvariantCulture, "n{0} -> n{1};\n", id1, id2);
        }

        protected void CreateEdge(ulong id1, ulong id2, string attribute,
                                  StringBuilder builder) {
            builder.AppendFormat(CultureInfo.InvariantCulture, "n{0} -> n{1} {2};\n", id1, id2, attribute);
        }

        protected void CreateEdgeWithStyle(int id1, int id2, string style,
                                           StringBuilder builder) {
            CreateEdgeWithStyle((ulong)id1, (ulong)id2, style, builder);
        }

        protected void CreateEdgeWithStyle(ulong id1, ulong id2, string style,
                                           StringBuilder builder) {
            builder.AppendFormat(CultureInfo.InvariantCulture, "n{0} -> n{1}[style={2}];\n", id1, id2, style);
        }

        protected void StartSubgraph(int margin, StringBuilder builder) {
            builder.AppendLine($"subgraph cluster_{subgraphIndex_} {{");
            builder.AppendLine($"margin={margin};");
            subgraphIndex_++;
        }

        protected void EndSubgraph(StringBuilder builder) {
            builder.AppendLine("}");
        }

        public string PrintGraph() {
            // With extremely large graphs, the application can run out of memory,
            // better show a error message than crashing it.
            try {
                var builder = new StringBuilder(1024 * 16);
                builder.AppendLine("digraph {");
                builder.AppendLine(GetExtraSettings());
                PrintGraph(builder);
                builder.AppendLine("}");
                return builder.ToString();
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed to generate Graphviz input: {ex.Message}");
                return null;
            }
        }

        protected virtual void PrintGraph(StringBuilder builder) { }

        protected virtual string GetExtraSettings() {
            return "";
        }

        public virtual Dictionary<string, TaggedObject> CreateNodeDataMap() {
            throw new NotImplementedException();
        }

        public virtual Dictionary<TaggedObject, List<TaggedObject>> CreateNodeDataGroupsMap() {
            throw new NotImplementedException();
        }

        public string CreateGraph(CancelableTask task) {
            return CreateGraph(PrintGraph(), task);
        }

        public string CreateGraph(string inputText, CancelableTask task) {
            Trace.TraceInformation($"Graphviz task {ObjectTracker.Track(task)}: Start");
            string inputFilePath;

            try {
                inputFilePath = Path.GetTempFileName();
                File.WriteAllText(inputFilePath, inputText);
            }
            catch (Exception ex) {
                Trace.TraceError($"Graphviz task {ObjectTracker.Track(task)}: Failed writing GraphViz input file: {ex}");

                task.Completed();
                return null;
            }

            var outputText = new StringBuilder(1024 * 32);

            var psi = new ProcessStartInfo("dot.exe") {
                Arguments = $"-Tplain \"{inputFilePath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = false,
                RedirectStandardOutput = true
            };

            //? TODO: Put path between " to support whitespace in the path.

            try {
                using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

                process.OutputDataReceived += (sender, e) => {
                    outputText.AppendLine(e.Data);
                };

                process.Start();
                process.BeginOutputReadLine();

                do {
                    process.WaitForExit(200);

                    if (task.IsCanceled) {
                        Trace.TraceWarning($"Graphviz task {ObjectTracker.Track(task)}: Canceled");

                        process.CancelOutputRead();
                        process.Kill();
                        task.Completed();
                        return null;
                    }
                } while (!process.HasExited);

                process.CancelOutputRead();

                if (process.ExitCode != 0) {
                    // dot failed somehow, treat it as an error.
                    Trace.TraceError($"Graphviz task {ObjectTracker.Track(task)}: GraphViz failed with error code: {process.ExitCode}");

                    task.Completed();
                    return null;
                }
            }
            catch (Exception ex) {
                Trace.TraceError($"Graphviz task {ObjectTracker.Track(task)}: Failed running GraphViz: {ex}");

                task.Completed();
                return null;
            }

#if !DEBUG
            // Clean up temporary files.
            try {
                File.Delete(inputFilePath);
            }
            catch (Exception) { }
#endif
            task.Completed();
            Trace.TraceInformation($"Graphviz task {ObjectTracker.Track(task)}: Completed");
            return outputText.ToString();
        }
    }
}
