using System;
using System.Diagnostics;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using System.Collections.Generic;
using System.Text;
using System.Security.Cryptography;

namespace IRExplorerExtension
{
    public static class Logger
    {
        private static IVsOutputWindowPane panel_;
        private static IServiceProvider provider_;
        private static Guid panelId_;
        private static string name_;
        private static object lockObject_ = new object();
        static TelemetryClient telemetry_;

        public static void Initialize(IServiceProvider provider, string name)
        {
            provider_ = provider;
            name_ = name;
        }

        public static void Initialize(IServiceProvider provider, string name, string version, string telemetryKey)
        {
            Initialize(provider, name);
            //Telemetry.Initialize(provider, version, telemetryKey);
        }

        public static void LogError(string message)
        {
            Log(message, true);
        }

        public static void Log(string message, bool isError = false)
        {
            if (string.IsNullOrEmpty(message))
                return;

            try
            {
                if (EnsurePane())
                {
                    var text = $"{DateTime.Now}: {message}\n";
                    panel_.OutputStringThreadSafe(text);

                    if(isError && InitializeTelemetry())
                    {
                        telemetry_.TrackEvent("Extension error", new Dictionary<string, string>()
                        {{"Message", message},
                         {"Debugger attached", DebuggerInstance.InBreakMode.ToString()},
                         {"Client connected", ClientInstance.IsConnected.ToString() }
                        });

                        telemetry_.Flush();
                    }

#if DEBUG
                    if (Debugger.IsAttached)
                    {
                        Debug.Write(text);                
                    }
#endif
                }
            }
            catch (Exception ex)
            {
                Debug.Write(ex);
            }
        }

        static bool InitializeTelemetry()
        {
            if(telemetry_ != null)
            {
                return true;
            }

            try
            {
                TelemetryConfiguration configuration = TelemetryConfiguration.CreateDefault();
                configuration.DisableTelemetry = false;
                configuration.InstrumentationKey = "da7d4359-13f9-40c0-a2bb-c3fb54a76275";
                telemetry_ = new TelemetryClient(configuration);

                telemetry_.Context.Session.Id = Guid.NewGuid().ToString();
                byte[] userId = Encoding.UTF8.GetBytes(Environment.UserName + Environment.MachineName);

                using (var crypto = new MD5CryptoServiceProvider())
                {
                    byte[] hash = crypto.ComputeHash(userId);
                    telemetry_.Context.User.Id = Convert.ToBase64String(hash);
                }

            }
            catch (Exception ex)
            {
                telemetry_ = null;
                Debug.Write(ex);
            }

            return telemetry_ != null;
        }

        public static void LogException(Exception ex, string message = "")
        {
            if (ex != null)
            {
                if (!string.IsNullOrEmpty(message))
                {
                    Log($"Exception: {message}\nMessage: {ex.Message}\nStack:\n{ex.StackTrace}");
                }
                else
                {
                    Log($"Exception: {ex.Message}\nStack:\n{ex.StackTrace}");
                }

                if (InitializeTelemetry())
                {
                    telemetry_.TrackException(ex, new Dictionary<string, string>()
                        {{"Message", message},
                         {"Debugger attached", DebuggerInstance.InBreakMode.ToString()},
                         {"Client connected", ClientInstance.IsConnected.ToString() }
                        });

                    telemetry_.Flush();
                }
            }
        }

        public static void Clear()
        {
            if (panel_ != null)
            {
                panel_.Clear();
            }
        }

        public static void DeletePane()
        {
            if (panel_ != null)
            {
                try
                {
                    IVsOutputWindow output = (IVsOutputWindow)provider_.GetService(typeof(SVsOutputWindow));
                    output.DeletePane(ref panelId_);
                    panel_ = null;
                }
                catch (Exception ex)
                {
                    Debug.Write(ex);
                }
            }
        }

        private static bool EnsurePane()
        {
            if (panel_ != null)
            {
                return true;
            }

            lock (lockObject_)
            {
                if (panel_ == null)
                {
                    panelId_ = Guid.NewGuid();
                    IVsOutputWindow output = (IVsOutputWindow)provider_.GetService(typeof(SVsOutputWindow));
                    output.CreatePane(ref panelId_, name_, 1, 1);
                    output.GetPane(ref panelId_, out panel_);
                }
            }

            return panel_ != null;
        }
    }
}
