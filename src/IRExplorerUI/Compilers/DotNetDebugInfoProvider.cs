// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Threading.Tasks;
using IRExplorerCore;
using IRExplorerCore.IR;
using IRExplorerCore.IR.Tags;
using IRExplorerCore.Utilities;
using Microsoft.Diagnostics.Runtime;
using Microsoft.CodeAnalysis;
using Microsoft.Diagnostics.Symbols;

namespace IRExplorerUI.Compilers {
    //? Provider ASM should return instance instead of JSONDebug
    public class DotNetDebugInfoProvider : IDebugInfoProvider {
        public class AddressNamePair {
            public long Address { get; set; }
            public string Name { get; set; }
        }

        class ManagedProcessCode {
            public int ProcessId { get; set; }
            public int MachineType { get; set; }
            public List<MethodCode> Methods { get; set; }
        }

        public class MethodCode {
            public long Address { get; set; }
            public int Size { get; set; }
            public string CodeB64 { get; set; }
            public List<AddressNamePair> CallTargets { get; set; }

            public byte[] GetCodeBytes() {
                try {
                    return Convert.FromBase64String(CodeB64);
                }
                catch (Exception ex) {
                    Trace.TraceError($"Failed to convert Base64: {ex.Message}\n{ex.StackTrace}");
                    return null;
                }
            }

            public string FindCallTarget(long address) {
                //? TODO: Map

                var index = CallTargets.FindIndex(item => item.Address == address);
                if (index != -1) {
                    return CallTargets[index].Name;
                }

                return null;
            }
        }
        
        private Dictionary<string, DebugFunctionInfo> functionMap_;
        private List<DebugFunctionInfo> functions_;
        private Machine architecture_;
        private Dictionary<DebugFunctionInfo, List<(int ILOffset, int NativeOffset)>> methodILNativeMap_;
        private Dictionary<long, MethodCode> methodCodeMap_;

        public DotNetDebugInfoProvider(Machine architecture) {
            architecture_ = architecture;
            functionMap_ = new Dictionary<string, DebugFunctionInfo>();
            functions_ = new List<DebugFunctionInfo>();
            methodILNativeMap_ = new Dictionary<DebugFunctionInfo, List<(int ILOffset, int NativeOffset)>>();
        }
        
        public Machine? Architecture => architecture_;
        public SymbolFileSourceOptions SymbolOptions { get; set;  }
        public SymbolFileDescriptor ManagedSymbolFile { get; set; }
        public string ManagedAsmFilePath { get; set; }

        public MethodCode FindMethodCode(DebugFunctionInfo funcInfo) {
            return methodCodeMap_?.GetValueOrNull(funcInfo.RVA);
        }

        public void AddFunctionInfo(DebugFunctionInfo funcInfo) {
            functions_.Add(funcInfo);
            functionMap_[funcInfo.Name] = funcInfo;
        }

        public bool AnnotateSourceLocations(FunctionIR function, IRTextFunction textFunc) {
            return AnnotateSourceLocations(function, textFunc.Name);
        }

        private bool EnsureHasSourceLines(DebugFunctionInfo debugInfo) {
            if (debugInfo == null) {
                return false;
            }
            else if (debugInfo.HasSourceLines) {
                return true;
            }
            else if (ManagedSymbolFile == null) {
                return false;
            }

            //? TODO: Make async
            var options = SymbolOptions != null ? SymbolOptions :
                          (SymbolFileSourceOptions)App.Settings.SymbolOptions.Clone();
            options.InsertSymbolPath(Utils.TryGetDirectoryName(ManagedSymbolFile.FileName));

            var symbolSearchPath = PDBDebugInfoProvider.ConstructSymbolSearchPath(options);
            using var symbolReader = new SymbolReader(TextWriter.Null, symbolSearchPath);
            var debugFile = symbolReader.FindSymbolFilePath(ManagedSymbolFile.FileName, ManagedSymbolFile.Id, ManagedSymbolFile.Age);

            if (!File.Exists(debugFile)) {
                return debugInfo.HasSourceLines;
            }

            lock (debugInfo) {
                if (!methodILNativeMap_.TryGetValue(debugInfo, out var ilOffsets)) {
                    return debugInfo.HasSourceLines;
                }

                try {
                    using var stream = File.OpenRead(debugFile);
                    var mdp = MetadataReaderProvider.FromPortablePdbStream(stream);
                    var md = mdp.GetMetadataReader();
                    var debugHandle = MetadataTokens.MethodDebugInformationHandle((int)debugInfo.Id);

                    var managedDebugInfo = md.GetMethodDebugInformation(debugHandle);
                    var sequencePoints = managedDebugInfo.GetSequencePoints();

                    foreach (var pair in ilOffsets) {
                        int closestDist = int.MaxValue;
                        SequencePoint? closestPoint = null;

                        // Search for exact or closes IL offset based on
                        //? TODO: Slow, use map combined with bin search since most ILoffsets are found exactly
                        foreach (var point in sequencePoints) {
                            if (point.Offset == pair.ILOffset) {
                                closestPoint = point;
                                closestDist = 0;
                                break;
                            }

                            int dist = Math.Abs(point.Offset - pair.ILOffset);

                            if (dist < closestDist) {
                                closestDist = dist;
                                closestPoint = point;
                            }
                        }

                        if (closestPoint.HasValue) {
                            //Trace.WriteLine($"Using closest {closestPoint.Value.StartLine}");
                            var doc = md.GetDocument(closestPoint.Value.Document);
                            var docName = md.GetString(doc.Name);
                            var lineInfo = new DebugSourceLineInfo(pair.NativeOffset, closestPoint.Value.StartLine,
                                closestPoint.Value.StartColumn, docName);
                            debugInfo.AddSourceLine(lineInfo);
                        }
                    }
                }
                catch (Exception ex) {
                    Trace.TraceError($"Failed to read managed PDB from {debugFile}: {ex.Message}\n{ex.StackTrace}");
                }

                return debugInfo.HasSourceLines;
            }
        }

