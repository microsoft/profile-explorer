using System.Reflection.PortableExecutable;

namespace IRExplorerUI.Compilers;

public interface IBinaryInfoProvider {
    SymbolFileDescriptor SymbolFileInfo { get; }
    BinaryFileDescriptor BinaryFileInfo { get; }
    //? TODO: Add finding of binary here
}

public class BinaryFileDescriptor {
    public string ImageName { get; set; }
    public string ImagePath { get; set; }
    public Machine Architecture { get; set; }
    public BinaryFileKind FileKind { get; set; }
    public long Checksum { get; set; }
    public int TimeStamp { get; set; }
    public long ImageSize { get; set; }
    public long CodeSize { get; set; }
    public long ImageBase { get; set; }
    public long BaseOfCode { get; set; }
    public int MajorVersion { get; set; }
    public int MinorVersion { get; set; }

    public bool IsNativeImage => FileKind == BinaryFileKind.Native;
    public bool IsManagedImage => FileKind == BinaryFileKind.DotNet ||
                                  FileKind == BinaryFileKind.DotNetR2R;
}

public enum BinaryFileKind {
    Native,
    DotNet,
    DotNetR2R
}
