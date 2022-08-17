using System;
using System.Collections.Generic;
using System.Reflection.PortableExecutable;
using IRExplorerCore;
using IRExplorerCore.IR;
using ProtoBuf;

namespace IRExplorerUI.Compilers {
    public interface IDebugInfoProvider : IDisposable {
        bool LoadDebugInfo(string debugFilePath);
        bool LoadDebugInfo(DebugFileSearchResult debugFile);
        bool AnnotateSourceLocations(FunctionIR function, IRTextFunction textFunc);
        bool AnnotateSourceLocations(FunctionIR function, string functionName);
        public Machine? Architecture { get; }
        public SymbolFileSourceOptions SymbolOptions { get; set; }
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

    [ProtoContract(SkipConstructor = true)]
    public class SymbolFileDescriptor : IEquatable<SymbolFileDescriptor> {
        [ProtoMember(1)]
        public string FileName { get; set; }
        [ProtoMember(2)]
        public Guid Id { get; set; }
        [ProtoMember(3)]
        public int Age { get; set; }
        
        public override string ToString() {
            return $"{Id}:{FileName}";
        }

        public bool Equals(SymbolFileDescriptor other) {
            return FileName.Equals(other.FileName, StringComparison.OrdinalIgnoreCase) &&
                   Id == other.Id &&
                   Age == other.Age;
        }

        public override bool Equals(object obj) {
            if (ReferenceEquals(null, obj)) {
                return false;
            }

            if (ReferenceEquals(this, obj)) {
                return true;
            }

            if (obj.GetType() != this.GetType()) {
                return false;
            }

            return Equals((SymbolFileDescriptor)obj);
        }

        public override int GetHashCode() {
            return HashCode.Combine(FileName.ToLower(), Id, Age);
        }

        public static bool operator ==(SymbolFileDescriptor left, SymbolFileDescriptor right) {
            return Equals(left, right);
        }

        public static bool operator !=(SymbolFileDescriptor left, SymbolFileDescriptor right) {
            return !Equals(left, right);
        }

        public SymbolFileDescriptor(string fileName, Guid id, int age) {
            FileName = fileName;
            Id = id;
            Age = age;
        }

        public SymbolFileDescriptor(string fileName) {
            FileName = fileName;
        }
    }
}
