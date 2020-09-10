using System;
using System.IO;
using IRExplorerUI;
using IRExplorerCore.UTC;
using IRExplorerUI.Scripting;

namespace IRExplorerCmd {
    class Program {
        static void Main(string[] args) {
            //? TODO: https://docs.microsoft.com/en-us/archive/msdn-magazine/2019/march/net-parse-the-command-line-with-system-commandline
            var compilerInfo = new ConsoleCompilerInfoProvider("UTC", new UTCCompilerIRInfo());
            var session = new ConsoleSessionManager(compilerInfo);

            if (args.Length >= 2) {
                string baseFilePath = args[0];
                string diffFilePath = args[1];

                if (File.Exists(baseFilePath) && File.Exists(diffFilePath)) {
                    if (!session.LoadMainDocument(baseFilePath)) {
                        Console.WriteLine($"Failed to load base document: {baseFilePath}");
                    }

                    if (!session.LoadDiffDocument(diffFilePath)) {
                        Console.WriteLine($"Failed to load diff document: {diffFilePath}");
                    }
                }

                if (args.Length >= 4 && session.IsInTwoDocumentsDiffMode) {
                    if (args[2].EndsWith("script")) {
                        string scriptPath = args[3];
                        var script = Script.LoadFromFile(scriptPath);

                        if (script == null) {
                            Console.WriteLine($"Failed to load script {scriptPath}");
                        }

                        string scriptOutPath = "";

                        if (args.Length >= 6) {
                            if (args[4].EndsWith("out")) {
                                scriptOutPath = args[5];
                            }
                        }

                        var scriptSession = new ScriptSession(null, session) {
                            SilentMode = true,
                            SessionName = scriptOutPath
                        };

                        if (script.Execute(scriptSession)) {
                            Console.WriteLine($"Result: {script.ScriptResult}");

                            if (script.ScriptException != null) {
                                Console.WriteLine($"Exception: {script.ScriptException.Message}");
                            }
                        }
                    }
                }
            }
        }
    }
}
