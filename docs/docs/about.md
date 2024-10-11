#### Overview

Profile Explorer is a tool for viewing CPU profiling traces collected through the [Event Tracing for Windows (ETW)]((https://learn.microsoft.com/en-us/windows-hardware/drivers/devtest/event-tracing-for-windows--etw-)) infrastructure on machines with x64 and ARM64 CPUs. Its focus is on presenting the slowest parts of the profiled application through an easy-to-use but detailed UI consisting of several views, such as slow functions list, flame graph, call tree, timeline, assembly code view, and source file view.  

The app offers some unique features based on the binary analysis it performs and the IDE-like UI, such as easy navigation through disassembly, improved mapping to source lines, displaying the function [control-flow graph](https://en.wikipedia.org/wiki/Control-flow_graph), viewing of multiple functions at the same time, marking, searching, filtering and much more.  

One of the app's key advantages is its performance. It loads traces fast and offers near-instant UI interaction, even for very large traces (over 10 GB ETL files). Most profile processing steps and algorithms are multi-threaded and don't block the UI.

#### Automatic updates

The app includes an auto-update feature that notifies you when a new version is available and offers to download and install it.

When the app starts, if a new version is available, an *Update Available* button is displayed in the status bar. *Click* on it to see the release notes and install the update.

![](img/update-ckeck.png){:style="width:400px"}

#### Download

Installers for the latest version:  

- [x64 installer](https://github.com/microsoft/profile-explorer/releases/latest/download/profile_explorer_installer_x64.exe)  
- [ARM64 installer](https://github.com/microsoft/profile-explorer/releases/latest/download/profile_explorer_installer_arm64.exe)

Use the ARM64 installer if you have a machine with an ARM64 CPU, since it includes a native build of the app (no emulation), otherwise use the x64 installer. Note that the x64 app can open traces recorded on ARM64 machines and vice versa.  

The installers for previous versions can be downloaded from the [Releases](https://github.com/microsoft/profile-explorer/releases) page.  