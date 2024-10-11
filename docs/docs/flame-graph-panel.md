???+ note "Introduction to flame graphs"
    A flame graph is an alternative, more compact way of viewing a call tree. In this view, function instances are nodes with a size proportional to the time spent relative to the caller (parent) function and makes it easier to identify the portions of the app that take most of the time.  

    Nodes are sorted in the horizontal direction based on decreasing time relative to their parent node, while the ordering in the vertical direction forms a stack trace (a path in the call tree). The function taking the most time in the application is then found in the leftmost, bottom part of the Flame graph.
   
    ![Flame graph diagram](img/flame-graph.png){: style="width:320px"}  
    In the example above, the *main* function is considered the process entry point, calling *foo* and *bar*, with *foo* taking 60% of the time and *bar* 40%. Function *foo* spends a part of its total (inclusive) time in the calls to *baz* and *etc1*, while the rest is self (exclusive) time, meaning instructions part of *foo* which are not calls to other functions.  

    Note that there are two instances of the function *baz* with different execution times, each with a unique path to it in the call tree starting from *main* (all other functions have a single instance). You can see the time of each instance by *hovering* with the mouse over it or in the *Details panel* after it's selected.

    The following links provide an introduction to the flame graph visualization concept, its history, and how it's being used across the industry for performance investigations.  

    - [CPU Flame Graphs (brendangregg.com)](https://www.brendangregg.com/FlameGraphs/cpuflamegraphs.html)
    - [Visualizing Performance - The Developers’ Guide to Flame Graphs (youtube.com)](https://www.youtube.com/watch?v=VMpTU15rIZY)

#### Overview

The Flame graph view is the main means of identifying the parts of the application where most time is spent. 

[![Profiling UI screenshot](img/flame-graph-panel_1435x557.png)](img/flame-graph-panel_1435x557.png){:target="_blank"}

The view has three parts:

- a toolbar at the top, with action buttons and the *Search* input box.
- the interactive flame graph itself.
- the Details panel on the right side. The panel displays detailed info about the selected node(s), and its visibility can be toggled using the *Details* button in the toolbar.

##### Flame graph nodes

The flame graph displays nodes representing functions stacked top to bottom according to the call tree paths forming the stack traces. Each node is a unique instance of a function — a function having multiple instances means there are several paths in the call tree that call the function.  

Each node has the demangled (undecorated) function name, optionally prepended with the module name, followed by the execution time percentage relative to the entire trace and the execution time.  

By default, the nodes are color-coded based on the module names to which the functions belong. Nodes for functions executing in the kernel/managed context are marked with a different text and border color (blue by default). The displayed text fields and colors can be customized in the Flame graph options.  

???+ note
    When the called function nodes are too small to be visible in the view, they are collapsed under a placeholder node rendered with a hatch pattern. Placeholder nodes are expanded into individual nodes when zooming in the view.  

##### Navigating the Flame graph

A *double-click* on a node expands it to cover the entire view and may expand the collapsed nodes. For example, the called nodes become visible after double-clicking the node hovered in the screenshot above.  

[![Profiling UI screenshot](img/flame-graph-expand_944x428.png)](img/flame-graph-expand_944x428.png){:target="_blank"}

Expanding a node can be repeated to go deeper down the call path. The *Back* button in the toolbar (or *Backspace* key/*Back* mouse button) undoes the operation and returns the view to its previous state.  

[![Profiling UI screenshot](img/flame-graph-expand2_947x456.png)](img/flame-graph-expand2_947x456.png){:target="_blank"}

???+ note
    In the screenshot above, the nodes starting with *ntoskrnl.exe!KiPageFault* use a different style to mark code executing in kernel mode. The user mode and kernel mode call stacks are automatically combined.

##### Changing the root node

It can be helpful to view only a subregion of the Flame graph. By changing the root node, only nodes for functions starting with the new root are displayed, and execution time percentages are computed relative to the new root, which starts at 100%.

To change the root node, from the right-click context menu, select *Set Function as Root* (alternatively, use the *Alt+Double-click* shortcut). After the switch, the toolbar displays the name of the current root node. Setting a new root node can be repeated in the new view.

To remove the root node and view the entire Flame graph, *click* the *X* button next to its name in the toolbar. If multiple nested root nodes were set, removing the current node activates the previous one.

[![Profiling UI screenshot](img/flame-graph-root_946x435.png)](img/flame-graph-root_946x435.png){:target="_blank"}

#### Searching functions

Use the *search input box* in the toolbar to search for functions with a specific name using a case-insensitive substring search. Matching nodes and function names are marked, and the *up/down* buttons on the right can be used to navigate between results. Press the Escape key to reset the search or the X button next to the input box.

[![Profiling UI screenshot](img/flame-graph-search_1078x286.png)](img/flame-graph-search_1078x286.png){:target="_blank"}

#### View interaction

???+ abstract "Toolbar"
    | Button | Description |
    | ------ | ------------|
    | ![](img/flame-graph-toolbar-back.png) | Undoes the previous action, such as expanding a node or changing the root node. |
    | ![](img/flame-graph-toolbar-reset.png) | Resets the view to it's original state, displaying the entire Flame graph. |
    | ![](img/flame-graph-toolbar-minus.png) | Zooms out the view around the center point. |
    | ![](img/flame-graph-toolbar-plus.png) | Zooms in the view around the center point. |
    | ![](img/flame-graph-toolbar-sync.png) | If enabled, selecting a node also selects the associated function in the other profiling views. |
    | ![](img/flame-graph-toolbar-source.png) | If enabled, selecting a node also displays the associated function in the Source file view, with the source lines annotated with profiling data. |
    | ![](img/flame-graph-toolbar-module.png) | If enabled, display the module name before the function name in the nodes as module!function. |
    | ![](img/flame-graph-toolbar-details.png) | If enabled, display the Details panel on the right side of the Flame graph view. |
    | Search box | Search for nodes with a specific function name using a case-insensitive substring search. Press the *Escape* key to reset the search or the *X* button next to the input box. |

???+ abstract "Mouse shortcuts"
    | Action | Description |
    | ------ | ------------|
    | Hover | Hovering over a node briefly displays a preview popup with the complete function name and total/self execution times. Clicking the *Pin button* or dragging the popup expands it into a panel equivalent to the *Details panel*. Multiple such panels can be kept open at the same time. |
    | Click | Selects a node and deselects any previously selected nodes. The *Details panel* is updated and, if *Sync* is enabled in the toolbar, the function is selected in the other panels. Displays the associated function in the Source file view if *Source* is enabled in the toolbar. <br><br>Clicking an empty part of the view deselects all nodes. |
    | Ctrl+Click | Selects the pointed node and keeps the previously selected nodes (append). The *Details panel* is updated to display a combined view of all selected nodes. |
    | Shift+Click | When a node is selected, it expands the selection to include all nodes in the call stack between the pointed node and the selected one. The *Details panel* is updated to display a combined view of all selected nodes. |
    | Double-click | Expands (zooms-in) the pointed node to cover the view's width, adjusting child node widths accordingly. |
    | Ctrl+Double-click | Opens the Assembly view of the selected function in the active tab. |
    | Ctrl+Shift+Double-click | Opens the Assembly view of the selected function in a new tab. |
    | Alt+Double-click | Sets the selected node as the root node of the Flame graph. |
    | Back | If the mouse has an optional *Back* button, this undoes the previous action, such as expanding a node (double-click) or changing the root node. An alternative is pressing the *Backspace* key or the *Back* button in the toolbar.|
    | Right-click | Shows the context menu for the selected nodes. |
    | Click+Drag | If the flame graph is larger than the view, clicking on and dragging an empty part of the view moves the view in the direction of the mouse. |
    | Scroll wheel | Scrolls the view vertically if the flame graph is larger than the view. |
    | Shift+Scroll wheel | Scrolls the view horizontally if the flame graph is larger than the view. |
    | Ctrl+Scroll wheel | Zooms in or out the view around the mouse pointer position. |
    | Click+Scroll wheel | Zooms in or out the view around the mouse pointer position. |

    !!! note
        When multiple nodes are selected, the application status bar displays the sum of their execution time as a percentage and value.  
        [![Profiling UI screenshot](img/flame-graph-selection_619x155.png){: style="width:450px"}](img/flame-graph-selection_619x155.png){:target="_blank"}
    
???+ abstract "Keyboard shortcuts"
    | Keys | Description |
    | ------ | ------------|
    | Return | Expands (zooms-in) the pointed node to cover the view's width, adjusting child node widths accordingly. |
    | Ctrl+Return | Opens the Assembly view of the selected function in the active tab. |
    | Ctrl+Shift+Return | Opens the Assembly view of the selected function in a new tab. |
    | Alt+Return | Opens a preview popup with the assembly of the selected function. Press the *Escape* key to close the popup.<br><br>Multiple preview popups can be can be kept open at the same time. |
    | Alt+Shift+Return | Opens a preview popup with the assembly of the selected function, with profile data filtered to include only the selected instance. |
    | Ctrl+C | Copies to clipboard a HTML and Markdown table with a summary of the selected nodes. |
    | Ctrl+Shift+C | Copies to clipboard the function names of the selected nodes. |
    | Ctrl+Alt+C | Copies to clipboard the mangled/decorated function names of the selected nodes. |
    | Backspace | Undoes the previous action, such as expanding a node (double-click) or changing the root node. |
    | Ctrl+= | Zooms in the view around the center point. |
    | Ctrl+- | Zooms out the view around the center point. |
    | Ctrl+0<br>Ctrl+R | Resets the view to the initial state. |
    | Arrow keys | Scrolls the view in the horizontal and vertical directions if the flame graph is larger than the view. |

???+ abstract "Right-click context menu"
    [![Profiling UI screenshot](img/flame-graph-context-menu_539x651.png){: style="width:380px"}](img/flame-graph-context-menu_539x651.png){:target="_blank"}  

#### Details panel

The Details panel shows extended information about the selected node(s) in the Flame graph. It provides a quick overview of the slowest functions and modules being called directly or through other functions starting with the selection.  

The top shows the *Total* (inclusive) execution time and *Self* (exclusive) execution time values for the selected node (function instance). The right side shows the index of the chosen instance, among all instances, with the slowest instance having the lowest index. Use the left/right arrow buttons to switch to the previous/next function instance.  

???+ note
    Functions in the lists have a right-click context menu with options to open the Assembly view, preview popup, and select the function in the other views.  

    *Double-click/Ctrl+Return* opens the Assembly view for the selected function. Combine these shortcuts with the *Shift* key to open the Assembly view in a new tab instead.  
    *Hovering* with the mouse over a function opens a preview popup with the function's assembly.    
    
    When multiple functions are selected, the application status bar displays the sum of their execution time as a percentage and value.

The information displayed in the tabs below is for the selected function instance only, except the *Info* tab which displays statistics for all instances instead.  

=== "Info"
    [![Profiling UI screenshot](img/details-panel-info_565x786.png){: style="width:380px"}](img/details-panel-info_565x786.png){:target="_blank"}  

    The Info tab displays details and statistics about all instances of the selected funcion node.  

    | Section | Description |
    | ------ | ------------|
    | Instances | Displays total execution time (sum), average, and median across all function instances, as a total/self execution time percentage relative to the entire trace and execution time value. |
    | Histogram | The histogram displays the time distribution across all function instances. Instances with similar times are grouped, and the number of instances in each group is shown above, with more details when *hovering* over a group with the mouse.<br><br>*Clicking* on a group selects the first node from the group in the Flame graph view. The *Total/Self* radio buttons switch between using the total time or self time for the histogram.<br><br>In the above example, there are 3 instances of function *genString*, one with an execution time of ~1.5ms and two, binned together, with ~1sec each. The time of the selected instance is marked with a green arrow, and the average/median times are indicated by red/blue dotted lines. |
    | Threads | Displays the list of threads on which all function instances execute, with each thread's total/self execution time percentage and execution time value.<br><br>*Right-clicking* a thread shows a context menu with options to open the Assembly view with profile data filtered to include only the selected thread and multiple options for changing the thread filtering for the entire trace.<br><br>*Double-clicking* a thread filters the entire trace to show only code executing on that thread. |
    | Module | Displays the name of the module to which the function belongs. Shortcut buttons on the right side:<br> <table>  <tbody>  <tr>  <td>![](img/details-panel-mark-module.png){: style="width:24px"}</td>  <td>Marks all function nodes belonging to the module with a color.</td>  </tr> <tr>  <td>![](img/details-panel-copy.png){: style="width:24px"}</td>  <td>Copies to clipboard the module name.</td>  </tr>   </tbody>  </table> |
    | Function | Displays the complete function name, followed by the execution context as U/K/M standing for User/Kernel/Managed mode. Shortcut buttons on the right side:<br> <table>  <tbody>  <tr>  <tr>  <td>![](img/details-panel-preview.png){: style="width:24px"}</td>  <td>Opens a preview popup with the function's assembly.</td>  </tr>  <tr>  <td>![](img/details-panel-tab.png){: style="width:24px"}</td>  <td>Opens the function's Assembly view in a new tab.</td>  </tr>  <td>![](img/details-panel-mark-module.png){: style="width:24px"}</td>  <td>Marks all function nodes with a color.</td>  </tr> <tr>  <td>![](img/details-panel-copy.png){: style="width:24px"}</td>  <td>Copies to clipboard the function name.</td>  </tr>   </tbody>  </table> |

=== "Stack"
    [![Profiling UI screenshot](img/details-panel-stack_565x706.png){: style="width:380px"}](img/details-panel-stack_565x706.png){:target="_blank"}  

    The Stack tab displays the call stack (stack trace) leading to the selected function instance node.
=== "Functions"
    [![Profiling UI screenshot](img/details-panel-functions_565x706.png){: style="width:380px"}](img/details-panel-functions_565x706.png){:target="_blank"}  

    The Functions tab lists the slowest functions being called directly or through other functions, starting with the selected function instance node. By default, the list is sorted by self (exclusive) time in descending order. The Flame graph options can change the sorting to consider total (inclusive) time instead.
=== "Modules"
    [![Profiling UI screenshot](img/details-panel-modules_565x706.png){: style="width:380px"}](img/details-panel-modules_565x706.png){:target="_blank"}  

    The Modules tab is similar to the Functions tab, with the difference that functions are first grouped by the module they belong to, and the execution time per module is also displayed. Selecting a module shows the list of the slowest functions.
=== "Categories"
    [![Profiling UI screenshot](img/details-panel-categories_565x706.png){: style="width:380px"}](img/details-panel-categories_565x706.png){:target="_blank"}  

    The Categories tab is similar to the Functions tab, with the difference that functions are first grouped by the category they belong to, and the execution time per category is also displayed. Selecting a category shows the list of the slowest functions.<br><br>*Right-click* on a category shows options to export a report as a HTML/Markdown file and copy the report to clipboard.
=== "Instances"
    [![Profiling UI screenshot](img/details-panel-instances_565x706.png){: style="width:380px"}](img/details-panel-instances_565x706.png){:target="_blank"}  

    The Instances tab lists all instances of the function, sorted by total (inclusive) execution time.

#### View options

*Click* on the *Gears* icon in the top-right part of the view displays the options panel (alternatively, use the *Flame Graph* tab in the application *Settings* window.).  

The tabs below describe each page of the options panel:  
=== "General"
    [![Profiling UI screenshot](img/flame-graph-options-general_586x337.png){: style="width:400px"}](img/flame-graph-options-general_586x337.png){:target="_blank"}  

=== "Appearance"
    [![Profiling UI screenshot](img/flame-graph-options-appearance_592x809.png){: style="width:400px"}](img/flame-graph-options-appearance_592x809.png){:target="_blank"}  

=== "Details Panel"
    [![Profiling UI screenshot](img/flame-graph-options-details_590x690.png){: style="width:400px"}](img/flame-graph-options-details_590x690.png){:target="_blank"}  

#### Documentation in progress
- Marking nodes
- View options