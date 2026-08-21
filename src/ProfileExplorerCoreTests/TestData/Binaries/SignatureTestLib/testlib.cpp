// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
// Test fixture source for PdbFunctionSignatureTests (ProfileExplorer.Profiling.Tests). Compiled
// with a full private PDB via the local MSVC toolchain (x64, x86, and ARM64) -- these functions
// have known, exactly verifiable signatures, unlike shipped/public-symbol-only PDBs (see
// PdbFunctionSignatureTests' TryGetFunctionSignature_PublicSymbolOnlyPdb_ReturnsNullRatherThanFabricating
// for why that distinction matters).
//
// Rebuild all three architectures: run BuildTestLib.cmd in this directory (requires Visual
// Studio/Build Tools with the x86/x64 and ARM64 VC++ components installed).
//
#include <windows.h>

// Simple functions with known, verifiable signatures for PDB type-extraction testing.
extern "C" __declspec(dllexport) int __cdecl AddIntegers(int a, int b) {
    return a + b;
}

extern "C" __declspec(dllexport) double __stdcall ComputeRatio(double numerator, double denominator, int scale) {
    return (numerator / denominator) * scale;
}

extern "C" __declspec(dllexport) void __cdecl CopyBuffer(char* destination, const char* source, unsigned int length) {
    for (unsigned int i = 0; i < length; i++) {
        destination[i] = source[i];
    }
}


// Simple functions with known, verifiable signatures for PDB type-extraction testing.
extern "C" __declspec(dllexport) int __cdecl AddIntegers(int a, int b) {
    return a + b;
}

extern "C" __declspec(dllexport) double __stdcall ComputeRatio(double numerator, double denominator, int scale) {
    return (numerator / denominator) * scale;
}

extern "C" __declspec(dllexport) void __cdecl CopyBuffer(char* destination, const char* source, unsigned int length) {
    for (unsigned int i = 0; i < length; i++) {
        destination[i] = source[i];
    }
}
