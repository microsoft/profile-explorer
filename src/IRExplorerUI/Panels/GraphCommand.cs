// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System.Windows.Input;

namespace IRExplorerUI;

public static class GraphCommand {
  public static readonly RoutedCommand GraphFitWidth = new("GraphFitWidth", typeof(GraphPanel));
  public static readonly RoutedCommand GraphResetWidth = new("GraphResetWidth", typeof(GraphPanel));
  public static readonly RoutedCommand GraphFitAll = new("GraphFitAll", typeof(GraphPanel));
  public static readonly RoutedCommand GraphZoomIn = new("GraphZoomIn", typeof(GraphPanel));
  public static readonly RoutedCommand GraphZoomOut = new("GraphZoomOut", typeof(GraphPanel));
  public static readonly RoutedCommand MarkBlock = new("MarkBlock", typeof(GraphPanel));
  public static readonly RoutedCommand MarkPredecessors = new("MarkPredecessors", typeof(GraphPanel));
  public static readonly RoutedCommand MarkSuccessors = new("MarkSuccessors", typeof(GraphPanel));
  public static readonly RoutedCommand MarkGroup = new("MarkGroup", typeof(GraphPanel));
  public static readonly RoutedCommand MarkLoop = new("MarkLoop", typeof(GraphPanel));
  public static readonly RoutedCommand MarkLoopNest = new("MarkLoopNest", typeof(GraphPanel));
  public static readonly RoutedCommand MarkDominators = new("MarkDominators", typeof(GraphPanel));
  public static readonly RoutedCommand MarkPostDominators = new("MarkPostDominators", typeof(GraphPanel));
  public static readonly RoutedCommand MarkDominanceFrontier = new("MarkDominanceFrontier", typeof(GraphPanel));
  public static readonly RoutedCommand MarkPostDominanceFrontier = new("MarkPostDominanceFrontier", typeof(GraphPanel));
  public static readonly RoutedCommand ClearMarked = new("ClearMarked", typeof(GraphPanel));
  public static readonly RoutedCommand ClearAllMarked = new("ClearAllMarked", typeof(GraphPanel));
  public static readonly RoutedCommand SelectQueryBlock1 = new("SelectQueryBlock1", typeof(GraphPanel));
  public static readonly RoutedCommand SelectQueryBlock2 = new("SelectQueryBlock2", typeof(GraphPanel));
  public static readonly RoutedCommand SwapQueryBlocks = new("SwapQueryBlocks", typeof(GraphPanel));
  public static readonly RoutedCommand CloseQueryPanel = new("CloseQueryPanel", typeof(GraphPanel));
  public static readonly RoutedCommand ShowReachablePath = new("ShowReachablePath", typeof(GraphPanel));
}
