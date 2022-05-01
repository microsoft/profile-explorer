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
        public Machine? Architecture { get; }
        IEnumerable<DebugFunctionInfo> EnumerateFunctions(bool includeExternal = false);
        DebugFunctionInfo FindFunction(string functionName);
        DebugFunctionInfo FindFunctionByRVA(long rva);
        DebugFunctionSourceFileInfo FindFunctionSourceFilePath(IRTextFunction textFunc);
        DebugFunctionSourceFileInfo FindFunctionSourceFilePath(string functionName);
        DebugFunctionSourceFileInfo FindSourceFilePathByRVA(long rva);
        DebugSourceLineInfo FindSourceLineByRVA(long rva);
    }

    [ProtoContract(SkipConstructor = true)]
    public class SymbolFileSourceOptions : SettingsBase {
        [ProtoMember(1)]
        public bool UseDefaultSymbolSource { get; set; }
        [ProtoMember(2)]
        public bool UseSymbolCache { get; set; }
        [ProtoMember(3)] 
        public bool SymbolSearchPathsEnabled { get; set; }
        [ProtoMember(4)]
        public string SymbolCachePath { get; set; }
        [ProtoMember(5)]
        public List<string> SymbolSearchPaths { get; set; }
        [ProtoMember(6)]
        public bool SourceServerEnabled { get; set; }
        [ProtoMember(7)]
        public bool AuthorizationTokenEnabled { get; set; }
        [ProtoMember(8)]
        public string AuthorizationToken { get; set; }

        public bool HasSymbolCachePath => !string.IsNullOrEmpty(SymbolCachePath);
        public bool HasAuthorizationToken => AuthorizationTokenEnabled && !string.IsNullOrEmpty(AuthorizationToken);

        public bool HasSymbolPath(string path) {
            path = Utils.TryGetDirectoryName(path).ToLowerInvariant();
            return SymbolSearchPaths.Find(item => item.ToLowerInvariant() == path) != null;
        }

        public void InsertSymbolPath(string path) {
            if(string.IsNullOrEmpty(path) || HasSymbolPath(path)) {
                return;
            }

            path = Utils.TryGetDirectoryName(path);

            if (!string.IsNullOrEmpty(path)) {
                SymbolSearchPaths.Insert(0, path);
            }
        }

        public SymbolFileSourceOptions WithSymbolPaths(params string[] paths) {
            var options = (SymbolFileSourceOptions)Clone();

            foreach (var path in paths) {
                options.InsertSymbolPath(path);
            }

            return options;
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

    //? TODO: Convert to record, similar for other such classes
    public class SymbolFileDescriptor {
        public string FileName { get; set; }
        public Guid Id { get; set; }
        public int Age { get; set; }
    }
}
