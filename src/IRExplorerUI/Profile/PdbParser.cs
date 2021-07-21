
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace IRExplorerUI.Profile {
    class PdbParser {
        private readonly string cvdumpPath_;
        private readonly Regex localProcRegex = new Regex(@"S_LPROC32: \[[0-9A-F]*\:([0-9A-F]*)\], .*, (.*)", RegexOptions.Compiled);
        private readonly Regex pub32ProcRegex = new Regex(@"S_PUB32: \[[0-9A-F]*\:([0-9A-F]*)\], Flags: [0-9A-F]*\, (.*)", RegexOptions.Compiled);

        public PdbParser(string cvdumpPath) {
            cvdumpPath_ = cvdumpPath;
        }

        public IEnumerable<(string, long)> Parse(string symbolPath) {
            var allText = RunCvdump(symbolPath);

            if (allText != null) {
                var textLines = allText.Split(new[] {"\r\n", "\r", "\n"}, StringSplitOptions.RemoveEmptyEntries);

                foreach (var line in textLines) {
                    var matches = pub32ProcRegex.Matches(line);

                    if (matches.Count == 0) {
                        matches = localProcRegex.Matches(line);
                        if (matches.Count == 0) {
                            continue;
                        }
                    }

                    var address = Convert.ToInt64(matches[0].Groups[1].Value, 16);
                    var funcName = matches[0].Groups[2].Value;
                    yield return (funcName, address);
                }
            }
        }

        public Dictionary<long, string> ParseFunctionAddressMap(string symbolPath) {
            var map = new Dictionary<long, string>();
            
            foreach (var pair in Parse(symbolPath)) {
                map[pair.Item2] = pair.Item1;
            }

            return map;
        }

        private string RunCvdump(string symbolPath) {
            var outputText = new StringBuilder(1024 * 32);

            var psi = new ProcessStartInfo(cvdumpPath_) {
                Arguments = $"-p -s \"{symbolPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = false,
                RedirectStandardOutput = true
            };

            //? TODO: Put path between " to support whitespace in the path.

            try {
                using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

                process.OutputDataReceived += (sender, e) => {
                    outputText.AppendLine(e.Data);
                };

                process.Start();
                process.BeginOutputReadLine();

                do {
                    process.WaitForExit(200);
                } while (!process.HasExited);

                process.CancelOutputRead();

                if (process.ExitCode != 0) {
                    Trace.TraceError($"Bad cvdump exit code: {process.ExitCode}");
                    return null;
                }
            }
            catch (Exception ex) {
                Trace.TraceError($"Error running cvdump: {ex.Message}");
                return null;
            }

            return outputText.ToString();
        }
    }
}