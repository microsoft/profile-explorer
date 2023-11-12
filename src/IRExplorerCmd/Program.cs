// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IRExplorerCore.UTC;
using IRExplorerUI.Scripting;

namespace IRExplorerCmd;

class Program {
  private static void CollectBaseDiffFiles(string root, string baseDir, string diffDir, string target,
                                           List<Tuple<string, string, string>> results) {
    foreach (string dir in Directory.GetDirectories(root)) {
      CollectBaseDiffFiles(dir, baseDir, diffDir, target, results);
    }

    string dirName = Path.GetRelativePath(baseDir, root);
    string baseDirName = Path.Combine(baseDir, dirName, $"master.{target}");
    string diffDirName = Path.Combine(diffDir, dirName, $"diffs.{target}");

    if (!Directory.Exists(baseDirName) ||
        !Directory.Exists(diffDirName)) {
      return;
    }

    foreach (string file in Directory.GetFiles(baseDirName, "*.asm")) {
      string fileName = Path.GetFileName(file);
      string diffFile = Path.Combine(diffDirName, fileName);

      if (!File.Exists(diffFile)) {
        continue;
      }

      results.Add(new Tuple<string, string, string>(file, diffFile, dirName));
    }
  }

  private static bool CompareBaseDiffFiles(string baseFilePath, string diffFilePath, string testName,
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

    //Console.WriteLine(scriptSession.OutputText);
    //Console.ReadKey();
    return false;
  }

  private static void Main(string[] args) {
    string baseDir = args[0];
    string diffDir = args[1];
    string reportFile = args[2];
    string target = args[3];
    string scriptPath = @"C:\test\irx-compare.cs";
    string reportHeader =
      "Test, Function, Blocks, B, D, Instrs, B, D, Stores, B, D, Loads, B, D, Loop Loads, B, D, Symbol Loads, B, D, Address exprs, B, D\n";

    var pairs = new List<Tuple<string, string, string>>();

    if (!Directory.Exists(baseDir) ||
        !Directory.Exists(diffDir)) {
      // Assume that two files should be compared.
      string baseDirName = Path.GetRelativePath(baseDir, baseDir);
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
          string relativeTestPath = Path.Combine(pair.Item3, Path.GetFileName(pair.Item1));
          CompareBaseDiffFiles(pair.Item1, pair.Item2, relativeTestPath, script, reportFile, session);
        }
        finally {
          concurrencySemaphore.Release();

          int doneNow = Interlocked.Increment(ref done);
          double percentage = doneNow / (double)pairs.Count * 100;

          Interlocked.MemoryBarrier();
          double prevPercentage = percentageDone;

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
      string text = File.ReadAllText(reportFile);
      File.WriteAllText(reportFile, reportHeader + text);
    }
  }
}
