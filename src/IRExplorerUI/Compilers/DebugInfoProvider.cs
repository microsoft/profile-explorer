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
    
    //? Merge with PDB cvdump reader for ETW
    //? TODO: Move global symbol load as member done once
    //? Use for-each iterators
    //? Free more COM

    public class DebugInfoProvider : IDisposable {
        private IDiaDataSource diaSource_;
        private IDiaSession session_;

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

            return true;
        }

        public bool AnnotateSourceLocations(FunctionIR function, IRTextFunction textFunc) {
            return AnnotateSourceLocations(function, textFunc.Name);
        }

        public bool AnnotateSourceLocations(FunctionIR function, string functionName) {
            var metadataTag = function.GetTag<AssemblyMetadataTag>();

            if(metadataTag == null) {
                return false;
            }

            var funcSymbol = FindFunction(functionName);

            if (funcSymbol == null) {
                return false;
            }

            uint funcRVA = funcSymbol.relativeVirtualAddress;

            foreach (var pair in metadataTag.OffsetToElementMap) {
                uint instrRVA = funcRVA + (uint)pair.Key;
                uint instrLength = (uint)metadataTag.ElementSizeMap[pair.Value];
                AnnotateInstructionSourceLocation(pair.Value, instrRVA, instrLength, funcSymbol);
            }

            return true;
        }

        public (string, int) FindFunctionSourceFilePath(IRTextFunction textFunc) {
            return FindFunctionSourceFilePath(textFunc.Name);
        }
        
        public (string, int) FindFunctionSourceFilePath(string functionName) {
            var funcSymbol = FindFunction(functionName);

            if (funcSymbol == null) {
                return (null, 0);
            }

            try {
                session_.findLinesByRVA(funcSymbol.relativeVirtualAddress, (uint)funcSymbol.length, out var lineEnum);

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
                return (null, 0);
            }

            return (null, 0);
        }

        private bool AnnotateInstructionSourceLocation(IRElement instr, uint instrRVA, uint instrLength,
                                                       IDiaSymbol funcSymbol) {
            try {
                session_.findLinesByRVA(instrRVA, 0, out var lineEnum);

                while(true) {
                    lineEnum.Next(1, out var lineNumber, out var retrieved);

                    if(retrieved == 0) {
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

                            session_.findSymbolByRVA(inlineeLineNumber.relativeVirtualAddress, SymTagEnum.SymTagFunction, out var inlineeSymbol);

                            locationTag.AddInlinee(inlineFrame.name, inlineeLineNumber.sourceFile.fileName,
                                                   (int)inlineeLineNumber.lineNumber,
                                                   (int)inlineeLineNumber.columnNumber);
                        }
                    }
                  
                }
            }
            catch (Exception ex) {
                return false;
            }

            return true;
        }

        private IDiaSymbol FindFunction(string functionName) {
            try {
                session_.findChildren(null, SymTagEnum.SymTagExe, null, 0, out var exeSymEnum);
                var globalSymbol = exeSymEnum.Item(0);

                var demangledName = DemangleFunctionName(functionName, FunctionNameDemanglingOptions.Default);
                var queryDemangledName = DemangleFunctionName(functionName, FunctionNameDemanglingOptions.OnlyName);
                globalSymbol.findChildren(SymTagEnum.SymTagFunction, queryDemangledName, 0, out var symbolEnum);
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
