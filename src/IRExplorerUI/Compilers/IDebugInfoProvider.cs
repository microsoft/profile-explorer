using System;
using System.Collections.Generic;
using System.Reflection.PortableExecutable;
using IRExplorerCore;
using IRExplorerCore.IR;
using ProtoBuf;

namespace IRExplorerUI.Compilers {
    public interface IDebugInfoProvider : IDisposable {
        bool LoadDebugInfo(string debugFilePath);
        bool AnnotateSourceLocations(FunctionIR function, IRTextFunction textFunc);
        bool AnnotateSourceLocations(FunctionIR function, string functionName);
        void Dispose();
        IEnumerable<DebugFunctionInfo> EnumerateFunctions();
        DebugFunctionInfo FindFunction(string functionName);
        DebugFunctionInfo FindFunctionByRVA(long rva);
        SourceLineInfo FindFunctionSourceFilePath(IRTextFunction textFunc);
        SourceLineInfo FindFunctionSourceFilePath(string functionName);
        SourceLineInfo FindSourceLineByRVA(DebugFunctionInfo funcInfo, long rva);
    }

    [ProtoContract(SkipConstructor = true)]
    public class SymbolFileSourceOptions : SettingsBase {
        [ProtoMember(1)]
        public bool UseDefaultSymbolSource { get; set; }
        [ProtoMember(2)]
        public bool UseSymbolCache { get; set; }
        [ProtoMember(3)]
        public string SymbolCachePath { get; set; }
        [ProtoMember(4)]
        public List<string> SymbolSearchPaths { get; set; }

        public bool HasSymbolCachePath => !string.IsNullOrEmpty(SymbolCachePath);

        public bool HasSymbolPath(string path) {
            path = Utils.TryGetDirectoryName(path).ToLowerInvariant();
            return SymbolSearchPaths.Find(item => item.ToLowerInvariant() == path) != null;
        }

        public void InsertSymbolPath(string path) {
            if (HasSymbolPath(path)) {
                return;
            }

            path = Utils.TryGetDirectoryName(path);

            if (!string.IsNullOrEmpty(path)) {
                SymbolSearchPaths.Insert(0, path);
            }
        }

        public SymbolFileSourceOptions() {
            Reset();
        }

        public static SymbolFileSourceOptions Default = new SymbolFileSourceOptions();

        public override void Reset() {
            InitializeReferenceMembers();
            UseDefaultSymbolSource = true;
            UseSymbolCache = true;
        }

        [ProtoAfterDeserialization]
        private void InitializeReferenceMembers() {
            SymbolSearchPaths ??= new List<string>();
        }

        public override SettingsBase Clone() {
            var serialized = StateSerializer.Serialize(this);
            return StateSerializer.Deserialize<SymbolFileSourceOptions>(serialized);
        }
    }

    public class SymbolFileDescriptor {
        public string FileName { get; set; }
        public Guid Id { get; set; }
        public int Age { get; set; }
    }
}
