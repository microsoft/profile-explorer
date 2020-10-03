using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IRExplorerCore.UTC;
using IRExplorerUI.Scripting;

namespace IRExplorerCmd {
    class Program {
        static void CollectBaseDiffFiles(string root, string baseDir, string diffDir, string target,
                                            List<Tuple<string, string, string>> results) {
            foreach (var dir in Directory.GetDirectories(root)) {
                CollectBaseDiffFiles(dir, baseDir, diffDir, target, results);
            }

            var dirName = Path.GetRelativePath(baseDir, root);
            var baseDirName = Path.Combine(baseDir, dirName, $"master.{target}");
            var diffDirName = Path.Combine(diffDir, dirName, $"diffs.{target}");

            if (!Directory.Exists(baseDirName) ||
                !Directory.Exists(diffDirName)) {
                return;
            }

            foreach (var file in Directory.GetFiles(baseDirName, "*.asm")) {
                string fileName = Path.GetFileName(file);
                string diffFile = Path.Combine(diffDirName, fileName);

                if (!File.Exists(diffFile)) {
                    continue;
                }

                results.Add(new Tuple<string, string, string>(file, diffFile, dirName));
            }
        }

        static bool CompareBaseDiffFiles(string baseFilePath, string diffFilePath, string testName,
                                         Script script, string scriptOutPath, ConsoleSession session) {
            Console.WriteLine("Loading files");

            if (File.Exists(baseFilePath) && File.Exists(diffFilePath)) {
                if (!session.LoadMainDocument(baseFilePath, false)) {
                    Console.WriteLine($"Failed to load base document: {baseFilePath}");
                    return false;
                }

                if (!session.LoadDiffDocument(diffFilePath, false)) {
                    Console.WriteLine($"Failed to load diff document: {diffFilePath}");
                    return false;
                }
            }

            if (!session.IsInTwoDocumentsDiffMode) {
                return false;
            }

            var scriptSession = new ScriptSession(null, session) {
                SilentMode = true,
                SessionName = scriptOutPath
            };

            Console.WriteLine("Starting script");
            scriptSession.AddVariable("TestName", testName);
            scriptSession.AddVariable("ReportFilePath", scriptOutPath);

            if (script.Execute(scriptSession)) {
                // Console.WriteLine($"Result: {script.ScriptResult}");

                if (script.ScriptException != null) {
                    Console.WriteLine($"Exception: {script.ScriptException.Message}");
                }

                return true;
            }
            else {
                //Console.WriteLine(scriptSession.OutputText);
                //Console.ReadKey();
                return false;
            }
        }

        static void Main(string[] args) {
            var baseDir = args[0];
            var diffDir = args[1];
            var reportFile = args[2];
            var target = args[3];
            var scriptPath = @"C:\test\irx-compare.cs";
            var reportHeader = "Test, Function, Blocks, B, D, Instrs, B, D, Stores, B, D, Loads, B, D, Loop Loads, B, D, Symbol Loads, B, D, Address exprs, B, D\n";

            var pairs = new List<Tuple<string, string, string>>();

            if (!Directory.Exists(baseDir) ||
                !Directory.Exists(diffDir)) {
                // Assume that two files should be compared.
                var baseDirName = Path.GetRelativePath(baseDir, baseDir);
                pairs.Add(new Tuple<string, string, string>(baseDir, diffDir, baseDirName));
            }
            else {
                CollectBaseDiffFiles(baseDir, baseDir, diffDir, target, pairs);
            }

            //foreach (var pair in pairs)
            //{
            //    Console.WriteLine($"{pair.Item1}\n{pair.Item2}\n{pair.Item3}\n--------\n");
            //}

            var sw = Stopwatch.StartNew();
            int maxConcurrency = Math.Min(16, Environment.ProcessorCount);
            var tasks = new Task[pairs.Count];
            using var concurrencySemaphore = new SemaphoreSlim(maxConcurrency);
            int index = 0;
            int done = 0;
            double percentageDone = 0;

            var script = Script.LoadFromFile(scriptPath);

            if (script == null) {
                Console.WriteLine($"Failed to load script {scriptPath}");
                return;
            }

            foreach (var pair in pairs) {
                concurrencySemaphore.Wait();

                tasks[index++] = Task.Run(() => {
                    try {
                        var compilerInfo = new ConsoleCompilerInfoProvider("UTC", new UTCCompilerIRInfo());
                        using var session = new ConsoleSession(compilerInfo);
                        var relativeTestPath = Path.Combine(pair.Item3, Path.GetFileName(pair.Item1));
                        CompareBaseDiffFiles(pair.Item1, pair.Item2, relativeTestPath, script, reportFile, session);
                    }
                    finally {
                        concurrencySemaphore.Release();

                        int doneNow = Interlocked.Increment(ref done);
                        double percentage = ((double)doneNow / (double)pairs.Count) * 100;

                        Interlocked.MemoryBarrier();
                        var prevPercentage = percentageDone;

                        if (percentage - prevPercentage >= 5) {
                            Console.WriteLine($"{percentage:0.00}%");
                            Interlocked.Exchange(ref percentageDone, percentage);
                        }
                    }
                });
            }

            Task.WaitAll(tasks);
            sw.Stop();
            Console.WriteLine($"Done in {sw.Elapsed.TotalSeconds} s");

            if (File.Exists(reportFile)) {
                var text = File.ReadAllText(reportFile);
                File.WriteAllText(reportFile, reportHeader + text);
            }
        }
    }
}
