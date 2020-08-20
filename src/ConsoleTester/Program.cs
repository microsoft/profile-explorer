// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IRExplorerCore;
using IRExplorerCore.Graph;
using IRExplorerCore.UTC;

namespace CompilerStudio {
    internal class Program {
        private static readonly object LockObject = new object();

        private static void Main(string[] args) {
            string filePath = args[0];

            Trace.Listeners.Add(new TextWriterTraceListener(@"C:\test\trace.log"));
            Trace.AutoFlush = true;

            Trace.TraceInformation("abc");
            Trace.TraceWarning("warn");
            Trace.TraceError("error");
            Trace.WriteLine("a line");


            if (!File.Exists(filePath)) {
                Console.WriteLine($"File {filePath} not found!");

                if (Directory.Exists(filePath)) {
                    var files = Directory.EnumerateFiles(filePath)
                                         .Where(file => file.ToLower().EndsWith(".log") ||
                                                file.ToLower().EndsWith(".ir") ||
                                                file.ToLower().EndsWith(".error") ||
                                                file.ToLower().EndsWith(".null") ||
                                                file.ToLower().EndsWith(".crash"))
                                        .ToList();

                    var tasks = new List<Process>();

                    foreach (string file in files) {
                        Console.WriteLine($"Starting new tester for {file}");
                        var psi = new ProcessStartInfo("ConsoleTester.exe", file) {
                            UseShellExecute = false,
                            WindowStyle = ProcessWindowStyle.Minimized
                        };

                        tasks.Add(Process.Start(psi));
                    }

                    Console.WriteLine("Waiting for child testers...");

                    foreach (var task in tasks) {
                        task.WaitForExit();
                        task.Close();
                    }

                    Console.WriteLine("Done");
                    return;
                }
                else {
                    Console.WriteLine($"Invalid path {filePath}");
                    return;
                }
            }

            Console.WriteLine("Loading file");

            SectionReaderBase reader;
            IRTextSummary summary;
            int failed = 0;

            try {
                var start = DateTime.Now;
                reader = new UTCSectionReader(filePath, expectSectionHeaders: false);
                summary = reader.GenerateSummary(null);
                var end = DateTime.Now;
                Console.WriteLine($"Summary done in {(end - start).TotalMilliseconds} ms");
            }
            catch (Exception ex) {
                Console.WriteLine($"Failed to load {filePath}: {ex}");
                return;
            }

            var sw = Stopwatch.StartNew();

            Parallel.ForEach(summary.Functions,
                             new ParallelOptions { MaxDegreeOfParallelism = 1 },
                             func => {
                                 foreach (var section in func.Sections) {
                                     bool sectionFailed = false;
                                     string text = null;
                                     string failureType = "crash";

                                     var errorHandler = new ParsingErrorHandler();
                                     //errorHandler.ThrowOnError = true;

                                     try {
                                         //if((count % 1000) == 0) {
                                         //    Console.WriteLine($"at {count}");
                                         //    Console.Out.Flush();
                                         //}

                                         text = reader.GetSectionText(section);
                                         var sectionParser = new UTCSectionParser(errorHandler);
                                         var function = sectionParser.ParseSection(section, text);

                                         if (function != null) {
                                             var p = new FlowGraphPrinter(function);
                                             string s = p.PrintGraph();
                                             File.WriteAllText(Guid.NewGuid().ToString() + ".in", s);


                                             if (errorHandler.HadParsingErrors) {
                                                 //Console.WriteLine($"=> Parsing errors at {func.Name}:{section.Name}");
                                                 //Console.Out.Flush();

                                                 if (!Debugger.IsAttached) {
                                                     Console.WriteLine("Parsing Errors:");

                                                     foreach (var error in errorHandler.ParsingErrors) {
                                                         Console.WriteLine(error);
                                                     }
                                                 }

                                                 sectionFailed = true;
                                                 failureType = "error";
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


                                     if (sectionFailed && text != null) {
                                         if (!section.Name.Contains("Vectorizer") &&
                                             !section.Name.Contains("vectorizer")) {
                                             try {
                                                 lock (LockObject) {
                                                     failed = Interlocked.Increment(ref failed);
                                                     string savePath = Path.GetDirectoryName(filePath);
                                                     string fileName = Path.GetFileNameWithoutExtension(filePath);
                                                     savePath = Path.Combine(savePath, fileName);

                                                     if (!Directory.Exists(savePath)) {
                                                         Directory.CreateDirectory(savePath);
                                                     }

                                                     savePath = Path.Combine(savePath, $"{failed}.{failureType}");
                                                     File.WriteAllText(savePath, text);
                                                 }
                                             }
                                             catch (Exception ex2) {
                                                 Console.WriteLine($"Failed to create crash file: {ex2}");
                                             }
                                         }
                                     }
                                 }
                             });

            sw.Stop();
            Console.WriteLine($"Done in {sw.Elapsed.TotalSeconds} s");
            Console.WriteLine($"Failed: {failed}");
        }
    }
}
