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
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;
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
        
        private Dictionary<string, FunctionDebugInfo> functionMap_;
        private List<FunctionDebugInfo> functions_;
        private Machine architecture_;
        private Dictionary<FunctionDebugInfo, List<(int ILOffset, int NativeOffset)>> methodILNativeMap_;
        private Dictionary<long, MethodCode> methodCodeMap_;
        private bool hasManagedSymbolFileFailure_;

        public DotNetDebugInfoProvider(Machine architecture) {
            architecture_ = architecture;
            functionMap_ = new Dictionary<string, FunctionDebugInfo>();
            functions_ = new List<FunctionDebugInfo>();
            methodILNativeMap_ = new Dictionary<FunctionDebugInfo, List<(int ILOffset, int NativeOffset)>>();
        }
        
        public Machine? Architecture => architecture_;
        public SymbolFileSourceOptions SymbolOptions { get; set;  }
        public SymbolFileDescriptor ManagedSymbolFile { get; set; }
        public string ManagedAsmFilePath { get; set; }

        public MethodCode FindMethodCode(FunctionDebugInfo funcInfo) {
            return methodCodeMap_?.GetValueOrNull(funcInfo.RVA);
        }

        public void AddFunctionInfo(FunctionDebugInfo funcInfo) {
            functions_.Add(funcInfo);
            functionMap_[funcInfo.Name] = funcInfo;
        }

        public bool AnnotateSourceLocations(FunctionIR function, IRTextFunction textFunc) {
            return AnnotateSourceLocations(function, textFunc.Name);
        }

        private bool EnsureHasSourceLines(FunctionDebugInfo functionDebugInfo) {
            if (functionDebugInfo == null) {
                return false;
            }
            else if (functionDebugInfo.HasSourceLines) {
                return true;
            }
            else if (ManagedSymbolFile == null || hasManagedSymbolFileFailure_) {
                return false;
            }

            //? TODO: Make async
            var options = SymbolOptions != null ? SymbolOptions :
                          (SymbolFileSourceOptions)App.Settings.SymbolOptions.Clone();
            if (File.Exists(ManagedSymbolFile.FileName)) {
                options.InsertSymbolPath(ManagedSymbolFile.FileName);
            }

            var symbolSearchPath = PDBDebugInfoProvider.ConstructSymbolSearchPath(options);
            
            using var logWriter = new StringWriter();
            using var symbolReader = new SymbolReader(logWriter, symbolSearchPath);
            symbolReader.SecurityCheck += s => true; // Allow symbols from "unsafe" locations.
            var debugFile = symbolReader.FindSymbolFilePath(ManagedSymbolFile.FileName, ManagedSymbolFile.Id, ManagedSymbolFile.Age);

            Trace.WriteLine($">> TraceEvent FindSymbolFilePath for {ManagedSymbolFile.FileName}: {debugFile}");
            Trace.IndentLevel = 1;
            Trace.WriteLine(logWriter.ToString());
            Trace.IndentLevel = 0;
            Trace.WriteLine($"<< TraceEvent");

            if (!File.Exists(debugFile)) {
                // Don't try again if PDB not found.
                hasManagedSymbolFileFailure_ = true;
                return functionDebugInfo.HasSourceLines;
            }

            lock (functionDebugInfo) {
                if (!methodILNativeMap_.TryGetValue(functionDebugInfo, out var ilOffsets)) {
                    return functionDebugInfo.HasSourceLines;
                }

                try {
                    using var stream = File.OpenRead(debugFile);
                    var mdp = MetadataReaderProvider.FromPortablePdbStream(stream);
                    var md = mdp.GetMetadataReader();
                    var debugHandle = MetadataTokens.MethodDebugInformationHandle((int)functionDebugInfo.Id);

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
                            var lineInfo = new SourceLineDebugInfo(pair.NativeOffset, closestPoint.Value.StartLine,
                                closestPoint.Value.StartColumn, docName);
                            functionDebugInfo.AddSourceLine(lineInfo);
                        }
                    }
                }
                catch (Exception ex) {
                    Trace.TraceError($"Failed to read managed PDB from {debugFile}: {ex.Message}\n{ex.StackTrace}");
                    hasManagedSymbolFileFailure_ = true;
                }

                return functionDebugInfo.HasSourceLines;
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
        public FunctionDebugInfo FindFunction(string functionName) {
            return functionMap_.GetValueOrDefault(functionName);
        }

        public void Dispose() {
            
        }

        public IEnumerable<FunctionDebugInfo> EnumerateFunctions(bool includeExternal) {
            return functions_;
        }

        public FunctionDebugInfo FindFunctionByRVA(long rva) {
            //? TODO: BinSearch
            foreach (var func in functions_) {
                if (rva >= func.StartRVA  && rva < func.EndRVA) {
                    return func;
                }
            }

            return FunctionDebugInfo.Unknown;
        }

        public SourceFileDebugInfo FindFunctionSourceFilePath(IRTextFunction textFunc) {
            return FindFunctionSourceFilePath(textFunc.Name);
        }

        public SourceFileDebugInfo FindFunctionSourceFilePath(string functionName) {
            if (functionMap_.TryGetValue(functionName, out var funcInfo)) {
                return GetSourceFileInfo(funcInfo);
            }

            return SourceFileDebugInfo.Unknown;
        }

        private static SourceFileDebugInfo GetSourceFileInfo(FunctionDebugInfo info)
        {
            return new SourceFileDebugInfo(info.StartSourceLineDebug.FilePath,
                info.StartSourceLineDebug.FilePath,
                info.StartSourceLineDebug.Line);
        }

        public SourceFileDebugInfo FindSourceFilePathByRVA(long rva) {
            var funcInfo = FindFunctionByRVA(rva);

            if (EnsureHasSourceLines(funcInfo)) {
                return GetSourceFileInfo(funcInfo);
            }

            return SourceFileDebugInfo.Unknown;
        }

        public SourceLineDebugInfo FindSourceLineByRVA(long rva) {
            var funcInfo = FindFunctionByRVA(rva);

            if (EnsureHasSourceLines(funcInfo)) {
                long offset = rva - funcInfo.StartRVA;
                return funcInfo.FindNearestLine(offset);
            }

            return SourceLineDebugInfo.Unknown;
        }
        
        public bool LoadDebugInfo(string debugFilePath) {
            if (!File.Exists(ManagedAsmFilePath)) {
                Trace.TraceError($"Missing managed ASM file: {ManagedAsmFilePath}");
                return false;
            }

            ManagedProcessCode processCode;

            if (!JsonUtils.DeserializeFromFile(ManagedAsmFilePath, out processCode)) {
                return false;
            }

            architecture_ = (Machine)processCode.MachineType;
            methodCodeMap_ = new Dictionary<long, MethodCode>(processCode.Methods.Count);

            foreach (var method in processCode.Methods) {
                methodCodeMap_[method.Address] = method;
            }

            return true;
        }

        public bool LoadDebugInfo(DebugFileSearchResult debugFile) {
            return LoadDebugInfo(debugFile.FilePath);
        }

        public void AddMethodILToNativeMap(FunctionDebugInfo functionDebugInfo, List<(int ILOffset, int NativeOffset)> ilOffsets) {
            methodILNativeMap_[functionDebugInfo] = ilOffsets;
        }
    }
}
