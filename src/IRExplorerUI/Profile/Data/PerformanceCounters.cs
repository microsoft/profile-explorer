// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using ProtoBuf;

namespace IRExplorerUI.Profile;

[ProtoContract(SkipConstructor = true)]
[ProtoInclude(100, typeof(PerformanceMetric))]
public class PerformanceCounter {
    [ProtoMember(2)] public int Index { get; set; }
    [ProtoMember(3)] public int Id { get; set; }
    [ProtoMember(4)] public string Name { get; set; }
    [ProtoMember(5)] public int Frequency { get; set; }

    public virtual bool IsMetric => false;

    public PerformanceCounter() {
    }

    public PerformanceCounter(int id, string name, int frequency = 0) {
        Id = id;
        Name = name;
        Frequency = frequency;
    }
}

[ProtoContract(SkipConstructor = true)]
public class PerformanceMetric : PerformanceCounter {
    [ProtoMember(1)] public PerformanceMetricConfig Config { get; set; }
    [ProtoMember(2)] public PerformanceCounter BaseCounter { get; set; }
    [ProtoMember(3)] public PerformanceCounter RelativeCounter { get; set; }

    public override bool IsMetric => true;

    public PerformanceMetric(int id, PerformanceMetricConfig config,
        PerformanceCounter baseCounter,
        PerformanceCounter relativeCounter) : base(id, config.Name) {
        Config = config;
        BaseCounter = baseCounter;
        RelativeCounter = relativeCounter;
    }

    public double ComputeMetric(PerformanceCounterValueSet counterValueSet, out long baseValue, out long relativeValue) {
        baseValue = counterValueSet.FindCounterValue(BaseCounter);
        relativeValue = counterValueSet.FindCounterValue(RelativeCounter);

        if (baseValue == 0) {
            return 0;
        }

        // Counters may not be accurate and the percentage can end up more than 100%.
        double result = (double)relativeValue / (double)baseValue;
        return Config.IsPercentage ? Math.Min(result, 1) : result;
    }
}


