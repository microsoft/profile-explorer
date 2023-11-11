// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using IRExplorerCore;
using IRExplorerCore.IR;
using IRExplorerCore.IR.Tags;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using IRExplorerCore.Analysis;
using IRExplorerCore.Graph;
using IRExplorerCore.UTC;

namespace CompilerStudio {
    internal class Program {
        private static readonly object LockObject = new object();

        private static void Main(string[] args) {
            string filePath = args[0];

            //if (!File.Exists(filePath)) {
            //    Console.WriteLine($"File {filePath} not found!");

            //    if (Directory.Exists(filePath)) {
            //        var files = Directory.EnumerateFiles(filePath)
            //                             .Where(file => file.ToLower().EndsWith(".log") ||
            //                                    file.ToLower().EndsWith(".ir") ||
            //                                    file.ToLower().EndsWith(".error") ||
            //                                    file.ToLower().EndsWith(".null") ||
            //                                    file.ToLower().EndsWith(".crash"))
            //                            .ToList();

            //        var tasks = new List<Process>();

            //        foreach (string file in files) {
            //            Console.WriteLine($"Starting new tester for {file}");
            //            var psi = new ProcessStartInfo("ConsoleTester.exe", file) {
            //                UseShellExecute = false,
            //                WindowStyle = ProcessWindowStyle.Minimized
            //            };

            //            tasks.Add(Process.Start(psi));
            //        }

            //        Console.WriteLine("Waiting for child testers...");

            //        foreach (var task in tasks) {
            //            task.WaitForExit();
            //            task.Close();
            //        }

            //        Console.WriteLine("Done");
            //        return;
            //    }
            //    else {
            //        Console.WriteLine($"Invalid path {filePath}");
            //        return;
            //    }
            //}

            Console.WriteLine("Loading file");

            SectionReaderBase reader;
            IRTextSummary summary;
            int failed = 0;

            try {
                var start = DateTime.Now;
                reader = new UTCSectionReader(filePath, expectSectionHeaders: false);
                summary = reader.GenerateSummary(null, null);
                var end = DateTime.Now;
                Console.WriteLine($"Summary done in {(end - start).TotalMilliseconds} ms");
            }
            catch (Exception ex) {
                Console.WriteLine($"Failed to load {filePath}: {ex}");
                return;
            }

            var compilerInfo = new UTCCompilerIRInfo();
            compilerInfo.Mode = IRMode.x86_64;
            var sw = Stopwatch.StartNew();

            int times = 10;
            int total = 0;


            for (int k = 0; k < times; k++) {
                Console.WriteLine($"Iteration {k}");

                foreach (var func in summary.Functions) {
                    //Parallel.ForEach(summary.Functions,
                    //             new ParallelOptions { MaxDegreeOfParallelism = 1 }, 
                    //             func => {
                    foreach (var section in func.Sections) {
                        bool sectionFailed = false;
                        string text = null;
                        string failureType = "crash";

                        var errorHandler = new ParsingErrorHandler();
                        //errorHandler.ThrowOnError = true;

                        try {
                            text = reader.GetSectionText(section);
                            var sectionParser = new UTCSectionParser(compilerInfo, errorHandler);
                            var function = sectionParser.ParseSection(section, text);

                            if (function != null) {
                                if (function.Blocks.Count > 1) {
                                    var p = new FlowGraphPrinter(function);
                                    string s = p.PrintGraph();
                                    total = s.Length;

                                    var domTree = new DominatorAlgorithm(function, DominatorAlgorithmOptions.BuildQueryCache |
                                                                                    DominatorAlgorithmOptions.BuildTree);
                                    foreach (var b1 in function.Blocks) {
                                        foreach (var b2 in function.Blocks) {
                                            if (domTree.Dominates(b1, b2)) {
                                                total++;
                                            }
                                        }

                                        foreach (var domBlock in domTree.EnumerateDominators(b1)) {
                                            total += domBlock.Number;
                                        }
                                    }

                                    var refs = new ReferenceFinder(function, compilerInfo);

                                    foreach (var op in refs.EnumerateAllOperands()) {
                                        total += op.TextLength;

                                        //var opRefs = refs.FindAllUses(op);

                                        //foreach (var opRef in opRefs) {
                                        //    total += opRef.TextLength;
                                        //}
                                    }


                                }

                                //File.WriteAllText(Guid.NewGuid().ToString() + ".in", s);

                                // build domtree
                                // find refs

                                if (errorHandler.HadParsingErrors) {
                                    //Console.WriteLine($"=> Parsing errors at {func.Name}:{section.Name}");
                                    //Console.Out.Flush();

                                    //if (!Debugger.IsAttached) {
                                    //    Console.WriteLine("Parsing Errors:");

                                    //    foreach (var error in errorHandler.ParsingErrors) {
                                    //        Console.WriteLine(error);
                                    //    }
                                    //}

                                    //sectionFailed = true;
                                    //failureType = "error";
                                }
                            }
                            else {
                                Console.WriteLine($"=> Null func at {func.Name}:{section.Name}");
                                Console.Out.Flush();
                                sectionFailed = true;
                                failureType = "null";
                            }
                        }
                        catch (Exception) {
                            Console.WriteLine($"=> Exception at {func.Name}:{section.Name}.");
                            sectionFailed = true;
                            failureType = "crash";
                        }

                        if (sectionFailed) {
                            failed++;
                        }

                        //if (sectionFailed && text != null) {
                        //    if (!section.Name.Contains("Vectorizer") &&
                        //        !section.Name.Contains("vectorizer")) {
                        //        try {
                        //            lock (LockObject) {
                        //                failed = Interlocked.Increment(ref failed);
                        //                string savePath = Path.GetDirectoryName(filePath);
                        //                string fileName = Path.GetFileNameWithoutExtension(filePath);
                        //                savePath = Path.Combine(savePath, fileName);

                        //                if (!Directory.Exists(savePath)) {
                        //                    Directory.CreateDirectory(savePath);
                        //                }

                        //                savePath = Path.Combine(savePath, $"{failed}.{failureType}");
                        //                File.WriteAllText(savePath, text);
                        //            }
                        //        }
                        //        catch (Exception ex2) {
                        //            Console.WriteLine($"Failed to create crash file: {ex2}");
                        //        }
                        //    }
                        //}
                    }
                }
            }

            sw.Stop();
            Console.WriteLine($"Done in {sw.Elapsed.TotalSeconds} s");
            Console.WriteLine($"Failed: {failed}");
            Console.WriteLine($"Total: {total}");
        }
    }
}
