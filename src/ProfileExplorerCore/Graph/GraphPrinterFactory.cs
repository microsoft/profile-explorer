// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using ProfileExplorer.Core.Analysis;
using ProfileExplorer.Core.IR;

namespace ProfileExplorer.Core.Graph;

public static class GraphPrinterFactory {
  public static GraphVizPrinter CreateInstance<T, U>(
    GraphKind kind, T element, U options) where T : class where U : class {
    if (typeof(T) == typeof(FunctionIR)) {
      return kind switch {
        GraphKind.FlowGraph => new FlowGraphPrinter(element as FunctionIR),
        GraphKind.DominatorTree => new DominatorTreePrinter(element as FunctionIR,
                                                            DominatorAlgorithmOptions.Dominators),
        GraphKind.PostDominatorTree => new DominatorTreePrinter(element as FunctionIR,
                                                                DominatorAlgorithmOptions.PostDominators),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
      };
    }

    if (typeof(T) == typeof(IRElement)) {
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