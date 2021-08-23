// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
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
    public class BinaryDissasemblerOptions : SettingsBase {
        private const string DEFAULT_DISASM_NAME = "dumpbin.exe";
        private const string DEFAULT_DISASM_ARGS = "/disasm /out:$DST $SRC";

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

        public BinaryDissasemblerOptions() {
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
            PostProcessorPath = "";
            PostProcessorArguments = "$SRC $DST";
            CacheDissasembly = true;
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
            return StateSerializer.Deserialize<BinaryDissasemblerOptions>(serialized);
        }
    }

    public enum BinaryDissasemblerStage {
        Dissasembling,
        PostProcessing
    }

    public class BinaryDissasemblerProgress {
        public BinaryDissasemblerProgress(BinaryDissasemblerStage stage) {
            Stage = stage;
        }

        public BinaryDissasemblerStage Stage { get; set; }
        public int Total { get; set; }
        public int Current { get; set; }
    }

    public delegate void BinaryDissasemblerProgressHandler(BinaryDissasemblerProgress info);

    public class BinaryDissasembler {
        private const string DEST_PLACEHOLDER = "$DST";
        private const string SOURCE_PLACEHOLDER = "$SRC";
        private const string DEBUG_PLACEHOLDER = "$DBG";
        private BinaryDissasemblerOptions options_;

        public BinaryDissasembler(BinaryDissasemblerOptions options) {
            options_ = options;
        }

        public string Dissasemble(string exePath, string debugPath,
                                  BinaryDissasemblerProgressHandler progressCallback,
                                  CancelableTask cancelableTask) {
            try {
                var outputFilePath = Path.GetTempFileName();
                var finalFilePath = outputFilePath;

                if(!string.IsNullOrEmpty(options_.PostProcessorPath)) {
                    finalFilePath = Path.GetTempFileName();
                }

                if(Dissasemble(exePath, debugPath, outputFilePath, finalFilePath, 
                               progressCallback, cancelableTask)) {
                    return finalFilePath;
                }
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed to run dissasembler for {exePath}: {ex.Message}");
            }

            return null;
        }

        public Task<string> DissasembleAsync(string exePath, string debugPath,
                          BinaryDissasemblerProgressHandler progressCallback = null,
                          CancelableTask cancelableTask = null) {
            return Task.Run(() => Dissasemble(exePath, debugPath, progressCallback, cancelableTask));
        }

        public bool Dissasemble(string exePath, string debugPath, string dissasemblyPath, 
                                string postprocessingPath, BinaryDissasemblerProgressHandler progressCallback,
                                CancelableTask cancelableTask) {
            try {
                var disasmArgs = options_.DissasemblerArguments.Replace(DEST_PLACEHOLDER, dissasemblyPath)
                                                               .Replace(SOURCE_PLACEHOLDER, exePath)
                                                               .Replace(DEBUG_PLACEHOLDER, debugPath);
                progressCallback?.Invoke(new BinaryDissasemblerProgress(BinaryDissasemblerStage.Dissasembling));

                if (!Utils.ExecuteTool(options_.DissasemblerPath, disasmArgs, cancelableTask)) {
                    return false;
                }

                if (!File.Exists(dissasemblyPath)) {
                    Trace.TraceError($"Dissasembler task {ObjectTracker.Track(cancelableTask)}: Output file not found: ${dissasemblyPath}");
                    return false;
                }

                // Done if no post-processing of the output is needed.
                if(string.IsNullOrEmpty(options_.PostProcessorPath)) {
                    return true;
                }

                var args = options_.PostProcessorArguments.Replace(DEST_PLACEHOLDER, postprocessingPath)
                                                          .Replace(SOURCE_PLACEHOLDER, dissasemblyPath)
                                                          .Replace(DEBUG_PLACEHOLDER, debugPath);
                progressCallback?.Invoke(new BinaryDissasemblerProgress(BinaryDissasemblerStage.PostProcessing));

                if (!Utils.ExecuteTool(options_.PostProcessorPath, args, cancelableTask)) {
                    return false;
                }

                if (!File.Exists(postprocessingPath)) {
                    Trace.TraceError($"Dissasembler task {ObjectTracker.Track(cancelableTask)}: Postprocessing output file not found: ${postprocessingPath}");
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
