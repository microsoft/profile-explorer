// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
#define TRACE_EVENT

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using IRExplorerCore;
using IRExplorerCore.IR;
using IRExplorerCore.IR.Tags;
using Dia2Lib;
using System.Diagnostics;
using System.IO;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using Microsoft.Diagnostics.Symbols;

namespace IRExplorerUI.Compilers {

    //? https://stackoverflow.com/questions/34960989/how-can-i-hide-dll-registration-message-window-in-my-application
    
    //? Use for-each iterators everywhere
    //? Free more COM?

    public sealed class PDBDebugInfoProvider : IDisposable, IDebugInfoProvider {
        // https://docs.microsoft.com/en-us/windows-hardware/drivers/debugger/symbol-path
        private const string DefaultSymbolSource = @"SRV*https://msdl.microsoft.com/download/symbols";
        private const string DefaultSymbolCachePath = @"C:\Symbols";

        private SymbolFileSourceOptions options_;
        private string debugFilePath_;
        private IDiaDataSource diaSource_;
        private IDiaSession session_;
        private IDiaSymbol globalSymbol_;
        private List<DebugFunctionInfo> cachedFunctionList_;

        public PDBDebugInfoProvider(SymbolFileSourceOptions options) {
            options_ = options;
        }

        public Machine? Architecture => null;

        public static async Task<string> LocateDebugInfoFile(SymbolFileDescriptor symbolFile, SymbolFileSourceOptions options) {
            if (symbolFile == null) {
                return null;
            }

            string symbolSearchPath = ConstructSymbolSearchPath(options);
#if DEBUG
            using var logWriter = new StringWriter();
#else
            var logWriter = StringWriter.Null;
#endif
            using var symbolReader = new SymbolReader(logWriter, symbolSearchPath);
            var result = await Task.Run(() => symbolReader.FindSymbolFilePath(symbolFile.FileName, symbolFile.Id, symbolFile.Age)).ConfigureAwait(false);

#if DEBUG
            Trace.WriteLine($">> TraceEvent FindSymbolFilePath for {symbolFile.FileName}");
            Trace.WriteLine(logWriter.ToString());
            Trace.WriteLine($"<< TraceEvent");
#endif
            return result;
        }

        public static string ConstructSymbolSearchPath(SymbolFileSourceOptions options) {
            //? TODO: Option for var ret = Environment.GetEnvironmentVariable("_NT_SYMBOL_PATH");
            var defaultSearchPath = "";

            if (options.UseDefaultSymbolSource) {
                defaultSearchPath = DefaultSymbolSource;

                if (options.UseSymbolCache) {
                    var cachePath = options.HasSymbolCachePath ? options.SymbolCachePath : DefaultSymbolCachePath;
                    defaultSearchPath = $"CACHE*{cachePath};{defaultSearchPath}";
                }
            }

            var userSearchPath = "";

            foreach (var path in options.SymbolSearchPaths) {
                if (!string.IsNullOrEmpty(path)) {
                    userSearchPath += $"{path};";
                }
            }

            // Give priority to the user locations.
            var symbolSearchPath = $"{userSearchPath}{defaultSearchPath}";
            return symbolSearchPath;
        }

        public static async Task<string> LocateDebugInfoFile(string imagePath, SymbolFileSourceOptions options) {
            using var binaryInfo = new PEBinaryInfoProvider(imagePath);

            if (binaryInfo.Initialize()) {
                return await LocateDebugInfoFile(binaryInfo.SymbolFileInfo, options).ConfigureAwait(false);
            }

            return null;
        }

        public bool LoadDebugInfo(string debugFilePath) {
            try {
                debugFilePath_ = debugFilePath;
                diaSource_ = new DiaSourceClass();
                diaSource_.loadDataFromPdb(debugFilePath);
                diaSource_.openSession(out session_);
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed to load debug file {debugFilePath}: {ex.Message}");
                return false;
            }

            try {
                session_.findChildren(null, SymTagEnum.SymTagExe, null, 0, out var exeSymEnum);
                globalSymbol_ = exeSymEnum.Item(0);
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed to locate global sym for file {debugFilePath}: {ex.Message}");
                return false;
            }

            return true;
        }

        public bool AnnotateSourceLocations(FunctionIR function, IRTextFunction textFunc) {
            return AnnotateSourceLocations(function, textFunc.Name);
        }

