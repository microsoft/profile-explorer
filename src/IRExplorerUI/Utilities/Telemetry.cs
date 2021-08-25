// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;

namespace IRExplorerUI {
    public class DefaultTelemetryInitializer : ITelemetryInitializer {
        public void Initialize(ITelemetry telemetry) {
            // By default these values are initialized with the machine name,
            // which is not that anonymous...
            telemetry.Context.Cloud.RoleName = "_";
            telemetry.Context.Cloud.RoleInstance = "_";
        }
    }

    public static class Telemetry {
        private static TelemetryClient telemetry_;

        public static bool IsEnabled { get; set; }
        public static bool AutoFlush { get; set; }

        static Telemetry() {
#if DEBUG
            IsEnabled = false;
#else
            IsEnabled = true;
#endif
            AutoFlush = true;
        }

        public static bool InitializeTelemetry() {
            if (!IsEnabled) {
                return false;
            }

            if (telemetry_ != null) {
                return true;
            }

            try {
                var configuration = TelemetryConfiguration.CreateDefault();
                configuration.DisableTelemetry = false;
                configuration.ConnectionString = @"InstrumentationKey=f44afd12-7079-42e2-83fb-62c5841f384a;IngestionEndpoint=https://westus2-2.in.applicationinsights.azure.com/";
                configuration.InstrumentationKey = @"f44afd12-7079-42e2-83fb-62c5841f384a";
                configuration.TelemetryInitializers.Add(new DefaultTelemetryInitializer());

                telemetry_ = new TelemetryClient(configuration);
                telemetry_.Context.Session.Id = Guid.NewGuid().ToString();

                // Make an anonymous user ID.
                var userId = Encoding.UTF8.GetBytes(Environment.UserName + Environment.UserDomainName);
                using var crypto = new SHA1CryptoServiceProvider();
                var hash = crypto.ComputeHash(userId);
                telemetry_.Context.User.Id = Convert.ToBase64String(hash);
            }
            catch (Exception ex) {
                telemetry_ = null;
                Debug.Write(ex);
            }

            return telemetry_ != null;
        }

        public static Task TrackEvent(string name) {
            if (InitializeTelemetry()) {
                return Task.Run(() => {
                    telemetry_.TrackEvent(name);

                    if (AutoFlush) {
                        telemetry_.Flush();
                    }
                });
            }

            return null;
        }

        public static Task TrackMetric(string name, double value) {
            if (InitializeTelemetry()) {
                Task.Run(() => {
                    telemetry_.TrackMetric(name, value);

                    if (AutoFlush) {
                        telemetry_.Flush();
                    }
                });
            }

            return null;
        }

        public static Task TrackException(Exception ex) {
            if (InitializeTelemetry()) {
                return Task.Run(() => {
                    telemetry_.TrackException(ex);

                    if (AutoFlush) {
                        telemetry_.Flush();
                    }
                });
            }

            return null;
        }
    }
}
