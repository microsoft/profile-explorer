// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using IRExplorerCore.Analysis;
using IRExplorerCore.Graph;
using IRExplorerCore.IR;

namespace IRExplorerCore.Graph {
    public static class GraphPrinterFactory {
        public static GraphVizPrinter CreateInstance<T, U>(
            GraphKind kind, T element, U options, ICompilerIRInfo compilerIrInfo) where T : class where U : class {
            if (typeof(T) == typeof(FunctionIR)) {
                return kind switch {
                    GraphKind.FlowGraph => new FlowGraphPrinter(element as FunctionIR, compilerIrInfo),
                    GraphKind.DominatorTree => new DominatorTreePrinter(element as FunctionIR,
                                                                        DominatorAlgorithmOptions.Dominators),
                    GraphKind.PostDominatorTree => new DominatorTreePrinter(element as FunctionIR,
                                                                            DominatorAlgorithmOptions.PostDominators),
                    _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
                };
            }
            else if (typeof(T) == typeof(IRElement)) {
                switch (kind) {
                    case GraphKind.ExpressionGraph: {
                        return new ExpressionGraphPrinter(element as IRElement,
                                                          options as ExpressionGraphPrinterOptions);
                    }
                }
            }
            else if (typeof(T) == typeof(IRTextSummary)) {
                switch (kind) {
                    case GraphKind.CallGraph: {
                        return new CallGraphPrinter(element as CallGraph,
                                                    options as CallGraphPrinterOptions);
                    }
                }
            }

            throw new NotImplementedException("Unsupported graph type");
        }
    }
}
