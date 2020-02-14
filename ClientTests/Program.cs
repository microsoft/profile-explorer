// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using FlaUI.UIA3;

namespace ClientTests {
    class Program {
        static void Main(string[] args) {
            Console.WriteLine("Starting UI tests");

            var app = FlaUI.Core.Application.Launch(@"C:\personal\projects\compiler_studio\Client\bin\Debug\netcoreapp3.1\Client.exe");
            using (var automation = new UIA3Automation()) {
                var window = app.GetMainWindow(automation);
                Console.WriteLine(window.Title);
            }
        }
    }
}
