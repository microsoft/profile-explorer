﻿// // Copyright (c) Microsoft Corporation. All rights reserved.
// // Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.Diagnostics.Symbols;

namespace IRExplorerUI.Compilers {
    public sealed class PEBinaryInfoProvider : IBinaryInfoProvider, IDisposable {
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

        public static BinaryFileDescriptor GetBinaryFileInfo(string filePath) {
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

        public static async Task<BinaryFileSearchResult> LocateBinaryFile(BinaryFileDescriptor binaryFile,
                                                                          SymbolFileSourceOptions options) {
            string result = null;
            await using var logWriter = new StringWriter();
     
            try {
                options = options.WithSymbolPaths(binaryFile.ImagePath);
                var userSearchPath = PDBDebugInfoProvider.ConstructSymbolSearchPath(options);
                using var symbolReader = new SymbolReader(logWriter, userSearchPath);
                symbolReader.SecurityCheck += s => true; // Allow symbols from "unsafe" locations.
                
                result = await Task.Run(() => symbolReader.FindExecutableFilePath(binaryFile.ImageName,
                    (int)binaryFile.TimeStamp,
                    (int)binaryFile.ImageSize)).ConfigureAwait(false);

                if (result == null) {
                    // Manually search in the provided directories.
                    // This helps in cases where the original fine name doesn't match
                    // the one on disk, like it seems to happen sometimes with the SPEC runner.
                    result = await Task.Run(() => {
                        var winPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                        var sysPath = Environment.GetFolderPath(Environment.SpecialFolder.System);
                        var sysx86Path = Environment.GetFolderPath(Environment.SpecialFolder.SystemX86);

                        // Don't search in the system dirs though, it's pointless
                        // and takes a long time checking thousands of binaries.
                        bool PathIsSubPath(string subPath, string basePath) {
                            var rel = Path.GetRelativePath(basePath, subPath);
                            return !rel.StartsWith('.') && !Path.IsPathRooted(rel);
                        }

                        foreach (var path in options.SymbolSearchPaths) {
                            if (PathIsSubPath(path, winPath)||
                                PathIsSubPath(path, sysPath) ||
                                PathIsSubPath(path, sysx86Path)) {
                                continue;
                            }

                            try {
                                var searchPath = Utils.TryGetDirectoryName(path);

                                foreach (var file in Directory.EnumerateFiles(searchPath, $"*.*", SearchOption.TopDirectoryOnly)) {
                                    if (!Utils.IsBinaryFile(file)) {
                                        continue;
                                    }

                                    var fileInfo = GetBinaryFileInfo(file);

                                    if (fileInfo != null &&
                                        fileInfo.TimeStamp == binaryFile.TimeStamp &&
                                        fileInfo.ImageSize == binaryFile.ImageSize) {
                                        Trace.Flush();
                                        return file;
                                    }
                                }
                            }
                            catch (Exception ex) {
                                Trace.TraceError($"Exception searching for binary {binaryFile.ImageName} in {path}: {ex.Message}");
                            }
                        }
                        Trace.Flush();

                        return null;
                    });
                }
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed FindExecutableFilePath: {ex.Message}");
            }
#if DEBUG
            Trace.WriteLine($">> TraceEvent FindExecutableFilePath for {binaryFile.ImageName}");
            Trace.WriteLine(logWriter.ToString());
            Trace.WriteLine($"<< TraceEvent");
#endif
            if (!string.IsNullOrEmpty(result) && File.Exists(result)) {
                // Read the binary info from the local file to fill in all fields.
                binaryFile = PEBinaryInfoProvider.GetBinaryFileInfo(result);
                return BinaryFileSearchResult.Success(binaryFile, result, logWriter.ToString());
            }

            return BinaryFileSearchResult.Failure(binaryFile, logWriter.ToString());
        }

        public SymbolFileDescriptor SymbolFileInfo {
            get {
                foreach (var entry in reader_.ReadDebugDirectory()) {
                    if (entry.Type == DebugDirectoryEntryType.CodeView) {
                        try {
                            var dir = reader_.ReadCodeViewDebugDirectoryData(entry);
                            return new SymbolFileDescriptor(dir.Path, dir.Guid, dir.Age);
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

        public List<SectionHeader> CodeSectionHeaders {
            get {
                var list = new List<SectionHeader>();

                if (reader_.PEHeaders.PEHeader == null) {
                    return list;
                }

                foreach (var section in reader_.PEHeaders.SectionHeaders) {
                    if (section.SectionCharacteristics.HasFlag(SectionCharacteristics.MemExecute) ||
                        section.SectionCharacteristics.HasFlag(SectionCharacteristics.ContainsCode)) {
                        list.Add(section);
                    }
                }

                return list;
            }
        }

        public byte[] GetSectionData(SectionHeader header) {
            var data = reader_.GetSectionData(header.VirtualAddress);
            var array = data.GetContent();
            var copy = new byte[array.Length];
            array.CopyTo(copy);
            return copy;
            //? TODO: return Unsafe.As<byte[]>(array);
        }

        public BinaryFileDescriptor BinaryFileInfo {
            get {
                if (reader_.PEHeaders.PEHeader == null) {
                    return null;
                }

                BinaryFileKind fileKind = BinaryFileKind.Native;

                if (reader_.HasMetadata && reader_.PEHeaders.CorHeader != null) {
                    if (reader_.PEHeaders.CorHeader.Flags.HasFlag(CorFlags.ILOnly)) {
                        fileKind = BinaryFileKind.DotNet;
                    }
                    else if (reader_.PEHeaders.CorHeader.Flags.HasFlag(CorFlags.ILLibrary)) {
                        fileKind = BinaryFileKind.DotNetR2R;
                    }
                }

                return new BinaryFileDescriptor() {
                    ImageName = Utils.TryGetFileName(filePath_),
                    ImagePath = filePath_,
                    Architecture = reader_.PEHeaders.CoffHeader.Machine,
                    FileKind = fileKind,
                    Checksum = reader_.PEHeaders.PEHeader.CheckSum,
                    TimeStamp = reader_.PEHeaders.CoffHeader.TimeDateStamp,
                    ImageSize = reader_.PEHeaders.PEHeader.SizeOfImage,
                    CodeSize = reader_.PEHeaders.PEHeader.SizeOfCode,
                    ImageBase = (long)reader_.PEHeaders.PEHeader.ImageBase,
                    BaseOfCode = reader_.PEHeaders.PEHeader.BaseOfCode,
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