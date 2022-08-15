using System;
using System.Reflection.PortableExecutable;

namespace IRExplorerUI.Compilers;

public interface IBinaryInfoProvider {
    SymbolFileDescriptor SymbolFileInfo { get; }
    BinaryFileDescriptor BinaryFileInfo { get; }
    //? TODO: Add finding of binary here
}

public class BinaryFileDescriptor : IEquatable<BinaryFileDescriptor> {
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

    public bool Equals(BinaryFileDescriptor other) {
        if (ReferenceEquals(null, other)) {
            return false;
        }

        if (ReferenceEquals(this, other)) {
            return true;
        }

        return ImageName == other.ImageName && Architecture == other.Architecture && FileKind == other.FileKind && ImageSize == other.ImageSize && MajorVersion == other.MajorVersion && MinorVersion == other.MinorVersion;
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
        return HashCode.Combine(ImageName, (int)Architecture, (int)FileKind, ImageSize, MajorVersion, MinorVersion);
    }

    public static bool operator ==(BinaryFileDescriptor left, BinaryFileDescriptor right) {
        return Equals(left, right);
    }

    public static bool operator !=(BinaryFileDescriptor left, BinaryFileDescriptor right) {
        return !Equals(left, right);
    }

}

public enum BinaryFileKind {
    Native,
    DotNet,
    DotNetR2R
}
