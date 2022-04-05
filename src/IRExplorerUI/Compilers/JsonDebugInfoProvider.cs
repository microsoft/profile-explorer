// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using IRExplorerCore;
using IRExplorerCore.IR;
using IRExplorerCore.IR.Tags;
using IRExplorerCore.Utilities;

namespace IRExplorerUI.Compilers {
    public class JsonDebugInfoProvider : IDebugInfoProvider {
        private Dictionary<string, DebugFunctionInfo> functionMap_;
        private List<DebugFunctionInfo> functions_;
        
        public bool AnnotateSourceLocations(FunctionIR function, IRTextFunction textFunc) {
            return AnnotateSourceLocations(function, textFunc.Name);
        }

        public bool AnnotateSourceLocations(FunctionIR function, string functionName) {
            var metadataTag = function.GetTag<AssemblyMetadataTag>();

            if (metadataTag == null) {
                return false;
            }

            var funcInfo = FindFunction(functionName);

            if (funcInfo.IsUnknown || !funcInfo.HasSourceLines) {
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
            return functionMap_.GetValueOr(functionName, DebugFunctionInfo.Unknown);
        }

        public void Dispose() {
            
        }

        public IEnumerable<DebugFunctionInfo> EnumerateFunctions() {
            return functions_;
        }

        public DebugFunctionInfo FindFunctionByRVA(long rva) {
            foreach (var func in functions_) {
                if (func.StartRVA >= rva && func.EndRVA < rva) {
                    return func;
                }
            }

            return DebugFunctionInfo.Unknown;
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

            if (!funcInfo.IsUnknown && funcInfo.HasSourceLines) {
                return GetSourceFileInfo(funcInfo);
            }

            return DebugFunctionSourceFileInfo.Unknown;
        }

        public DebugSourceLineInfo FindSourceLineByRVA(long rva) {
            var funcInfo = FindFunctionByRVA(rva);

            if (!funcInfo.IsUnknown && funcInfo.HasSourceLines) {
                long offset = rva - funcInfo.StartRVA;
                return funcInfo.FindNearestLine(offset);
            }

            return DebugSourceLineInfo.Unknown;
        }
        
        public bool LoadDebugInfo(string debugFilePath) {
            if (!JsonUtils.DeserializeFromFile(debugFilePath, out functions_)) {
                return false;
            }

            functionMap_ = new Dictionary<string, DebugFunctionInfo>(functions_.Count);

            foreach (var func in functions_) {
                functionMap_[func.Name] = func;
            }

            return true;
        }
    }
}
