// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Core {
    public static class ObjectTracker {
        public class DebugObjectId {
            static Dictionary<string, int> PrefixNumbers = new Dictionary<string, int>();
            static object LockObject = new object();

            public string Id { get; set; }

            public DebugObjectId() {
                Id = "<null>";
            }

            public DebugObjectId(object value) {
                var typeName = value.GetType().Name.ToLower();
                var prefix = typeName.Substring(0, Math.Min(12, typeName.Length));
                int number = 1;

                lock (LockObject) {
                    if (PrefixNumbers.TryGetValue(prefix, out number)) {
                        ++number;
                        PrefixNumbers[prefix] = number;
                    }
                    else {
                        PrefixNumbers[prefix] = number;
                    }
                }

                Id = $"{prefix}-{number}";
            }

            public override string ToString() {
                return Id;
            }
        }

        static ConditionalWeakTable<object, DebugObjectId> DebugTaskId =
            new ConditionalWeakTable<object, DebugObjectId>();

        public static DebugObjectId Track(object value) {
            if (value == null) {
                return new DebugObjectId();
            }

            return DebugTaskId.GetValue(value, (obj) => {
                return new DebugObjectId(obj);
            });
        }
    }
}
