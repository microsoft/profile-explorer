// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Text;
using Core.Analysis;
using Core.GraphViz;
using Core.IR;

namespace Core.Graph {
    public enum GraphKind {
        FlowGraph,
        DominatorTree,
        PostDominatorTree
    }

    public static class GraphPrinterFactory {
        public static GraphVizPrinter CreateInstance(GraphKind kind, FunctionIR function) {
            switch(kind) {
                case GraphKind.FlowGraph: {
                    return new CFGPrinter(function);
                }
                case GraphKind.DominatorTree: {
                    return new DominatorTreePrinter(function, DominatorAlgorithmOptions.Dominators);
                }
                case GraphKind.PostDominatorTree: {
                    return new DominatorTreePrinter(function, DominatorAlgorithmOptions.PostDominators);
                }
            }

            throw new NotImplementedException("Unsupported graph type");
        }
    }
}