[ProtoContract(SkipConstructor = true)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct PerformanceCounterValue : IEquatable<PerformanceCounterValue> {
    [ProtoMember(1)] public int CounterId { get; set; }
    [ProtoMember(2)] public long Value { get; set; }

    public PerformanceCounterValue(int counterId, long value = 0) {
        CounterId = counterId;
        Value = value;
    }

    public bool Equals(PerformanceCounterValue other) {
        return CounterId == other.CounterId && Value == other.Value;
    }

    public override bool Equals(object obj) {
        return obj is PerformanceCounterValue other && Equals(other);
    }

    public override int GetHashCode() {
        return HashCode.Combine(CounterId, Value);
    }
}


// Groups a set of counters associated with a single instruction.
// There is one PerformanceCounterValue for each counter type
// that accumulates all instances of the raw events.
[ProtoContract(SkipConstructor = true)]
public class PerformanceCounterValueSet {
    [ProtoMember(1)] public List<PerformanceCounterValue> Counters { get; set; }

    public int Count => Counters.Count;

    public PerformanceCounterValueSet() {
        InitializeReferenceMembers();
    }

    [ProtoAfterDeserialization]
    private void InitializeReferenceMembers() {
        Counters ??= new List<PerformanceCounterValue>();
    }

    public void AddCounterSample(int perfCounterId, long value) {
        var index = Counters.FindIndex((item) => item.CounterId == perfCounterId);
        var countersSpan = CollectionsMarshal.AsSpan(Counters);

        if (index != -1) {
            ref var counterRef = ref countersSpan[index];
            counterRef.Value += value;
        }
        else {
            // Keep the list sorted so that it is in sync
            // with the sorted counter definition list.
            var counter = new PerformanceCounterValue(perfCounterId, value);
            int insertionIndex = 0;

            for (int i = 0; i < Counters.Count; i++, insertionIndex++) {
                if (Counters[i].CounterId >= perfCounterId) {
                    break;
                }
            }

            Counters.Insert(insertionIndex, counter);
        }
    }

    public long FindCounterValue(int perfCounterId) {
        var index = Counters.FindIndex((item) => item.CounterId == perfCounterId);
        return index != -1 ? Counters[index].Value : 0;
    }

    public long FindCounterValue(PerformanceCounter counter) {
        return FindCounterValue(counter.Id);
    }

    public void Add(PerformanceCounterValueSet other) {
        //? TODO: This assumes there are not many counters being collected,
        //? switch to dict if dozens get to be collected one day.
        foreach (var counter in other.Counters) {
            var index = Counters.FindIndex((item) => item.CounterId == counter.CounterId);

            if (index != -1) {
                var countersSpan = CollectionsMarshal.AsSpan(Counters);
                ref var counterRef = ref countersSpan[index];
                counterRef.Value += counter.Value;
            }
            else {
                Counters.Add(new PerformanceCounterValue(counter.CounterId, counter.Value));
            }
        }
    }

    public long this[int perfCounterId] => FindCounterValue(perfCounterId);
}


public static class PerformanceCounterExtensions {
    public static PerformanceCounterValueSet AccumulateValue<K>(this Dictionary<K, PerformanceCounterValueSet> dict, K key, PerformanceCounterValueSet value) {
        if (!dict.TryGetValue(key, out var currentValue)) {
            currentValue = new PerformanceCounterValueSet();
            dict[key] = currentValue;
        }

        currentValue.Add(value);
        return currentValue;
    }
}


[ProtoContract(SkipConstructor = true)]
public class PerformanceCounterConfig : IEquatable<PerformanceCounterConfig> {
    [ProtoMember(1)]
    public bool IsEnabled { get; set; }
    [ProtoMember(2)]
    public bool IsBuiltin { get; set; }
    [ProtoMember(3)]
    public int Id { get; set; }
    [ProtoMember(4)]
    public string Name { get; set; }
    [ProtoMember(5)]
    public string Description { get; set; }
    [ProtoMember(6)]
    public int Interval { get; set; }
    [ProtoMember(7)]
    public int MinInterval { get; set; }
    [ProtoMember(8)]
    public int MaxInterval { get; set; }
    [ProtoMember(9)]
    public int DefaultInterval { get; set; }

    public PerformanceCounterConfig(int id, string name, int defaultInterval,
                                    int minInterval, int maxInterval, bool isBuiltin) {
        Id = id;
        Name = name;
        Interval = defaultInterval;
        DefaultInterval = defaultInterval;
        MinInterval = minInterval;
        MaxInterval = maxInterval;
        IsBuiltin = isBuiltin;
    }

    public bool Equals(PerformanceCounterConfig other) {
        if (ReferenceEquals(null, other)) {
            return false;
        }

        if (ReferenceEquals(this, other)) {
            return true;
        }

        return Id == other.Id && Name == other.Name;
    }

    public override bool Equals(object obj) {
        if (ReferenceEquals(null, obj)) {
            return false;
        }

        if (ReferenceEquals(this, obj)) {
            return true;
        }

        if (obj.GetType() != this.GetType()) {
            return false;
        }

        return Equals((PerformanceCounterConfig)obj);
    }

    public override int GetHashCode() {
        return HashCode.Combine(Id, Name);
    }

    public static bool operator ==(PerformanceCounterConfig left, PerformanceCounterConfig right) {
        return Equals(left, right);
    }

    public static bool operator !=(PerformanceCounterConfig left, PerformanceCounterConfig right) {
        return !Equals(left, right);
    }
}



[ProtoContract(SkipConstructor = true)]
public class PerformanceMetricConfig : IEquatable<PerformanceMetricConfig> {
    [ProtoMember(1)]
    public string Name { get; set; }
    [ProtoMember(2)]
    public string BaseCounterName { get; set; }
    [ProtoMember(3)]
    public string RelativeCounterName { get; set; }
    [ProtoMember(4)]
    public string Description { get; set; }
    [ProtoMember(5)]
    public bool IsPercentage { get; set; }
    [ProtoMember(6)]
    public bool IsEnabled { get; set; }

    public PerformanceMetricConfig(string name, string baseCounterName, string relativeCounterName,
                                   bool isPercentage, string description) {
        Name = name;
        BaseCounterName = baseCounterName;
        RelativeCounterName = relativeCounterName;
        Description = description;
        IsPercentage = isPercentage;
        IsEnabled = true;
    }

    public bool Equals(PerformanceMetricConfig other) {
        if (ReferenceEquals(null, other)) {
            return false;
        }

        if (ReferenceEquals(this, other)) {
            return true;
        }

        return Name == other.Name &&
               BaseCounterName == other.BaseCounterName &&
               RelativeCounterName == other.RelativeCounterName;
    }

    public override bool Equals(object obj) {
        if (ReferenceEquals(null, obj)) {
            return false;
        }

        if (ReferenceEquals(this, obj)) {
            return true;
        }

        if (obj.GetType() != this.GetType()) {
            return false;
        }

        return Equals((PerformanceMetricConfig)obj);
    }

    public override int GetHashCode() {
        return HashCode.Combine(Name, BaseCounterName, RelativeCounterName, IsPercentage);
    }

    public static bool operator ==(PerformanceMetricConfig left, PerformanceMetricConfig right) {
        return Equals(left, right);
    }

    public static bool operator !=(PerformanceMetricConfig left, PerformanceMetricConfig right) {
        return !Equals(left, right);
    }
}