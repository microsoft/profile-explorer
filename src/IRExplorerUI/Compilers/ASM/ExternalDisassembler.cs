// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using IRExplorerCore;
using IRExplorerUI.Compilers;
using ProtoBuf;

// Save recent exec files with pdb pairs
// Caching that checks for same CRC
// Try to fill PDB path

namespace IRExplorerUI.Compilers.ASM {
    [ProtoContract(SkipConstructor = true)]
    public class ExternalDisassemblerOptions : SettingsBase {
        private const string DEFAULT_DISASM_NAME = "dumpbin.exe";
        private const string DEFAULT_DISASM_ARGS = "/disasm /out:\"$DST\" \"$SRC\"";
        private const string DEFAULT_POSTPROC_TOOL_NAME = "";
        private const string DEFAULT_POSTPROC_TOOL_ARGS = "$SRC $DST";

        private const string DEFAULT_DOTNET_DISASM_NAME = "r2rdump.exe";
        private const string DEFAULT_DOTNET_DISASM_ARGS = "--in \"$SRC\" --out \"$DST\" --disasm --irexplorer-dump";

        [ProtoMember(1)]
        public string DisassemblerPath { get; set; }
        [ProtoMember(2)]
        public string DisassemblerArguments { get; set; }
        [ProtoMember(3)]
        public string PostProcessorPath { get; set; }
        [ProtoMember(4)]
        public string PostProcessorArguments { get; set; }
        [ProtoMember(5)]
        public bool CacheDissasembly { get; set; }
        [ProtoMember(6)]
        public bool OptionsExpanded { get; set; }
        [ProtoMember(7)]
        public BinaryFileKind FileKind { get; set; }
        [ProtoMember(8)]
        public bool UseInputFileName { get; set; }
        [ProtoMember(9)]
        public bool IsEnabled { get; set; }

        public ExternalDisassemblerOptions(BinaryFileKind fileKind) {
            FileKind = fileKind;
            Reset();
        }

        public override void Reset() {
            DisassemblerPath = DetectDisassembler();

            switch (FileKind) {
                case BinaryFileKind.Native: {
                    DisassemblerArguments = DEFAULT_DISASM_ARGS;
                    PostProcessorPath = DEFAULT_POSTPROC_TOOL_NAME;
                    PostProcessorArguments = DEFAULT_POSTPROC_TOOL_ARGS;
                    break;
                }
                case BinaryFileKind.DotNetR2R: {
                    DisassemblerArguments = DEFAULT_DOTNET_DISASM_ARGS;
                    UseInputFileName = true;
                    break;
                }
                default: {
                    throw new InvalidOperationException();
                }
            }

            CacheDissasembly = true;
            OptionsExpanded = true;
        }

        public string DetectDisassembler() {
            switch (FileKind) {
                case BinaryFileKind.Native: {
                    try {

                        var path = Utils.DetectMSVCPath();
                        var disasmPath = Path.Combine(path, DEFAULT_DISASM_NAME);

                        if (File.Exists(disasmPath)) {
                            return disasmPath;
                        }
                    }
                    catch (Exception ex) {
                        Trace.TraceError($"Failed to detect disassembler: {ex.Message}");
                    }

                    return DEFAULT_DISASM_NAME;
                }
                case BinaryFileKind.DotNetR2R: {
                    return DEFAULT_DOTNET_DISASM_NAME;
                }
                default: {
                    throw new InvalidOperationException();
                }
            }
        }

        public override SettingsBase Clone() {
            var serialized = StateSerializer.Serialize(this);
            return StateSerializer.Deserialize<ExternalDisassemblerOptions>(serialized);
        }
    }

    public enum DisassemblerStage {
        Disassembling,
        PostProcessing
    }

    public class DisassemblerProgress {
        public DisassemblerProgress(DisassemblerStage stage) {
            Stage = stage;
        }

        public DisassemblerStage Stage { get; set; }
        public int Total { get; set; }
        public int Current { get; set; }
    }

    public delegate void DisassemblerProgressHandler(DisassemblerProgress info);

    public interface IDisassembler {
        DisassemberResult Disassemble(string imagePath, ICompilerInfoProvider compilerInfo,
                                      DisassemblerProgressHandler progressCallback = null,
                                      CancelableTask cancelableTask = null);
        Task<DisassemberResult> DisassembleAsync(string imagePath, ICompilerInfoProvider compilerInfo,
                                                 DisassemblerProgressHandler progressCallback = null,
                                                 CancelableTask cancelableTask = null);

        bool EnsureDisassemblerAvailable();
    }

    public class DisassemberResult {
        public DisassemberResult(string disassemblyPath, string debugInfoFilePath) {
            DisassemblyPath = disassemblyPath;
            DebugInfoFilePath = debugInfoFilePath;
        }

        public string DisassemblyPath { get; set; }
        public string DebugInfoFilePath { get; set; }
    }