        public bool AnnotateSourceLocations(FunctionIR function, string functionName) {
            var metadataTag = function.GetTag<AssemblyMetadataTag>();

            if (metadataTag == null) {
                return false;
            }

            var funcSymbol = FindFunctionSymbol(functionName);

            if (funcSymbol == null) {
                return false;
            }

            uint funcRVA = funcSymbol.relativeVirtualAddress;

            foreach (var pair in metadataTag.OffsetToElementMap) {
                uint instrRVA = funcRVA + (uint)pair.Key;
                AnnotateInstructionSourceLocation(pair.Value, instrRVA, funcSymbol);
            }

            return true;
        }

        public DebugFunctionSourceFileInfo FindFunctionSourceFilePath(IRTextFunction textFunc) {
            return FindFunctionSourceFilePath(textFunc.Name);
        }

        public DebugFunctionSourceFileInfo FindSourceFilePathByRVA(long rva) {
            // Find the first line in the function.
            var (lineInfo, sourceFile) = FindSourceLineByRVAImpl(rva);
            return FindFunctionSourceFilePathImpl(lineInfo, sourceFile, (uint)rva);
        }

        //? TODO: file selected in source panel should also be checked
        //? TODO: Integrate with SourceFileMapper
        public DebugFunctionSourceFileInfo FindFunctionSourceFilePath(string functionName) {
            var funcSymbol = FindFunctionSymbol(functionName);

            if (funcSymbol == null) {
                return DebugFunctionSourceFileInfo.Unknown;
            }

            // Find the first line in the function.
            var (lineInfo, sourceFile) = FindSourceLineByRVAImpl(funcSymbol.relativeVirtualAddress);
            return FindFunctionSourceFilePathImpl(lineInfo, sourceFile, funcSymbol.relativeVirtualAddress);
        }

        private DebugFunctionSourceFileInfo FindFunctionSourceFilePathImpl(DebugSourceLineInfo lineInfo,
            IDiaSourceFile sourceFile, uint rva) {
            if (lineInfo.IsUnknown) {
                return DebugFunctionSourceFileInfo.Unknown;
            }

            var originalFilePath = lineInfo.FilePath;
            var localFilePath = lineInfo.FilePath;
            bool localFileFound = File.Exists(localFilePath);
            bool hasChecksumMismatch = false;

            if (localFileFound) {
                // Check if the PDB file checksum matches the one of the local file.
                hasChecksumMismatch = !SourceFileChecksumMatchesPDB(sourceFile, localFilePath);
            }

            // Try to use the source server if no exact local file found.
            if ((!localFileFound || hasChecksumMismatch) && options_.SourceServerEnabled) {
                using var logWriter = new StringWriter();
                using var symbolReader = new SymbolReader(logWriter);
                using var pdb = symbolReader.OpenNativeSymbolFile(debugFilePath_);
                var sourceLine = pdb.SourceLocationForRva(rva);

                Trace.WriteLine($"Query source server for {sourceLine?.SourceFile?.BuildTimeFilePath}");

                if (sourceLine?.SourceFile != null) {
                    if (options_.HasAuthorizationToken) {
                        // SourceLink HTTP personal authentication token.
                        var token = string.Format("{0}:{1}", "", options_.AuthorizationToken);
                        var tokenB64 = Convert.ToBase64String(ASCIIEncoding.ASCII.GetBytes(token));
                        pdb.SymbolReader.AuthorizationHeaderForSourceLink = $"Basic {tokenB64}";
                    }

                    // Download the source file.
                    // The checksum should match, but do a check just in case.
                    var filePath = sourceLine.SourceFile.GetSourceFile();

                    if (File.Exists(filePath)) {
                        Trace.WriteLine($" - downloded {filePath}");
                        localFilePath = filePath;
                        hasChecksumMismatch = !SourceFileChecksumMatchesPDB(sourceFile, localFilePath);
                    }
                }
            }

            return new DebugFunctionSourceFileInfo(localFilePath, originalFilePath, lineInfo.Line, hasChecksumMismatch);
        }

        private bool SourceFileChecksumMatchesPDB(IDiaSourceFile sourceFile, string filePath) {
            var hashAlgo = GetSourceFileChecksumHashAlgorithm(sourceFile);
            var pdbChecksum = GetSourceFileChecksum(sourceFile);
            var fileChecksum = ComputeSourceFileChecksum(filePath, hashAlgo);
            return pdbChecksum != null && fileChecksum != null &&
                   Enumerable.SequenceEqual(pdbChecksum, fileChecksum);
        }

        public DebugSourceLineInfo FindSourceLineByRVA(long rva) {
            return FindSourceLineByRVAImpl(rva).Item1;
        }

