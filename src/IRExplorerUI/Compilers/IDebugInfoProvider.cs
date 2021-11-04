using System;
using System.Collections.Generic;
using System.Reflection.PortableExecutable;
using IRExplorerCore;
using IRExplorerCore.IR;
using ProtoBuf;

namespace IRExplorerUI.Compilers {
    public struct DebugFunctionInfo : IEquatable<DebugFunctionInfo> {
        public string Name;
        public long RVA;
        public long Size;

        public long StartRVA => RVA;
        public long EndRVA => RVA + Size - 1;

        public DebugFunctionInfo(string name, long rva, long size) {
            Name = name;
            RVA = rva;
            Size = size;
        }

        public override bool Equals(object obj) {
            return obj is DebugFunctionInfo info && Equals(info);
        }

        public bool Equals(DebugFunctionInfo other) {
            return RVA == other.RVA &&
                   Size == other.Size;
        }

        public override int GetHashCode() {
            return HashCode.Combine(Name, RVA, Size);
        }
    }

    public interface IDebugInfoProvider {
        bool AnnotateSourceLocations(FunctionIR function, IRTextFunction textFunc);
        bool AnnotateSourceLocations(FunctionIR function, string functionName);
        void Dispose();
        IEnumerable<DebugFunctionInfo> EnumerateFunctions();
        (string Name, long RVA) FindFunctionByRVA(long rva);
        (string Path, int LineNumber) FindFunctionSourceFilePath(IRTextFunction textFunc);
        (string Path, int LineNumber) FindFunctionSourceFilePath(string functionName);
        (string Path, int LineNumber) FindFunctionSourceFilePathByRVA(long rva);
        int FindLineByRVA(long rva);
        bool LoadDebugInfo(string debugFilePath);
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

        public SymbolFileSourceOptions() {
            Reset();
        }

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

    public class BinaryFileDescription {
        public string FilePath { get; set; }
        public Machine Architecture { get; set; }
        public long TimeStamp { get; set; }
        public long ImageSize { get; set; }
        public long CodeSize { get; set; }
        public int MajorVersion { get; set; }
        public int MinorVersion { get; set; }

    }

    public interface IBinaryInfoProvider {
        SymbolFileDescriptor SymbolFileInfo { get; }
        BinaryFileDescription BinaryFileInfo { get; }
    }
}