    public class ExternalDisassembler : IDisassembler {
        private const string DEST_PLACEHOLDER = "$DST";
        private const string SOURCE_PLACEHOLDER = "$SRC";
        private const string DEBUG_PLACEHOLDER = "$DBG";
        public ExternalDisassemblerOptions Options { get; }

        public ExternalDisassembler(ExternalDisassemblerOptions options) {
            Options = options;
        }

        public DisassemberResult Disassemble(string imagePath, ICompilerInfoProvider compilerInfo,
                                  DisassemblerProgressHandler progressCallback,
                                  CancelableTask cancelableTask) {
            return DisassembleImpl(imagePath, compilerInfo, progressCallback, cancelableTask);
        }


        public Task<DisassemberResult> DisassembleAsync(string imagePath, ICompilerInfoProvider compilerInfo,
                          DisassemblerProgressHandler progressCallback = null,
                          CancelableTask cancelableTask = null) {
            return Task.Run(() => DisassembleImpl(imagePath, compilerInfo, progressCallback, cancelableTask));
        }

        private DisassemberResult DisassembleImpl(string imagePath, ICompilerInfoProvider compilerInfo,
            DisassemblerProgressHandler progressCallback,
            CancelableTask cancelableTask) {
            try {
                var outputFilePath = Path.GetTempFileName();
                var finalFilePath = outputFilePath;

                if (!string.IsNullOrEmpty(Options.PostProcessorPath)) {
                    finalFilePath = Path.GetTempFileName();
                }

                var debugPath = compilerInfo.FindDebugInfoFile(imagePath).Result;

                if (DisassembleImpl(imagePath, outputFilePath, debugPath,
                        finalFilePath, progressCallback, cancelableTask)) {
                    // Some disassemblers create the debug file themselves,
                    // query again if fine couldn't be found.
                    if (!File.Exists(debugPath)) {
                        debugPath = compilerInfo.FindDebugInfoFile(imagePath, null, outputFilePath).Result;
                    }

                    return new DisassemberResult(finalFilePath, debugPath);
                }
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed to run disassembler for {imagePath}: {ex.Message}");
            }

            return null;
        }

        private bool DisassembleImpl(string imagePath, string disassemblyPath,
            string debugPath,
            string postprocessingPath, DisassemblerProgressHandler progressCallback,
            CancelableTask cancelableTask) {
            try {
                var disasmArgs = Options.DisassemblerArguments.Replace(DEST_PLACEHOLDER, disassemblyPath)
                                                               .Replace(SOURCE_PLACEHOLDER, imagePath)
                                                               .Replace(DEBUG_PLACEHOLDER, debugPath);
                progressCallback?.Invoke(new DisassemblerProgress(DisassemblerStage.Disassembling));

                // Force the symbol path in the disasm context so that it picks the PDB file
                // in case it's in another directory (downloaded from a symbol server for ex).
                var envVariables = new Dictionary<string, string>();

                if (!string.IsNullOrEmpty(debugPath)) {
                    envVariables["_NT_SYMBOL_PATH"] = Utils.TryGetDirectoryName(debugPath);
                }

                if (!Utils.ExecuteTool(Options.DisassemblerPath, disasmArgs, cancelableTask, envVariables)) {
                    Trace.TraceError($"Disassembler task {ObjectTracker.Track(cancelableTask)}: Failed to execute disassembler: ${Options.DisassemblerPath}");
                    return false;
                }

                if (!File.Exists(disassemblyPath)) {
                    Trace.TraceError($"Disassembler task {ObjectTracker.Track(cancelableTask)}: Output file not found: ${disassemblyPath}");
                    return false;
                }

                // Done if no post-processing of the output is needed.
                if (string.IsNullOrEmpty(Options.PostProcessorPath)) {
                    return true;
                }

                var args = Options.PostProcessorArguments.Replace(DEST_PLACEHOLDER, postprocessingPath)
                                                          .Replace(SOURCE_PLACEHOLDER, disassemblyPath)
                                                          .Replace(DEBUG_PLACEHOLDER, debugPath);
                progressCallback?.Invoke(new DisassemblerProgress(DisassemblerStage.PostProcessing));

                if (!Utils.ExecuteTool(Options.PostProcessorPath, args, cancelableTask)) {
                    Trace.TraceError($"Disassembler task {ObjectTracker.Track(cancelableTask)}: Failed to execute post-processing tool: ${Options.DisassemblerPath}");
                    return false;
                }

                if (!File.Exists(postprocessingPath)) {
                    Trace.TraceError($"Disassembler task {ObjectTracker.Track(cancelableTask)}: Postprocessing output file not found: ${postprocessingPath}");
                    return false;
                }

                return true;
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed to run disassembler for {imagePath}: {ex.Message}");
            }

            return false;
        }

        public bool EnsureDisassemblerAvailable() {
            if (!File.Exists(Options.DisassemblerPath)) {
                var disasmPath = Options.DetectDisassembler();

                if (string.IsNullOrEmpty(disasmPath)) {
                    return false;
                }

                Options.DisassemblerPath = disasmPath;
                App.SaveApplicationSettings();
            }

            return true;
        }
    }
}