        private (DebugSourceLineInfo, IDiaSourceFile) FindSourceLineByRVAImpl(long rva) {
            try {
                session_.findLinesByRVA((uint)rva, 1, out var lineEnum);

                while (true) {
                    lineEnum.Next(1, out var lineNumber, out var retrieved);

                    if (retrieved == 0) {
                        break;
                    }

                    var sourceFile = lineNumber.sourceFile;
                    return (new DebugSourceLineInfo(lineNumber.addressOffset, (int)lineNumber.lineNumber,
                                             (int)lineNumber.columnNumber, sourceFile.fileName), sourceFile);
                }
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed to get line for RVA {rva}: {ex.Message}");
            }

            return (DebugSourceLineInfo.Unknown, null);
        }

        private HashAlgorithm GetSourceFileChecksumHashAlgorithm(IDiaSourceFile sourceFile) {
            return sourceFile.checksumType switch {
                1 => System.Security.Cryptography.MD5.Create(),
                2 => System.Security.Cryptography.SHA1.Create(),
                3 => System.Security.Cryptography.SHA256.Create(),
                _ => System.Security.Cryptography.MD5.Create()
            };
        }

        private unsafe byte[] GetSourceFileChecksum(IDiaSourceFile sourceFile) {
            // Call once to get the size of the hash.
            uint hashSizeInBytes;
            byte* dummy = null;
            sourceFile.get_checksum(0, out hashSizeInBytes, out *dummy);

            // Allocate buffer and get the actual hash.
            var hash = new byte[hashSizeInBytes];
            uint bytesFetched;

            fixed (byte* bufferPtr = hash) {
                sourceFile.get_checksum((uint)hash.Length, out bytesFetched, out *bufferPtr);
            }

            return hash;
        }

        private byte[] ComputeSourceFileChecksum(string sourceFile, HashAlgorithm hashAlgo) {
            try {
                using var stream = File.OpenRead(sourceFile);
                return hashAlgo.ComputeHash(stream);
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed to compute hash for {sourceFile}: {ex.Message}");
                return Array.Empty<byte>();
            }
        }

        public DebugFunctionInfo FindFunctionByRVA(long rva) {
            try {
                session_.findSymbolByRVA((uint)rva, SymTagEnum.SymTagFunction, out var funcSym);

                if(funcSym != null) {
                    return new DebugFunctionInfo(funcSym.name, funcSym.relativeVirtualAddress, (long)funcSym.length);
                }

                // Do another lookup as a public symbol.
                session_.findSymbolByRVA((uint)rva, SymTagEnum.SymTagPublicSymbol, out var funcSym2);

                if (funcSym2 != null) {
                    return new DebugFunctionInfo(funcSym2.name, funcSym2.relativeVirtualAddress, (long)funcSym2.length);
                }
            }
            catch (Exception ex) { 
                Trace.TraceError($"Failed to find function for RVA {rva}: {ex.Message}");
            }

            return DebugFunctionInfo.Unknown;
        }

        public DebugFunctionInfo FindFunction(string functionName) {
            var funcSym = FindFunctionSymbol(functionName);

            if (funcSym != null) {
                return new DebugFunctionInfo(funcSym.name, funcSym.relativeVirtualAddress, (long)funcSym.length);
            }

            return DebugFunctionInfo.Unknown;
        }

        private bool AnnotateInstructionSourceLocation(IRElement instr, uint instrRVA, IDiaSymbol funcSymbol) {
            try {
                session_.findLinesByRVA(instrRVA, 0, out var lineEnum);

                while (true) {
                    lineEnum.Next(1, out var lineNumber, out var retrieved);

                    if (retrieved == 0) {
                        break;
                    }

                    var locationTag = instr.GetOrAddTag<SourceLocationTag>();
                    locationTag.Reset(); // Tag may be already populated.
                    locationTag.Line = (int)lineNumber.lineNumber;
                    locationTag.Column = (int)lineNumber.columnNumber;

                    funcSymbol.findInlineFramesByRVA(instrRVA, out var inlineeFrameEnum);

                    foreach (IDiaSymbol inlineFrame in inlineeFrameEnum) {
                        inlineFrame.findInlineeLinesByRVA(instrRVA, 0, out var inlineeLineEnum);

                        while (true) {
                            inlineeLineEnum.Next(1, out var inlineeLineNumber, out var inlineeRetrieved);

                            if (inlineeRetrieved == 0) {
                                break;
                            }

                            // Getting the source file of the inlinee often fails, ignore it.
                            string inlineeFileName = "";

                            try {
                                inlineeFileName = inlineeLineNumber.sourceFile.fileName;
                            }
                            catch (Exception _) {
                                
                            }

                            locationTag.AddInlinee(inlineFrame.name, inlineeFileName,
                                                   (int)inlineeLineNumber.lineNumber,
                                                   (int)inlineeLineNumber.columnNumber);
                        }
                    }

                }
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed to get source lines for {funcSymbol.name}: {ex.Message}");
                return false;
            }

            return true;
        }
        
