using System;
using ProtoBuf;

namespace IRExplorerUI.Profile;

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
