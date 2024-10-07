[![Profiling UI screenshot](img/flow-graph-view_501x693.png){: style="width:320px"}](img/flow-graph-view_501x693.png){:target="_blank"}

The Flow Graph view displays the [control-flow graph (CFG)](https://en.wikipedia.org/wiki/Control-flow_graph) of the function in the active assembly view, with basic blocks annotated with profiling information.

The function CFG makes it easier to see the structure of a function, loops. Blocks and arrows color-coded, ex default colors for loops, exit blocks TODO

Example img with selection sync on block click.

[![Profiling UI screenshot](img/flow-graph-select_1277x370.png)](img/flow-graph-select_1277x370.png){:target="_blank"} 

- basic blocks coded by color
- edges color coding, red for exit block, green loop
- click on node selects block in assembly view, on ASM selects block
- profiling info annotates hot blocks (label below) and color
- mouse and keyboard shorcuts for zoom, pan