        public IEnumerable<DebugFunctionInfo> EnumerateFunctions(bool includeExternal) {
            if (cachedFunctionList_ != null) {
                foreach (var entry in cachedFunctionList_) {
                    yield return entry;
                }
            }
            else {
                cachedFunctionList_ = new List<DebugFunctionInfo>();
                IDiaEnumSymbols symbolEnum;

                try {
                    globalSymbol_.findChildren(SymTagEnum.SymTagFunction, null, 0, out symbolEnum);
                }
                catch (Exception ex) {
                    Trace.TraceError($"Failed to enumerate functions: {ex.Message}");
                    yield break;
                }

                foreach (IDiaSymbol sym in symbolEnum) {
                    //Trace.WriteLine($" FuncSym {sym.name}: RVA {sym.relativeVirtualAddress:X}, size {sym.length}");
                    var funcInfo = new DebugFunctionInfo(sym.name, sym.relativeVirtualAddress, (long)sym.length);
                    cachedFunctionList_.Add(funcInfo);
                    yield return funcInfo;
                }
            }

            if (includeExternal) {
                IDiaEnumSymbols publicSymbolEnum;

                try {
                    globalSymbol_.findChildren(SymTagEnum.SymTagPublicSymbol, null, 0, out publicSymbolEnum);
                }
                catch (Exception ex) {
                    Trace.TraceError($"Failed to enumerate functions: {ex.Message}");
                    yield break;
                }

                foreach (IDiaSymbol sym in publicSymbolEnum) {
                    //Trace.WriteLine($" PublicSym {sym.name}: RVA {sym.relativeVirtualAddress:X} size {sym.length}");
                    yield return new DebugFunctionInfo(sym.name, sym.relativeVirtualAddress, (long)sym.length);
                }
            }
        }

        private IDiaSymbol FindFunctionSymbol(string functionName) {
            var demangledName = DemangleFunctionName(functionName, FunctionNameDemanglingOptions.Default);
            var queryDemangledName = DemangleFunctionName(functionName, FunctionNameDemanglingOptions.OnlyName);
            var result = FindFunctionSymbolImpl(SymTagEnum.SymTagFunction, functionName, demangledName, queryDemangledName);

            if (result != null) {
                return result;
            }

            return FindFunctionSymbolImpl(SymTagEnum.SymTagPublicSymbol, functionName, demangledName, queryDemangledName);
        }

        private IDiaSymbol FindFunctionSymbolImpl(SymTagEnum symbolType, string functionName, string demangledName, string queryDemangledName) {
            try {
                globalSymbol_.findChildren(symbolType, queryDemangledName, 0, out var symbolEnum);
                IDiaSymbol candidateSymbol = null;

                while (true) {
                    symbolEnum.Next(1, out var symbol, out var retrieved);

                    if (retrieved == 0) {
                        break;
                    }

                    // Class::function matches, now check the entire unmangled name
                    // to pick that right function overload.
                    candidateSymbol ??= symbol;
                    symbol.get_undecoratedNameEx((uint)NativeMethods.UnDecorateFlags.UNDNAME_NO_ACCESS_SPECIFIERS, out var symbolDemangledName);

                    if (symbolDemangledName == demangledName) {
                        return symbol;
                    }
                }

                return candidateSymbol; // Return first match.
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed to get function symbol for {functionName}: {ex.Message}");
                return null;
            }
        }

        private const int MaxDemangledFunctionNameLength = 8192;

