// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Text;

namespace Client {
    public class PostDominatorTreePanel : GraphPanel {
        public PostDominatorTreePanel() : base() {

        }

        public override ToolPanelKind PanelKind => ToolPanelKind.PostDominatorTree;
    }
}
