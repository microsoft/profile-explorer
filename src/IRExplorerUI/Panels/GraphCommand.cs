// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Windows.Input;

namespace IRExplorerUI {
    public static class GraphCommand {
        public static readonly RoutedCommand GraphFitWidth =
            new RoutedCommand("GraphFitWidth", typeof(GraphPanel));
        public static readonly RoutedCommand GraphResetWidth =
            new RoutedCommand("GraphResetWidth", typeof(GraphPanel));
        public static readonly RoutedCommand GraphFitAll =
            new RoutedCommand("GraphFitAll", typeof(GraphPanel));
        public static readonly RoutedCommand GraphZoomIn =
            new RoutedCommand("GraphZoomIn", typeof(GraphPanel));
        public static readonly RoutedCommand GraphZoomOut =
            new RoutedCommand("GraphZoomOut", typeof(GraphPanel));

        public static readonly RoutedCommand MarkBlock = new RoutedCommand("MarkBlock", typeof(GraphPanel));
        public static readonly RoutedCommand MarkPredecessors =
            new RoutedCommand("MarkPredecessors", typeof(GraphPanel));
        public static readonly RoutedCommand MarkSuccessors =
            new RoutedCommand("MarkSuccessors", typeof(GraphPanel));
        public static readonly RoutedCommand MarkGroup = new RoutedCommand("MarkGroup", typeof(GraphPanel));
        public static readonly RoutedCommand MarkLoop = new RoutedCommand("MarkLoop", typeof(GraphPanel));
        public static readonly RoutedCommand MarkLoopNest =
            new RoutedCommand("MarkLoopNest", typeof(GraphPanel));
        public static readonly RoutedCommand MarkDominators = new RoutedCommand("MarkDominators", typeof(GraphPanel));
        public static readonly RoutedCommand MarkPostDominators = new RoutedCommand("MarkPostDominators", typeof(GraphPanel));
        public static readonly RoutedCommand MarkDominanceFrontier = new RoutedCommand("MarkDominanceFrontier", typeof(GraphPanel));
        public static readonly RoutedCommand MarkPostDominanceFrontier = new RoutedCommand("MarkPostDominanceFrontier", typeof(GraphPanel));

        public static readonly RoutedCommand ClearMarked =
            new RoutedCommand("ClearMarked", typeof(GraphPanel));
        public static readonly RoutedCommand ClearAllMarked =
            new RoutedCommand("ClearAllMarked", typeof(GraphPanel));

        public static readonly RoutedCommand SelectQueryBlock1 =
            new RoutedCommand("SelectQueryBlock1", typeof(GraphPanel));
        public static readonly RoutedCommand SelectQueryBlock2 =
            new RoutedCommand("SelectQueryBlock2", typeof(GraphPanel));
        public static readonly RoutedCommand SwapQueryBlocks =
            new RoutedCommand("SwapQueryBlocks", typeof(GraphPanel));
        public static readonly RoutedCommand CloseQueryPanel =
            new RoutedCommand("CloseQueryPanel", typeof(GraphPanel));
        public static readonly RoutedCommand ShowReachablePath =
            new RoutedCommand("ShowReachablePath", typeof(GraphPanel));
    }
}
