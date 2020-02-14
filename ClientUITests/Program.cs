// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;

namespace ClientUITests {
    static class Utils {
        public static List<int> GetRandomSubsetInRange(int subsetLength, int rangeMin, int rangeMax) {
            var random = new Random(31);
            return Enumerable.Range(rangeMin, rangeMax)
                             .OrderBy(t => random.Next())
                             .Take(subsetLength).ToList();
        }

        public static List<T> GetRandomElements<T>(this IEnumerable<T> list, int elementsCount) {
            return list.OrderBy(arg => Guid.NewGuid()).Take(elementsCount).ToList();
        }
    }

    //? TODO: Diff test needed

    class Program {
        static void Main(string[] args) {
            Console.WriteLine("Starting UI tests");
            int startFuncIndex = 0;
            bool failed = true;

            while (failed) {
                failed = false;

                if (startFuncIndex != 0) {
                    Console.WriteLine($"=> Resuming tests from function {startFuncIndex}");
                }

                var psi = new ProcessStartInfo(@"Client.exe", args[0]);
                var app = FlaUI.Core.Application.Launch(psi);
                app.WaitWhileMainHandleIsMissing();

                using (var automation = new UIA3Automation()) {
                    int lastFuncIndex = startFuncIndex;
                    int lastSectionIndex = 0;
                    string lastFuncName = "";
                    string lastSectionName = "";

                    try {
                        var window = app.GetMainWindow(automation);
                        var title = window.Title;

                        while (title.Contains("Loading")) {
                            Thread.Sleep(1000);
                            title = window.Title;
                        }

                        var autoButton = window.FindFirstDescendant(cf => cf.ByAutomationId("AutomationButton")).AsButton();
                        var funcList = window.FindFirstDescendant(cf => cf.ByAutomationId("AutoFunctionList")).AsDataGridView();
                        funcList.Focus();

                        var rows = funcList.Patterns.Grid.Pattern;

                        for (int i = startFuncIndex; i < rows.RowCount; i++) {
                            funcList.Focus();
                            var rowItem = rows.GetItem(i, 0).Parent;
                            lastFuncIndex = i;
                            lastFuncName = rowItem.Name;

                            rowItem.Patterns.SelectionItem.Pattern.Select();
                            Keyboard.Type(VirtualKeyShort.ENTER);

                            var sectionList = window.FindFirstDescendant(cf => cf.ByAutomationId("AutoSectionList")).AsDataGridView();
                            sectionList.Focus();
                            var sectionRows = sectionList.Patterns.Grid.Pattern;
                            int sectionRowCount = Math.Min(sectionRows.RowCount, 100);

                            for (int j = 0; j < sectionRowCount; j++) {
                                sectionList.Focus();
                                var item = sectionRows.GetItem(j, 0).Parent;
                                lastSectionIndex = j;
                                lastSectionName = item.Name;

                                item.Patterns.SelectionItem.Pattern.Select();
                                app.WaitWhileBusy();

                                Keyboard.Type(VirtualKeyShort.ENTER);
                                WaitForFlowGraph(app);
                            }

                            // Reopen a subset of section to test save/restore.
                            if (sectionRowCount >= 4) {
                                var revisitNumber = sectionRowCount / 4;
                                var subset = Utils.GetRandomSubsetInRange(revisitNumber, 0, sectionRowCount - 1);

                                foreach (var j in subset) {
                                    if (j >= sectionRowCount) {
                                        Console.WriteLine($"Invalid index {j} for max {sectionRowCount}");
                                        continue;
                                    }

                                    sectionList.Focus();
                                    var item = sectionRows.GetItem(j, 0).Parent;
                                    lastSectionIndex = j;
                                    lastSectionName = item.Name;

                                    item.Patterns.SelectionItem.Pattern.Select();
                                    app.WaitWhileBusy();

                                    Keyboard.Type(VirtualKeyShort.ENTER);
                                    WaitForFlowGraph(app);

                                    while (autoButton.IsEnabled) {
                                        Keyboard.Type(VirtualKeyShort.F8);
                                        app.WaitWhileBusy();
                                    }

                                    app.WaitWhileBusy();
                                }

                                foreach (var j in subset) {
                                    if (j >= sectionRowCount) {
                                        Console.WriteLine($"Invalid index {j} for max {sectionRowCount}");
                                        continue;
                                    }

                                    sectionList.Focus();
                                    var item = sectionRows.GetItem(j, 0).Parent;
                                    lastSectionIndex = j;
                                    lastSectionName = item.Name;

                                    item.Patterns.SelectionItem.Pattern.Select();
                                    app.WaitWhileBusy();

                                    Keyboard.Type(VirtualKeyShort.ENTER);
                                    WaitForFlowGraph(app);
                                    app.WaitWhileBusy();
                                }
                            }
                        }

                        app.Close();
                    }
                    catch (Exception ex) {
                        failed = true;
                        startFuncIndex = lastFuncIndex + 1;


                        Console.WriteLine($"Failed running UI test: {ex}");
                        Console.WriteLine($"Failed at {lastFuncIndex}:{lastSectionIndex}");
                        Console.WriteLine($"Failed at {lastFuncName}:{lastSectionName}");


                        try {
                            Thread.Sleep(10000);
                            app.Kill();
                            Thread.Sleep(10000);
                        }
                        catch { }
                    }
                }
            }
        }

        private static void WaitForFlowGraph(Application app) {
            if (!app.WaitWhileBusy(TimeSpan.FromSeconds(30))) {
                var dotInstances = Process.GetProcessesByName("dot");

                if (dotInstances.Length > 0) {
                    foreach (var p in dotInstances) {
                        p.Kill();
                    }

                    app.WaitWhileBusy(TimeSpan.FromSeconds(5));
                }
            }
        }
    }
}
