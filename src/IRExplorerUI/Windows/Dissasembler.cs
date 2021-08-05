// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.IO;
using IRExplorerCore;

// Save recent exec files with pdb pairs
// Caching that checks for same CRC
// Try to fill PDB path

namespace IRExplorerUI {
    public class DissasemblerOptions {
        public string DissasemblerPath { get; set; }
        public string DissasemblerArguments { get; set; }
        public string PostProcessorPath { get; set; }
        public string PostProcessorArguments { get; set; }
        public bool CacheDissasembly { get; set; }
    }

    public class Dissasembler {
        private const string DEST_PLACEHOLDER = "$DST";
        private const string SOURCE_PLACEHOLDER = "$SRC";
        private const string DEBUG_PLACEHOLDER = "$DBG";
        private DissasemblerOptions options_;

        public Dissasembler(DissasemblerOptions options) {
            options_ = options;
        }

        public string Dissasemble(string exePath, string debugPath, CancelableTask task) {
            try {
                var outputFilePath = Path.GetTempFileName();
                var finalFilePath = outputFilePath;

                if(!string.IsNullOrEmpty(options_.PostProcessorPath)) {
                    finalFilePath = Path.GetTempFileName();
                }

                if(Dissasemble(exePath, debugPath, outputFilePath, finalFilePath, task)) {
                    return finalFilePath;
                }
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed to run dissasembler for {exePath}: {ex.Message}");
            }

            return null;
        }

        public bool Dissasemble(string exePath, string debugPath, string dissasemblyPath, string postprocessingPath,
                                CancelableTask task) {
            try {
                var disasmArgs = options_.DissasemblerArguments.Replace(DEST_PLACEHOLDER, dissasemblyPath)
                                                               .Replace(SOURCE_PLACEHOLDER, exePath)
                                                               .Replace(DEBUG_PLACEHOLDER, debugPath);

                if (!ExecuteTool(options_.DissasemblerPath, disasmArgs, task)) {
                    return false;
                }

                if (!File.Exists(dissasemblyPath)) {
                    Trace.TraceError($"Dissasembler task {ObjectTracker.Track(task)}: Output file not found: ${dissasemblyPath}");
                    return false;
                }

                // Done if no post-processing of the output is needed.
                if(string.IsNullOrEmpty(options_.PostProcessorPath)) {
                    return true;
                }

                var args = options_.PostProcessorArguments.Replace(DEST_PLACEHOLDER, postprocessingPath)
                                                          .Replace(SOURCE_PLACEHOLDER, dissasemblyPath)
                                                          .Replace(DEBUG_PLACEHOLDER, debugPath);

                if (!ExecuteTool(options_.PostProcessorPath, args, task)) {
                    return false;
                }

                if (!File.Exists(postprocessingPath)) {
                    Trace.TraceError($"Dissasembler task {ObjectTracker.Track(task)}: Postprocessing output file not found: ${postprocessingPath}");
                    return false;
                }

                return true;
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed to run dissasembler for {exePath}: {ex.Message}");
            }

            return false;
        }

        private bool ExecuteTool(string path, string args, CancelableTask task) {
            if (!File.Exists(path)) {
                return false;
            }

            var disasmProcInfo = new ProcessStartInfo(path) {
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = false,
                RedirectStandardOutput = false
            };

            using var disasmProc = new Process { StartInfo = disasmProcInfo, EnableRaisingEvents = true };
            disasmProc.Start();

            do {
                disasmProc.WaitForExit(100);

                if (task.IsCanceled) {
                    Trace.TraceWarning($"Dissasembler task {ObjectTracker.Track(task)}: Canceled");
                    disasmProc.Kill();
                    return false;
                }
            } while (!disasmProc.HasExited);

            if (disasmProc.ExitCode != 0) {
                Trace.TraceError($"Dissasembler task {ObjectTracker.Track(task)}: Failed with error code: {disasmProc.ExitCode}");
                return false;
            }

            return true;
        }
    }
}