        public bool AnnotateSourceLocations(FunctionIR function, string functionName) {
            var metadataTag = function.GetTag<AssemblyMetadataTag>();

            if (metadataTag == null) {
                return false;
            }

            var funcInfo = FindFunction(functionName);

            if (!EnsureHasSourceLines(funcInfo)) {
                return false;
            }

            foreach (var pair in metadataTag.OffsetToElementMap) {
                var lineInfo = funcInfo.FindNearestLine(pair.Key);

                if(!lineInfo.IsUnknown) {
                    var locationTag = pair.Value.GetOrAddTag<SourceLocationTag>();
                    locationTag.Reset(); // Tag may be already populated.
                    locationTag.Line = lineInfo.Line;
                    locationTag.Column = lineInfo.Column;
                }
            }

            return true;
        }
        public DebugFunctionInfo FindFunction(string functionName) {
            return functionMap_.GetValueOrDefault(functionName);
        }

        public void Dispose() {
            
        }

        public IEnumerable<DebugFunctionInfo> EnumerateFunctions(bool includeExternal) {
            return functions_;
        }

        public DebugFunctionInfo FindFunctionByRVA(long rva) {
            //? TODO: BinSearch
            foreach (var func in functions_) {
                if (rva >= func.StartRVA  && rva < func.EndRVA) {
                    return func;
                }
            }

            return null;
        }

        public DebugFunctionSourceFileInfo FindFunctionSourceFilePath(IRTextFunction textFunc) {
            return FindFunctionSourceFilePath(textFunc.Name);
        }

        public DebugFunctionSourceFileInfo FindFunctionSourceFilePath(string functionName) {
            if (functionMap_.TryGetValue(functionName, out var funcInfo)) {
                return GetSourceFileInfo(funcInfo);
            }

            return DebugFunctionSourceFileInfo.Unknown;
        }

        private static DebugFunctionSourceFileInfo GetSourceFileInfo(DebugFunctionInfo info)
        {
            return new DebugFunctionSourceFileInfo(info.StartDebugSourceLine.FilePath,
                info.StartDebugSourceLine.FilePath,
                info.StartDebugSourceLine.Line);
        }

        public DebugFunctionSourceFileInfo FindSourceFilePathByRVA(long rva) {
            var funcInfo = FindFunctionByRVA(rva);

            if (EnsureHasSourceLines(funcInfo)) {
                return GetSourceFileInfo(funcInfo);
            }

            return DebugFunctionSourceFileInfo.Unknown;
        }

        public DebugSourceLineInfo FindSourceLineByRVA(long rva) {
            var funcInfo = FindFunctionByRVA(rva);

            if (EnsureHasSourceLines(funcInfo)) {
                long offset = rva - funcInfo.StartRVA;
                return funcInfo.FindNearestLine(offset);
            }

            return DebugSourceLineInfo.Unknown;
        }
        
        public bool LoadDebugInfo(string debugFilePath) {
            if (!File.Exists(ManagedAsmFilePath)) {
                Trace.TraceError($"Missing managed ASM file: {ManagedAsmFilePath}");
                return true;
            }

            ManagedProcessCode processCode;

            if (!JsonUtils.DeserializeFromFile(ManagedAsmFilePath, out processCode)) {
                return true;
            }

            architecture_ = (Machine)processCode.MachineType;
            methodCodeMap_ = new Dictionary<long, MethodCode>(processCode.Methods.Count);

            foreach (var method in processCode.Methods) {
                methodCodeMap_[method.Address] = method;
            }

            return true;
        }


        public void AddMethodILToNativeMap(DebugFunctionInfo debugInfo, List<(int ILOffset, int NativeOffset)> ilOffsets) {
            methodILNativeMap_[debugInfo] = ilOffsets;
        }
    }
}
