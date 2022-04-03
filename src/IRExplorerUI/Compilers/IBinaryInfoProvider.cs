using System.Reflection.PortableExecutable;

namespace IRExplorerUI.Compilers;

public interface IBinaryInfoProvider {
    SymbolFileDescriptor SymbolFileInfo { get; }
    BinaryFileDescription BinaryFileInfo { get; }
    //? TODO: Add finding of binary here
}

public class BinaryFileDescription {
    public string ImageName { get; set; }
    public string ImagePath { get; set; }
    public Machine Architecture { get; set; }
    public BinaryFileKind FileKind { get; set; }
    public long Checksum { get; set; }
    public long TimeStamp { get; set; }
    public long ImageSize { get; set; }
    public long CodeSize { get; set; }
    public int MajorVersion { get; set; }
    public int MinorVersion { get; set; }

}

public enum BinaryFileKind {
    Native,
    DotNet,
    DotNetR2R
}
