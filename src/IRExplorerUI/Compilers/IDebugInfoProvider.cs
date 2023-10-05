using System;
using System.Collections.Generic;
using System.Reflection.PortableExecutable;
using IRExplorerCore;
using IRExplorerCore.IR;
using ProtoBuf;

namespace IRExplorerUI.Compilers {
    public interface IDebugInfoProvider : IDisposable {
        bool LoadDebugInfo(string debugFilePath);
        void Unload();
        bool LoadDebugInfo(DebugFileSearchResult debugFile);
        bool AnnotateSourceLocations(FunctionIR function, IRTextFunction textFunc);
        bool AnnotateSourceLocations(FunctionIR function, string functionName);
        public Machine? Architecture { get; }
        public SymbolFileSourceOptions SymbolOptions { get; set; }
        IEnumerable<FunctionDebugInfo> EnumerateFunctions();
        List<FunctionDebugInfo> GetSortedFunctions();
        FunctionDebugInfo FindFunction(string functionName);
        FunctionDebugInfo FindFunctionByRVA(long rva);
        SourceFileDebugInfo FindFunctionSourceFilePath(IRTextFunction textFunc);
        SourceFileDebugInfo FindFunctionSourceFilePath(string functionName);
        SourceFileDebugInfo FindSourceFilePathByRVA(long rva);
        SourceLineDebugInfo FindSourceLineByRVA(long rva);
    }

    [ProtoContract(SkipConstructor = true)]
    public class SymbolFileSourceOptions : SettingsBase {
        private const string DefaultSymbolSourcePath = @"https://symweb";
        private const string DefaultSymbolCachePath = @"C:\Symbols";

        [ProtoMember(1)]
        public bool SymbolSourcePathEnabled { get; set; }
        [ProtoMember(2)]
        public string SymbolSourcePath { get; set; }
        [ProtoMember(3)]
        public bool SymbolCachePathEnabled { get; set; }
        [ProtoMember(4)]
        public string SymbolCachePath { get; set; }
        [ProtoMember(5)]
        public bool SymbolSearchPathsEnabled { get; set; }
        [ProtoMember(6)]
        public List<string> SymbolSearchPaths { get; set; }
        [ProtoMember(7)]
        public bool SourceServerEnabled { get; set; }
        [ProtoMember(8)]
        public bool AuthorizationTokenEnabled { get; set; }
        [ProtoMember(9)]
        public string AuthorizationToken { get; set; }

        public bool HasSymbolSourcePath => !string.IsNullOrEmpty(SymbolSourcePath);
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

        public void InsertSymbolPaths(IEnumerable<string> paths) {
            foreach (var path in paths) {
                InsertSymbolPath(path);
            }
        }

        public SymbolFileSourceOptions WithSymbolPaths(params string[] paths) {
            var options = Clone();

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

            SymbolSourcePath = DefaultSymbolSourcePath;
            SymbolSourcePathEnabled = true;
            SymbolCachePath = DefaultSymbolCachePath;
            SymbolCachePathEnabled = true;
        }

        [ProtoAfterDeserialization]
        private void InitializeReferenceMembers() {
            SymbolSearchPaths ??= new List<string>();
        }

        public SymbolFileSourceOptions Clone() {
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
            return HashCode.Combine(FileName.GetHashCode(StringComparison.OrdinalIgnoreCase), Id, Age);
        }

        public static bool operator ==(SymbolFileDescriptor left, SymbolFileDescriptor right) {
            return Equals(left, right);
        }

        public static bool operator !=(SymbolFileDescriptor left, SymbolFileDescriptor right) {
            return !Equals(left, right);
        }

        public SymbolFileDescriptor(string fileName, Guid id, int age) {
            FileName = fileName != null ? string.Intern(fileName) : null;
            Id = id;
            Age = age;
        }

        public SymbolFileDescriptor(string fileName) {
            FileName = fileName != null ? string.Intern(fileName) : null;
        }
    }
}