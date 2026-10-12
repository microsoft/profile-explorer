// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

// NativeAddressDisassemblyService moved to ProfileExplorerCore/Binary/ — it combines the
// TraceEvent-coupled BinaryFileLocator (Core) with NativeAddressDisassembler (Profiling), and
// Core references Profiling (not the reverse), so the on-demand auto-load wrapper must live in Core
// to keep this Profiling library TraceEvent-free.

