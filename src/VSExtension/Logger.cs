// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.Shell.Interop;

namespace IRExplorerExtension {
    public static class Logger {
        private static IVsOutputWindowPane panel_;
        private static IServiceProvider provider_;
        private static Guid panelId_;
        private static string name_;
        private static object lockObject_ = new object();

        public static void Initialize(IServiceProvider provider, string name) {
            provider_ = provider;
            name_ = name;
        }

        public static void Initialize(IServiceProvider provider, string name, string version,
                                      string telemetryKey) {
            Initialize(provider, name);
        }

        public static void LogError(string message) {
            Log(message, true);
        }

        public static void Log(string message, bool isError = false) {
            if (string.IsNullOrEmpty(message)) {
                return;
            }

            try {
                if (EnsurePane()) {
                    string text = $"{DateTime.Now}: {message}\n";
                    panel_.OutputStringThreadSafe(text);
                    
#if DEBUG
                    if (Debugger.IsAttached) {
                        Debug.Write(text);
                    }
#endif
                }
            }
            catch (Exception ex) {
                Debug.Write(ex);
            }
        }

        public static void LogException(Exception ex, string message = "") {
            if (ex != null) {
                Log(!string.IsNullOrEmpty(message)
                        ? $"Exception: {message}\nMessage: {ex.Message}\nStack:\n{ex.StackTrace}"
                        : $"Exception: {ex.Message}\nStack:\n{ex.StackTrace}");
            }
        }

        public static void Clear() {
            panel_?.Clear();
        }

        public static void DeletePane() {
            if (panel_ != null) {
                try {
                    var output =
                        (IVsOutputWindow)provider_.GetService(typeof(SVsOutputWindow));

                    output.DeletePane(ref panelId_);
                    panel_ = null;
                }
                catch (Exception ex) {
                    Debug.Write(ex);
                }
            }
        }

        private static bool EnsurePane() {
            if (panel_ != null) {
                return true;
            }

            lock (lockObject_) {
                if (panel_ == null) {
                    panelId_ = Guid.NewGuid();

                    var output =
                        (IVsOutputWindow)provider_.GetService(typeof(SVsOutputWindow));

                    output.CreatePane(ref panelId_, name_, 1, 1);
                    output.GetPane(ref panelId_, out panel_);
                }
            }

            return panel_ != null;
        }
    }
}
