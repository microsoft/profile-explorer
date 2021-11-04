// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using IRExplorerCore;
using ProtoBuf;

// Save recent exec files with pdb pairs
// Caching that checks for same CRC
// Try to fill PDB path

namespace IRExplorerUI {
    [ProtoContract(SkipConstructor = true)]
    public class BinaryDisassemblerOptions : SettingsBase {
        private const string DEFAULT_DISASM_NAME = "dumpbin.exe";
        private const string DEFAULT_DISASM_ARGS = "/disasm /out:\"$DST\" \"$SRC\"";
        private const string DEFAULT_POSTPROC_TOOL_NAME = "";
        private const string DEFAULT_POSTPROC_TOOL_ARGS = "$SRC $DST";

        [ProtoMember(1)]
        public string DissasemblerPath { get; set; }
        [ProtoMember(2)]
        public string DissasemblerArguments { get; set; }
        [ProtoMember(3)]
        public string PostProcessorPath { get; set; }
        [ProtoMember(4)]
        public string PostProcessorArguments { get; set; }
        [ProtoMember(5)]
        public bool CacheDissasembly { get; set; }
        [ProtoMember(6)]
        public bool OptionsExpanded { get; set; }

        public BinaryDisassemblerOptions() {
            Reset();
        }

        public override void Reset() {
            var disasmPath = DetectDissasembler();

            if (!string.IsNullOrEmpty(disasmPath)) {
                DissasemblerPath = disasmPath;
            }
            else {
                DissasemblerPath = DEFAULT_DISASM_NAME;
            }

            DissasemblerArguments = DEFAULT_DISASM_ARGS;
            PostProcessorPath = DEFAULT_POSTPROC_TOOL_NAME;
            PostProcessorArguments = DEFAULT_POSTPROC_TOOL_ARGS;
            CacheDissasembly = true;
            OptionsExpanded = true;
        }

        public string DetectDissasembler() {
            try {
                var path = Utils.DetectMSVCPath();
                var disasmPath = Path.Combine(path, DEFAULT_DISASM_NAME);

                if (File.Exists(disasmPath)) {
                    return disasmPath;
                }
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed to detect dissasembler: {ex.Message}");
            }

            return null;
        }

        public override SettingsBase Clone() {
            var serialized = StateSerializer.Serialize(this);
            return StateSerializer.Deserialize<BinaryDisassemblerOptions>(serialized);
        }
    }

    public enum BinaryDisassemblerStage {
        Disassembling,
        PostProcessing
    }

    public class BinaryDisassemblerProgress {
        public BinaryDisassemblerProgress(BinaryDisassemblerStage stage) {
            Stage = stage;
        }

        public BinaryDisassemblerStage Stage { get; set; }
        public int Total { get; set; }
        public int Current { get; set; }
    }

    public delegate void BinaryDisassemblerProgressHandler(BinaryDisassemblerProgress info);

    public class BinaryDisassembler {
        private const string DEST_PLACEHOLDER = "$DST";
        private const string SOURCE_PLACEHOLDER = "$SRC";
        private const string DEBUG_PLACEHOLDER = "$DBG";
        private BinaryDisassemblerOptions options_;

        public BinaryDisassembler(BinaryDisassemblerOptions options) {
            options_ = options;
        }

        public string Disassemble(string exePath, string debugPath,
                                  BinaryDisassemblerProgressHandler progressCallback,
                                  CancelableTask cancelableTask) {
            try {
                var outputFilePath = Path.GetTempFileName();
                var finalFilePath = outputFilePath;

                if(!string.IsNullOrEmpty(options_.PostProcessorPath)) {
                    finalFilePath = Path.GetTempFileName();
                }

                if(Disassemble(exePath, debugPath, outputFilePath, finalFilePath, 
                               progressCallback, cancelableTask)) {
                    return finalFilePath;
                }
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed to run disassembler for {exePath}: {ex.Message}");
            }

            return null;
        }

        public Task<string> DisassembleAsync(string exePath, string debugPath,
                          BinaryDisassemblerProgressHandler progressCallback = null,
                          CancelableTask cancelableTask = null) {
            return Task.Run(() => Disassemble(exePath, debugPath, progressCallback, cancelableTask));
        }

        public bool Disassemble(string exePath, string debugPath, string dissasemblyPath, 
                                string postprocessingPath, BinaryDisassemblerProgressHandler progressCallback,
                                CancelableTask cancelableTask) {
            try {
                var disasmArgs = options_.DissasemblerArguments.Replace(DEST_PLACEHOLDER, dissasemblyPath)
                                                               .Replace(SOURCE_PLACEHOLDER, exePath)
                                                               .Replace(DEBUG_PLACEHOLDER, debugPath);
                progressCallback?.Invoke(new BinaryDisassemblerProgress(BinaryDisassemblerStage.Disassembling));

                // Force the symbol path in the disasm context so that it picks the PDB file
                // in case it's in another directory (downloaded from a symbol server for ex).
                var envVariables = new Dictionary<string, string>() {
                    { "_NT_SYMBOL_PATH", Utils.TryGetDirectoryName(debugPath) }
                };

                if (!Utils.ExecuteTool(options_.DissasemblerPath, disasmArgs, cancelableTask, envVariables)) {
                    Trace.TraceError($"Disassembler task {ObjectTracker.Track(cancelableTask)}: Failed to execute disassembler: ${options_.DissasemblerPath}");
                    return false;
                }

                if (!File.Exists(dissasemblyPath)) {
                    Trace.TraceError($"Disassembler task {ObjectTracker.Track(cancelableTask)}: Output file not found: ${dissasemblyPath}");
                    return false;
                }

                // Done if no post-processing of the output is needed.
                if(string.IsNullOrEmpty(options_.PostProcessorPath)) {
                    return true;
                }

                var args = options_.PostProcessorArguments.Replace(DEST_PLACEHOLDER, postprocessingPath)
                                                          .Replace(SOURCE_PLACEHOLDER, dissasemblyPath)
                                                          .Replace(DEBUG_PLACEHOLDER, debugPath);
                progressCallback?.Invoke(new BinaryDisassemblerProgress(BinaryDisassemblerStage.PostProcessing));

                if (!Utils.ExecuteTool(options_.PostProcessorPath, args, cancelableTask)) {
                    Trace.TraceError($"Disassembler task {ObjectTracker.Track(cancelableTask)}: Failed to execute post-processing tool: ${options_.DissasemblerPath}");
                    return false;
                }

                if (!File.Exists(postprocessingPath)) {
                    Trace.TraceError($"Disassembler task {ObjectTracker.Track(cancelableTask)}: Postprocessing output file not found: ${postprocessingPath}");
                    return false;
                }

                return true;
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed to run dissasembler for {exePath}: {ex.Message}");
            }

            return false;
        }
    }
}
