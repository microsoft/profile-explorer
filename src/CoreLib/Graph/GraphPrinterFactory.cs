// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using IRExplorerCore.Analysis;
using IRExplorerCore.GraphViz;
using IRExplorerCore.IR;

namespace IRExplorerCore.Graph {
    public enum GraphKind {
        FlowGraph,
        DominatorTree,
        PostDominatorTree,
        ExpressionGraph
    }

    public static class GraphPrinterFactory {
        public static GraphVizPrinter CreateInstance<T, U>(
            GraphKind kind, T element, U options) where T : class where U : class {
            if (typeof(T) == typeof(FunctionIR)) {
                switch (kind) {
                    case GraphKind.FlowGraph: {
                        return new CFGPrinter(element as FunctionIR);
                    }
                    case GraphKind.DominatorTree: {
                        return new DominatorTreePrinter(element as FunctionIR,
                                                        DominatorAlgorithmOptions.Dominators);
                    }
                    case GraphKind.PostDominatorTree: {
                        return new DominatorTreePrinter(element as FunctionIR,
                                                        DominatorAlgorithmOptions
                                                            .PostDominators);
                    }
                    default:
                        throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
                }
            }
            else if (typeof(T) == typeof(IRElement)) {
                switch (kind) {
                    case GraphKind.ExpressionGraph: {
                        return new ExpressionGraphPrinter(element as IRElement,
                                                          options as
                                                              ExpressionGraphPrinterOptions);
                    }
                }
            }

            throw new NotImplementedException("Unsupported graph type");
        }
    }
}
