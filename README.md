# Profile Explorer Overview

Profile Explorer is a tool for viewing CPU profiling traces collected through the [Event Tracing for Windows (ETW)](https://learn.microsoft.com/en-us/windows-hardware/drivers/devtest/event-tracing-for-windows--etw-) infrastructure. Its focus is on presenting the slowest parts of the profiled application through an easy-to-use but detailed UI consisting of several views and panels, such as a hot function list, call tree, flame graph, timeline, assembly view, and source file view.  

The app can be viewed as a companion to [Windows Performance Analyzer (WPA)](https://learn.microsoft.com/en-us/windows-hardware/test/wpt/windows-performance-analyzer), offering some unique features based on the binary analysis it performs and the IDE-like UI, such as easy navigation through disassembly, improved mapping to source lines, displaying the function control flow graph, viewing of multiple functions at the same time, marking, search and filtering and much more.

Summary, flame graph and timeline views of a trace:
![Screenshot 2024-09-25 111503](https://github.com/user-attachments/assets/438fdbc1-00b8-424e-a323-b46f8e8bdf4d)

Assembly, source file and flow graph views of a function:
![Screenshot 2024-09-25 122416](https://github.com/user-attachments/assets/ed3dc776-cdcc-4eb7-a9d5-db3513cc908c)

## 📖 Documentation

### [https://microsoft.github.io/profile-explorer](https://microsoft.github.io/profile-explorer)

The application also has a built-in *Help panel* that can display the same documentation. Most views also have in the upper-right corner a *question mark* icon which opens the *Help panel* at the view's documentation page. 

## 🛠️ Building

To build the application and it's external dependencies, ensure the following build tools are installed on a Windows 11 machine:  
- recent Visual Studio 2022 with the following workflows:
	- *.NET desktop development*
	- *Desktop development with C++*
	- *C++ ARM64/ARM64EC build tools (Latest)* - for native ARM64 builds
- [.NET 8.0 SDK ](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) (should be installed by Visual Studio 2022 already)

From a an admin command line window, run:  
- ```build.cmd [debug|release]``` for an x64 build.  
- ```build-arm64.cmd [debug|release]``` for a native ARM64 build.  

The debug or release build mode is used for the main project, externl dependencies are always build in release mode. The admin mode for the command line is required to register msdia140.dll using regsvr32, it needs to be done once on a machine.  

The build script will update the git submodules and build the main project and external dependencies, then copy the built dependencies and other resources to the output directory.  

The output directory with *ProfileExplorer.exe* will be found at:  
 ```src\ProfileExplorerUI\bin\[Debug|Release]\net8.0-windows```.  

After the initial build, open the solution ```src\ProfileExplorer.sln``` and use the *ProfileExplorerUI* project as the build and startup target.

## 📦 Publishing and creating the installer

To publish the application and create an installer, from a command-line window run:  
- ```installer\x64\prepare.cmd``` for an x64 build.  
- ```installer\arm64\prepare.cmd``` for a native arm64 build.  

This will build the main project as a self-containe application, build the external dependencies and create the installer executable, with output found at:  

- ```installer\[x64|arm64]\out```  
- ```installer\[x64|arm64]\profile_explorer_installer_VERSION.exe```

Currently [InnoSetup](https://jrsoftware.org/isdl.php) is used to create the installer and it must be already installed on the machine and *iscc.exe* found on *PATH*.

## 📑 Project structure

Below is a high-level overview of the main parts of the application.

### Application projects

| Location | Description |
| --- | --- |
| src/ProfileExplorerUI | The application UI implemented using WPF, main project. |
| src/ProfileExplorerCore | The UI-independent part that defines the intermediate representation (IR), parsing functions, Graphviz and Tree Sitter support, various data structures, algorithms and utilities. |
| src/ProfileExplorerUITests | Unit tests for the ProfileExplorerUI project. |
| src/ProfileExplorerCoreTests | Unit tests for the ProfileExplorerCore project. |
| src/ManagedProfiler | .NET JIT profiler extension for capturing JIT output assembly code. |
| src/PDBViewer | A small utility implemented using WinForms for dispalying the contents of PDB debug info files. |
| src/VSExtension | A Visual Studio extension that connects to the application. Not used for profiling functionality. |
| src/GrpcLib | Defines the GRPC protobuf format used by the Visual Studio extension to communicate with the application. Not used for profiling functionality. | 

### External dependencies

The following projects are build from source, as either x64 or native arm64 binaries.

| Location | Description |
| --- | --- |
| src/external/capstone | [Capstone](https://github.com/capstone-engine/capstone) disassembly framework, submodule. |
| src/external/graphviz | [Graphviz](https://gitlab.com/graphviz/graphviz) graph visualization tools, submodule. |
| src/external/tree-sitter | [Tree-sitter](https://tree-sitter.github.io/tree-sitter/) parser generator, with support for C/C++, C# and Rust, submodules. |
| src/external/TreeListView | [TreeListView](https://github.com/hazzik/TreeListView), WPF tree list view control. |

## Contributing

This project welcomes contributions and suggestions.  Most contributions require you to agree to a
Contributor License Agreement (CLA) declaring that you have the right to, and actually do, grant us
the rights to use your contribution. For details, visit https://cla.opensource.microsoft.com.

When you submit a pull request, a CLA bot will automatically determine whether you need to provide
a CLA and decorate the PR appropriately (e.g., status check, comment). Simply follow the instructions
provided by the bot. You will only need to do this once across all repos using our CLA.

This project has adopted the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/).
For more information see the [Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/) or
contact [opencode@microsoft.com](mailto:opencode@microsoft.com) with any additional questions or comments.

## Trademarks

This project may contain trademarks or logos for projects, products, or services. Authorized use of Microsoft 
trademarks or logos is subject to and must follow 
[Microsoft's Trademark & Brand Guidelines](https://www.microsoft.com/en-us/legal/intellectualproperty/trademarks/usage/general).
Use of Microsoft trademarks or logos in modified versions of this project must not cause confusion or imply Microsoft sponsorship.
Any use of third-party trademarks or logos are subject to those third-party's policies.
