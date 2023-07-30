// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using IRExplorerCore.Analysis;
using IRExplorerCore.Graph;
using IRExplorerCore.IR;

namespace IRExplorerCore.Graph {
    public static class GraphPrinterFactory {
        public static GraphVizPrinter CreateInstance<T, U>(
            GraphKind kind, T element, U options,
            GraphVizPrinterNameProvider nameProvider) where T : class where U : class {
            if (typeof(T) == typeof(FunctionIR)) {
                return kind switch {
                    GraphKind.FlowGraph => new FlowGraphPrinter(element as FunctionIR, nameProvider),
                    GraphKind.DominatorTree => new DominatorTreePrinter(element as FunctionIR,
                                                                        DominatorAlgorithmOptions.Dominators,
                                                                        nameProvider),
                    GraphKind.PostDominatorTree => new DominatorTreePrinter(element as FunctionIR,
                                                                            DominatorAlgorithmOptions.PostDominators,
                                                                            nameProvider),
                    _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
                };
            }
            else if (typeof(T) == typeof(IRElement)) {
                switch (kind) {
                    case GraphKind.ExpressionGraph: {
                        return new ExpressionGraphPrinter(element as IRElement,
                                                          options as ExpressionGraphPrinterOptions,
                                                          nameProvider);
                    }
                }
            }
            else if (typeof(T) == typeof(IRTextSummary)) {
                switch (kind) {
                    case GraphKind.CallGraph: {
                        return new CallGraphPrinter(element as CallGraph,
                                                    options as CallGraphPrinterOptions,
                                                    nameProvider);
                    }
                }
            }

            throw new NotImplementedException("Unsupported graph type");
        }
    }
}