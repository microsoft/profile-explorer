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
using System.Runtime.InteropServices;

namespace IRExplorerUI.Compilers {

    //? https://stackoverflow.com/questions/34960989/how-can-i-hide-dll-registration-message-window-in-my-application

    //? TODO: Move global symbol load as member done once
    //? Use for-each iterators
    //? Free more COM

    public static class SymSrvHelpers {
        //? TODO: Not thread safe
        private static int processId_ = Process.GetCurrentProcess().Id;
        private static bool initialized_;

        public static bool InitSymSrv(string symbolPath) {
            if (initialized_) {
                CleanupSymSrv();
                initialized_ = false;
            }

            if (NativeMethods.SymInitialize((IntPtr)processId_, symbolPath, false)) {
                // Enable diagnostic printing to the debugger.
                NativeMethods.SymSetOptions(NativeMethods.SYMOPT_DEBUG);
                initialized_ = true;
                return true;
            }

            return false;
        }

        public static bool CleanupSymSrv() {
            return NativeMethods.SymCleanup((IntPtr)processId_);
        }

        /// Private method to locate the local path for a matching PDB. Implicitly handles symbol download if needed.
        public static string GetLocalPDBFilePath(string pdbFilename, Guid guid, int pdbAge) {
            const int MAX_PATH = 4096;
            StringBuilder outPath = new StringBuilder(MAX_PATH);
            IntPtr buffer = Marshal.AllocHGlobal(Marshal.SizeOf(guid));
            Marshal.StructureToPtr(guid, buffer, false);

            try {
                if (!NativeMethods.SymFindFileInPath((IntPtr)processId_, null, pdbFilename, buffer, pdbAge, 0, 8,
                    outPath, IntPtr.Zero, IntPtr.Zero)) {
                    return null;
                }
            }
            finally {
                Marshal.FreeHGlobal(buffer);
            }

            return outPath.ToString();
        }

        public static string GetLocalImageFilePath(string imageFilename, int imageTimestamp, int imageSize) {
            const int MAX_PATH = 4096;
            StringBuilder outPath = new StringBuilder(MAX_PATH);
            IntPtr buffer = Marshal.AllocHGlobal(4);
            Marshal.WriteInt32(buffer, imageTimestamp);

            try {
                if (!NativeMethods.SymFindFileInPath((IntPtr)processId_, null, imageFilename,
                    buffer, imageSize, 0, 2, outPath, IntPtr.Zero, IntPtr.Zero)) {
                    return null;
                }
            }
            finally {
                Marshal.FreeHGlobal(buffer);
            }

            return outPath.ToString();
        }
    }

    public class PDBDebugInfoProvider : IDisposable, IDebugInfoProvider {
        // https://docs.microsoft.com/en-us/windows-hardware/drivers/debugger/symbol-path
        private const string DefaultSymbolSource = @"SRV*https://msdl.microsoft.com/download/symbols";
        private const string DefaultSymbolCachePath = @"C:\Symbols";

        private IDiaDataSource diaSource_;
        private IDiaSession session_;
        private IDiaSymbol globalSymbol_;

        public static async Task<string> LocateDebugFile(SymbolFileDescriptor symbolFile, SymbolFileSourceOptions options) {
            var symbolSearchPath = options.UseDefaultSymbolSource ? DefaultSymbolSource : "";

            if (options.UseSymbolCache) {
                var cachePath = options.HasSymbolCachePath ? options.SymbolCachePath : DefaultSymbolCachePath;
                symbolSearchPath = $"CACHE*{cachePath};{symbolSearchPath}";
            }

            foreach(var path in options.SymbolSearchPaths) {
                if (!string.IsNullOrEmpty(path)) {
                    symbolSearchPath += $";{path}";
                }
            }

            if (!SymSrvHelpers.InitSymSrv(symbolSearchPath)) {
                return null;
            }

            return await Task.Run(() =>
                SymSrvHelpers.GetLocalPDBFilePath(symbolFile.FileName, symbolFile.Id, symbolFile.Age));
        }

        public static async Task<string> LocateDebugFile(string imagePath, SymbolFileSourceOptions options) {
            using var binaryInfo = new PEBinaryInfoProvider(imagePath);

            if (binaryInfo.Initialize()) {
                return await LocateDebugFile(binaryInfo.SymbolFileInfo, options);
            }

            return null;
        }

