// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using ProfileExplorerCore;
using ProfileExplorerCore.Graph;
using ProfileExplorerCore.IR;
using ProfileExplorerCore.Utilities;

namespace ProfileExplorer.UI;

public class GraphLayoutCache {
  private GraphKind graphKind_;
  private Dictionary<IRTextSection, CompressedString> graphLayout_;
  private Dictionary<byte[], CompressedString> shapeGraphLayout_;
  private ReaderWriterLockSlim rwLock_;

  public GraphLayoutCache(GraphKind graphKind) {
    graphKind_ = graphKind;
    shapeGraphLayout_ = new Dictionary<byte[], CompressedString>();
    graphLayout_ = new Dictionary<IRTextSection, CompressedString>();
    rwLock_ = new ReaderWriterLockSlim();
  }

  public Graph GenerateGraph<T, U>(T element, IRTextSection section, CancelableTask task,
                                   U options = null) where T : class where U : class {
    GraphVizPrinter printer = null;
    string graphText;

    try {
      rwLock_.EnterUpgradeableReadLock();

      //? TODO: Currently only FunctionIR graphs (flow, dominator, etc) are cached.
      bool useCache = typeof(T) == typeof(FunctionIR);

      if (useCache && graphLayout_.TryGetValue(section, out var graphData)) {
#if DEBUG
        Trace.TraceInformation($"Graph cache: Loading cached section graph for {section}");
#endif
        graphText = graphData.ToString();
      }
      else {
        // Check if the same Graphviz input was used before, since
        // the resulting graph will be identical even though the function is not.
        printer ??= GraphPrinterFactory.CreateInstance(graphKind_, element, options);
        string inputText = printer.PrintGraph();

        if (string.IsNullOrEmpty(inputText)) {
          // Printing the graph failed for some reason, like running out of memory.
          return null;
        }

        // The input text is looked up using a SHA256 hash that basically makes each
        // input unique, use justs 32 bytes of memory and faster to look up.
        byte[] inputTextHash = CompressionUtils.CreateSHA256(inputText);

        if (useCache && shapeGraphLayout_.TryGetValue(inputTextHash, out var shapeGraphData)) {
#if DEBUG
          Trace.TraceInformation($"Graph cache: Loading cached graph layout for {section}");
#endif
          // Associate graph layout with the section.
          graphText = shapeGraphData.ToString(); // Decompress.
          CacheGraphLayoutAndShape(section, shapeGraphData);
        }
        else {
          // This is a new graph layout that must be computed through Graphviz.
#if DEBUG
          Trace.TraceInformation($"Graph cache: Compute new graph layout for {section}");
#endif
          graphText = printer.CreateGraph(inputText, task);

          if (string.IsNullOrEmpty(graphText)) {
            Trace.TraceWarning($"Graph cache: Failed to create graph for {section}");
            return null; // Failed or canceled by user.
          }

          if (useCache) {
            CacheGraphLayoutAndShape(section, graphText, inputTextHash);
          }
        }
      }
    }
    finally {
      rwLock_.ExitUpgradeableReadLock();
    }

    // Parse the graph layout output from Graphviz to build
    // the actual Graph object with nodes and edges.
    printer ??= GraphPrinterFactory.CreateInstance(graphKind_, element, options);
    var blockNodeMap = printer.CreateNodeDataMap();
    var blockNodeGroupsMap = printer.CreateNodeDataGroupsMap();
    var graphReader = new GraphvizReader(graphKind_, graphText, blockNodeMap);
    var layoutGraph = graphReader.ReadGraph();

    if (layoutGraph != null) {
      layoutGraph.GraphOptions = options;
      layoutGraph.DataNodeGroupsMap = blockNodeGroupsMap;
    }

    return layoutGraph;
  }

  public void ClearCache() {
    graphLayout_.Clear();
    shapeGraphLayout_.Clear();
  }

  private void CacheGraphLayoutAndShape(IRTextSection section, CompressedString shapeGraphData) {
    // Acquire the write lock before updating shared data.
    try {
      rwLock_.EnterWriteLock();
      graphLayout_.Add(section, shapeGraphData);
    }
    finally {
      rwLock_.ExitWriteLock();
    }
  }

  private void CacheGraphLayoutAndShape(IRTextSection section, string graphText, byte[] inputTextHash) {
    var compressedGraphText = new CompressedString(graphText);

    // Acquire the write lock before updating shared data.
    try {
      rwLock_.EnterWriteLock();
      graphLayout_[section] = compressedGraphText;
      shapeGraphLayout_[inputTextHash] = compressedGraphText;
    }
    finally {
      rwLock_.ExitWriteLock();
    }
  }
}