        public static string DemangleFunctionName(string name, FunctionNameDemanglingOptions options =
                FunctionNameDemanglingOptions.Default) {
            var sb = new StringBuilder(MaxDemangledFunctionNameLength);
            NativeMethods.UnDecorateFlags flags = NativeMethods.UnDecorateFlags.UNDNAME_COMPLETE;
            flags |= NativeMethods.UnDecorateFlags.UNDNAME_NO_ACCESS_SPECIFIERS |
                     NativeMethods.UnDecorateFlags.UNDNAME_NO_ALLOCATION_MODEL |
                     NativeMethods.UnDecorateFlags.UNDNAME_NO_MEMBER_TYPE;

            if (options.HasFlag(FunctionNameDemanglingOptions.OnlyName)) {
                flags |= NativeMethods.UnDecorateFlags.UNDNAME_NAME_ONLY;
            }

            if (options.HasFlag(FunctionNameDemanglingOptions.NoSpecialKeywords)) {
                flags |= NativeMethods.UnDecorateFlags.UNDNAME_NO_MS_KEYWORDS;
                flags |= NativeMethods.UnDecorateFlags.UNDNAME_NO_MS_THISTYPE;
            }

            if (options.HasFlag(FunctionNameDemanglingOptions.NoReturnType)) {
                flags |= NativeMethods.UnDecorateFlags.UNDNAME_NO_FUNCTION_RETURNS;
            }

            NativeMethods.UnDecorateSymbolName(name, sb, MaxDemangledFunctionNameLength, flags);
            return sb.ToString();
        }

        public static string DemangleFunctionName(IRTextFunction function, FunctionNameDemanglingOptions options =
                FunctionNameDemanglingOptions.Default) {
            return DemangleFunctionName(function.Name, options);
        }

        public void Dispose() {
            if (session_ != null) {
                Marshal.ReleaseComObject(session_);
            }

            if (diaSource_ != null) {
                Marshal.ReleaseComObject(diaSource_);
            }
        }
    }
}


//? TODO: Remove unused code, replaced by TraceEvent
//public static class SymSrvHelpers {
//    private static int processId_ = Process.GetCurrentProcess().Id;
//    private static bool initialized_;
//    private static string currentSymbolPath_;
//    private static object lockObject_ = new object();

//    public static bool InitSymSrv(string symbolPath) {
//        lock (lockObject_) {
//            if (initialized_) {
//                if (currentSymbolPath_ == symbolPath) {
//                    return true;
//                }

//                CleanupSymSrv();
//            }

//            //symbolPath = @"srv*https://symweb";

//            if (NativeMethods.SymInitialize((IntPtr)processId_, symbolPath, false)) {
//                //NativeMethods.SymSetOptions(NativeMethods.SYMOPT_EXACT_SYMBOLS);
//                initialized_ = true;
//                currentSymbolPath_ = symbolPath;
//                return true;
//            }
//        }

//        return false;
//    }

//    private static bool CleanupSymSrv() {
//        if (initialized_) {
//            initialized_ = false;
//            return NativeMethods.SymCleanup((IntPtr)processId_);
//        }

//        return true;
//    }

//    /// Private method to locate the local path for a matching PDB. Implicitly handles symbol download if needed.
//    public static string GetLocalPDBFilePath(string pdbFilename, Guid guid, int pdbAge) {
//        const int MAX_PATH = 4096;
//        StringBuilder outPath = new StringBuilder(MAX_PATH);
//        IntPtr buffer = Marshal.AllocHGlobal(Marshal.SizeOf(guid));
//        Marshal.StructureToPtr(guid, buffer, false);

//        try {
//            if (!NativeMethods.SymFindFileInPath((IntPtr)processId_, null, pdbFilename, buffer, pdbAge, 0, 8,
//                outPath, IntPtr.Zero, IntPtr.Zero)) {
//                return null;
//            }
//        }
//        finally {
//            Marshal.FreeHGlobal(buffer);
//        }

//        Trace.WriteLine($"=> Found PDB for {pdbFilename} as {outPath.ToString()}, Guid {guid}");

//        return outPath.ToString();
//    }

//    public static string GetLocalImageFilePath(BinaryFileDescription binaryInfo) {
//        return GetLocalImageFilePath(binaryInfo.ImageName, 
//                                     (int)binaryInfo.TimeStamp, 
//                                     (int)binaryInfo.ImageSize);
//    }

//    public static string GetLocalImageFilePath(string imageFilename, int imageTimestamp, int imageSize) {
//        const int MAX_PATH = 4096;
//        StringBuilder outPath = new StringBuilder(MAX_PATH);
//        IntPtr buffer = Marshal.AllocHGlobal(4);
//        Marshal.WriteInt32(buffer, imageTimestamp);

//        try {
//            if (!NativeMethods.SymFindFileInPath((IntPtr)processId_, null, imageFilename,
//                buffer, imageSize, 0, 4, outPath, IntPtr.Zero, IntPtr.Zero)) {
//                return null;
//            }
//        }
//        finally {
//            Marshal.FreeHGlobal(buffer);
//        }

//        return outPath.ToString();
//    }
//}