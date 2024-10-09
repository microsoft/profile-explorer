#### Overview

The Flow Graph view displays the [control-flow graph (CFG)](https://en.wikipedia.org/wiki/Control-flow_graph) of the function in the active assembly view, with [basic blocks](https://en.wikipedia.org/wiki/Basic_block) annotated with profiling information.  


The function CFG makes it easier to see the structure of a function and control-flow created by jumps, branches and loops.  

[![Profiling UI screenshot](img/flow-graph-view_501x693.png){: style="width:320px"}](img/flow-graph-view_501x693.png){:target="_blank"}

Each basic block is represented by a rectangle, with the block number as the label. An edge between two blocks means the source and destination block are connected either through a jump/branch or fall-through code.  

Color coding of both blocks and edges is used to help identify control flow. The used colors can be customized in the [Flow Graph options](#view-options).

Block border colors coding (default colors):  

- blue: blocks ends with a branch instruction.
- green: block is the target of a loop back-edge (it's a loop header).
- red: block ends with a return instruction (it's a function exit),

Edge color coding (default colors):  

- blue: target block is a branch target (branch in the source block jumps to it).
- green: loop back-edge, target block is a loop header (start of a  loop).
- red: target block is a function exit block.
- dotted: target block is the [immediate dominator](https://en.wikipedia.org/wiki/Dominator_(graph_theory)) of the source block.

When a block is selected, the block and its instructions are also selected in the *Assembly* view, like in the example below where B5 is selected. Notice that B5 is a single-block, nested loop, while B4 is the loop header block of a larger loop including B5.  

[![Profiling UI screenshot](img/flow-graph-select_1277x370.png)](img/flow-graph-select_1277x370.png){:target="_blank"} 


#### Profiling annotations

#### View interaction

???+ abstract "Toolbar"
    | Button | Description |
    | ------ | ------------|
    | ![](img/flame-graph-toolbar-sync.png) | If enabled, selecting a function also selects it in the other profiling views. |

???+ abstract "Mouse shortcuts"
    | Action | Description |
    | ------ | ------------|
    | Hover | Hovering over a function displays a popup with the stack trace (call path) end with the slowest function's instance. Pin or drag the popup to keep it open.|

???+ abstract "Keyboard shortcuts"
    | Keys | Description |
    | ------ | ------------|
    | Return | Opens the Assembly view of the selected function in the active tab. |

???+ abstract "Right-click context menu"
    [![Profiling UI screenshot](img/flow-graph-context-menu_383x548.png){: style="width:380px"}](img/flow-graph-context-menu_383x548.png){:target="_blank"}  

#### View options

*Click* on the *Gears* icon in the top-right part of the view displays the options panel (alternatively, use the *Flow Graph* tab in the application *Settings* window.).  

The tabs below describe each page of the options panel:  
=== "General"
    [![Profiling UI screenshot](img/flow-graph-options-general_558x423.png){: style="width:400px"}](img/flow-graph-options-general_558x423.png){:target="_blank"}  

=== "Appearance"
    [![Profiling UI screenshot](img/flow-graph-options-appearance_527x594.png){: style="width:400px"}](img/flow-graph-options-appearance_527x594.png){:target="_blank"}

- basic blocks coded by color
- edges color coding, red for exit block, green loop
- click on node selects block in assembly view, on ASM selects block
- profiling info annotates hot blocks (label below) and color
- mouse and keyboard shorcuts for zoom, pan