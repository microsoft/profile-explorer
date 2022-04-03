// // Copyright (c) Microsoft Corporation. All rights reserved.
// // Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection.PortableExecutable;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.Diagnostics.Symbols;

namespace IRExplorerUI.Compilers {
    public class PEBinaryInfoProvider : IBinaryInfoProvider, IDisposable {
        private string filePath_;
        private PEReader reader_;

        public PEBinaryInfoProvider(string filePath) {
            filePath_ = filePath;
        }

        public bool Initialize() {
            if (!File.Exists(filePath_)) {
                return false;
            }

            try {
                var stream = File.OpenRead(filePath_);
                reader_ = new PEReader(stream);
                return reader_.PEHeaders != null; // Throws BadImageFormatException on invalid file.
            }
            catch (Exception ex) {
                Trace.WriteLine($"Failed to read PE binary file: {filePath_}");
                return false;
            }
        }

        public static BinaryFileDescription GetBinaryFileInfo(string filePath) {
            using var binaryInfo = new PEBinaryInfoProvider(filePath);

            if (binaryInfo.Initialize()) {
                return binaryInfo.BinaryFileInfo;
            }

            return null;
        }

        public static SymbolFileDescriptor GetSymbolFileInfo(string filePath) {
            using var binaryInfo = new PEBinaryInfoProvider(filePath);

            if (binaryInfo.Initialize()) {
                return binaryInfo.SymbolFileInfo;
            }

            return null;
        }

        public static async Task<string> LocateBinaryFile(BinaryFileDescription binaryFile, SymbolFileSourceOptions options) {
            using var logWriter = new StringWriter();
            var userSearchPath = PDBDebugInfoProvider.ConstructSymbolSearchPath(options);
            using var symbolReader = new SymbolReader(logWriter, userSearchPath);
            var result = await Task.Run(() => symbolReader.FindExecutableFilePath(binaryFile.ImageName, (int)binaryFile.TimeStamp, (int)binaryFile.ImageSize));

            Trace.WriteLine($">> TraceEvent FindExecutableFilePath for {binaryFile.ImageName}");
            Trace.WriteLine(logWriter.ToString());
            Trace.WriteLine($"<< TraceEvent");
            return result;
        }

        public SymbolFileDescriptor SymbolFileInfo {
            get {
                foreach (var entry in reader_.ReadDebugDirectory()) {
                    if (entry.Type == DebugDirectoryEntryType.CodeView) {
                        try {
                            var dir = reader_.ReadCodeViewDebugDirectoryData(entry);
                            return new SymbolFileDescriptor() {
                                FileName = dir.Path,
                                Id = dir.Guid,
                                Age = dir.Age
                            };
                        }
                        catch (BadImageFormatException) {
                            // PE reader has problems with some old binaries.
                        }

                        break;
                    }
                }

                return null;
            }
        }

        public BinaryFileDescription BinaryFileInfo {
            get {
                if (reader_.PEHeaders.PEHeader == null) {
                    return null;
                }

                BinaryFileKind fileKind = BinaryFileKind.Native;

                if (reader_.HasMetadata && reader_.PEHeaders.CorHeader != null) {
                    if (reader_.PEHeaders.CorHeader.Flags.HasFlag(CorFlags.ILLibrary)) {
                        fileKind = BinaryFileKind.DotNetR2R;
                    }
                    else if (reader_.PEHeaders.CorHeader.Flags.HasFlag(CorFlags.ILLibrary)) {
                        fileKind = BinaryFileKind.DotNet;
                    }
                }

                return new BinaryFileDescription() {
                    ImageName = Utils.TryGetFileName(filePath_),
                    ImagePath = filePath_,
                    Architecture = reader_.PEHeaders.CoffHeader.Machine,
                    FileKind = fileKind,
                    Checksum = reader_.PEHeaders.PEHeader.CheckSum,
                    TimeStamp = reader_.PEHeaders.CoffHeader.TimeDateStamp,
                    ImageSize = reader_.PEHeaders.PEHeader.SizeOfImage,
                    CodeSize = reader_.PEHeaders.PEHeader.SizeOfCode,
                    MajorVersion = reader_.PEHeaders.PEHeader.MajorImageVersion,
                    MinorVersion = reader_.PEHeaders.PEHeader.MinorImageVersion
                };
            }
        }

        public void Dispose() {
            reader_?.Dispose();
        }
    }
}