        public bool LoadDebugInfo(string debugFilePath) {
            try {
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

            var funcSymbol = FindFunction(functionName);

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

        public (string, int) FindFunctionSourceFilePath(IRTextFunction textFunc) {
            return FindFunctionSourceFilePath(textFunc.Name);
        }

        public (string, int) FindFunctionSourceFilePath(string functionName) {
            var funcSymbol = FindFunction(functionName);

            if (funcSymbol != null) {
                return FindFunctionSourceFilePathByRVA(funcSymbol.relativeVirtualAddress);
            }
            
            return (null, 0);
        }

        public int FindLineByRVA(long rva) {
            try {
                session_.findLinesByRVA((uint)rva, 1, out var lineEnum);

                while (true) {
                    lineEnum.Next(1, out var lineNumber, out var retrieved);

                    if (retrieved == 0) {
                        break;
                    }

                    var sourceFile = lineNumber.sourceFile;
                    return (int)lineNumber.lineNumber;
                }
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed to get line for RVA {rva}: {ex.Message}");
                return -1;
            }

            return -1;
        }

        public (string, int) FindFunctionSourceFilePathByRVA(long rva) {
            try {
                session_.findLinesByRVA((uint)rva, 1, out var lineEnum);

                while (true) {
                    lineEnum.Next(1, out var lineNumber, out var retrieved);

                    if (retrieved == 0) {
                        break;
                    }

                    var sourceFile = lineNumber.sourceFile;
                    return (sourceFile.fileName, (int)lineNumber.lineNumber);
                }
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed to get line for RVA {rva}: {ex.Message}");
                return (null, -1);
            }

            return (null, -1);
        }

        public (string, long) FindFunctionByRVA(long rva) {
            try {
                session_.findSymbolByRVA((uint)rva, SymTagEnum.SymTagFunction, out var funcSym);

                if(funcSym != null) {
                    return (funcSym.name, funcSym.relativeVirtualAddress);
                }

                // Do another lookup as a public symbol.
                session_.findSymbolByRVA((uint)rva, SymTagEnum.SymTagPublicSymbol, out var funcSym2);

                if (funcSym2 != null) {
                    return (funcSym2.name, funcSym2.relativeVirtualAddress);
                }
            }
            catch (Exception ex) { 
                Trace.TraceError($"Failed to find function for RVA {rva}: {ex.Message}");
            }
            return (null, 0);

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

        public IEnumerable<DebugFunctionInfo> EnumerateFunctions() {
            IDiaEnumSymbols symbolEnum;

            try {
                globalSymbol_.findChildren(SymTagEnum.SymTagFunction, null, 0, out symbolEnum);
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed to enumerate functions: {ex.Message}");
                yield break;
            }

            Trace.AutoFlush = false;

            foreach (IDiaSymbol sym in symbolEnum) {
                //Trace.WriteLine($" FuncSym {sym.name}: RVA {sym.relativeVirtualAddress:X}, size {sym.length}");
                yield return new DebugFunctionInfo(sym.name, sym.relativeVirtualAddress, (long)sym.length);
            }

            globalSymbol_.findChildren(SymTagEnum.SymTagPublicSymbol, null, 0, out var publicSymbolEnum);
         
            foreach (IDiaSymbol sym in publicSymbolEnum) {
                //Trace.WriteLine($" PublicSym {sym.name}: RVA {sym.relativeVirtualAddress:X} size {sym.length}");
                yield return new DebugFunctionInfo(sym.name, sym.relativeVirtualAddress, (long)sym.length);
            }

            Trace.AutoFlush = true;
        }

        private IDiaSymbol FindFunction(string functionName) {
            try {
                var demangledName = DemangleFunctionName(functionName, FunctionNameDemanglingOptions.Default);
                var queryDemangledName = DemangleFunctionName(functionName, FunctionNameDemanglingOptions.OnlyName);
                globalSymbol_.findChildren(SymTagEnum.SymTagFunction, queryDemangledName, 0, out var symbolEnum);
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
            flags |= NativeMethods.UnDecorateFlags.UNDNAME_NO_ACCESS_SPECIFIERS;

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
