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
using OxyPlot;

namespace IRExplorerUI.Compilers {
    //? Provider ASM should return instance instead of JSONDebug
    public class DotNetDebugInfoProvider : IDebugInfoProvider {
        private Dictionary<string, DebugFunctionInfo> functionMap_;
        private List<DebugFunctionInfo> functions_;
        private Machine architecture_;
        private Dictionary<DebugFunctionInfo, List<(int ILOffset, int NativeOffset)>> methodILNativeMap_;

        public DotNetDebugInfoProvider(Machine architecture) {
            architecture_ = architecture;
            functionMap_ = new Dictionary<string, DebugFunctionInfo>();
            functions_ = new List<DebugFunctionInfo>();
            methodILNativeMap_ = new Dictionary<DebugFunctionInfo, List<(int ILOffset, int NativeOffset)>>();
        }
        
        public Machine? Architecture => architecture_;
        public SymbolFileSourceOptions SymbolOptions { get; set;  }
        public SymbolFileDescriptor ManagedSymbolFile { get; set; }

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
            return true;
        }


        public void AddMethodILToNativeMap(DebugFunctionInfo debugInfo, List<(int ILOffset, int NativeOffset)> ilOffsets) {
            methodILNativeMap_[debugInfo] = ilOffsets;
        }
    }
}
