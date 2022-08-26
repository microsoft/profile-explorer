using System;
using System.Reflection.PortableExecutable;
using ProtoBuf;

namespace IRExplorerUI.Compilers;

public interface IBinaryInfoProvider {
    SymbolFileDescriptor SymbolFileInfo { get; }
    BinaryFileDescriptor BinaryFileInfo { get; }
    //? TODO: Add finding of binary here
}

[ProtoContract(SkipConstructor = true)]
public class BinaryFileDescriptor : IEquatable<BinaryFileDescriptor> {
    [ProtoMember(1)]
    public string ImageName { get; set; }
    [ProtoMember(2)]
    public string ImagePath { get; set; }
    [ProtoMember(3)]
    public Machine Architecture { get; set; }
    [ProtoMember(4)]
    public BinaryFileKind FileKind { get; set; }
    [ProtoMember(5)]
    public long Checksum { get; set; }
    [ProtoMember(6)]
    public int TimeStamp { get; set; }
    [ProtoMember(7)]
    public long ImageSize { get; set; }
    [ProtoMember(8)]
    public long CodeSize { get; set; }
    [ProtoMember(9)]
    public long ImageBase { get; set; }
    [ProtoMember(10)]
    public long BaseOfCode { get; set; }
    [ProtoMember(11)]
    public int MajorVersion { get; set; }
    [ProtoMember(12)]
    public int MinorVersion { get; set; }

    public bool IsNativeImage => FileKind == BinaryFileKind.Native;
    public bool IsManagedImage => FileKind == BinaryFileKind.DotNet ||
                                  FileKind == BinaryFileKind.DotNetR2R;

    public bool Equals(BinaryFileDescriptor other) {
        if (ReferenceEquals(null, other)) {
            return false;
        }

        if (ReferenceEquals(this, other)) {
            return true;
        }

        return ImageName.Equals(other.ImageName, StringComparison.OrdinalIgnoreCase) &&
               TimeStamp == other.TimeStamp &&
               ImageSize == other.ImageSize;
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

        return Equals((BinaryFileDescriptor)obj);
    }

    public override int GetHashCode() {
        return HashCode.Combine(ImageName, TimeStamp, ImageSize);
    }

    public static bool operator ==(BinaryFileDescriptor left, BinaryFileDescriptor right) {
        return Equals(left, right);
    }

    public static bool operator !=(BinaryFileDescriptor left, BinaryFileDescriptor right) {
        return !Equals(left, right);
    }

    public override string ToString() {
        return $"{ImageName}, Version: {MajorVersion}.{MinorVersion}, ImageSze: {ImageSize}";
    }

}

public enum BinaryFileKind {
    Native,
    DotNet,
    DotNetR2R
}
