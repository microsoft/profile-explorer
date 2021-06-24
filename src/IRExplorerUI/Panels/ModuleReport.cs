﻿// unset

using System;
using System.Collections.Generic;
using IRExplorerCore;

namespace IRExplorerUI {
    public class FunctionCodeStatistics {
        public long Size { get; set; }
        public int Instructions { get; set; }
        public int Loads { get; set; }
        public int Stores { get; set; }
        public int Branches { get; set; }
        public int Calls { get; set; }
        public int Callers { get; set; }
        public int IndirectCalls { get; set; }
        public int Callees { get; set; }

        public bool ComputeDiff(FunctionCodeStatistics other) {
            Size = other.Size - Size;
            Instructions = other.Instructions - Instructions;
            Loads = other.Loads - Loads;
            Stores = other.Stores - Stores;
            Branches = other.Branches - Branches;
            Calls = other.Calls - Calls;
            Callers = other.Callers - Callers;
            IndirectCalls = other.IndirectCalls - IndirectCalls;
            Callees = other.Callees - Callees;
            return Size != 0 || Instructions != 0 ||
                   Loads != 0 || Stores != 0 ||
                   Branches != 0 || Calls != 0 ||
                   Callers != 0 || IndirectCalls != 0 || Callees != 0;
        }
        
        public void Add(FunctionCodeStatistics other) {
            Size = other.Size - Size;
            Instructions = other.Instructions + Instructions;
            Loads = other.Loads + Loads;
            Stores = other.Stores + Stores;
            Branches = other.Branches + Branches;
            Calls = other.Calls + Calls;
            Callers = other.Callers + Callers;
            IndirectCalls = other.IndirectCalls + IndirectCalls;
            Callees = other.Callees + Callees;
        }

        public override string ToString() {
            return $"Size: {Size}\n" +
                   $"Instructions: {Instructions}\n" +
                   $"Loads: {Loads}\n" +
                   $"Stores: {Stores}\n" +
                   $"Branches: {Branches}\n" +
                   $"Calls: {Calls}\n" +
                   $"Callers: {Callers}\n" +
                   $"IndirectCalls: {IndirectCalls}\n" +
                   $"Callees: {Callees}";
        }
    }

    public class ValueStatistics {
        public class Generator {
            private List<double> values_ = new List<double>();
            private double total_;
            private double min_ = double.MaxValue;
            private double max_ = double.MinValue;

            public void Add(double value) {
                values_.Add(value);
                total_ += value;
                min_ = Math.Min(min_, value);
                max_ = Math.Max(max_, value);
            }

            public ValueStatistics Compute(int count) {
                var stats = new ValueStatistics();

                if(count == 0 || values_.Count == 0) {
                    return stats;
                }

                values_.Sort();
                stats.Average = total_ / count;
                stats.Median = values_[values_.Count / 2];
                stats.Min = min_;
                stats.Max = max_;
                return stats;
            }
        }

        public double Average { get; set; }
        public double Median { get; set; }
        public double Min { get; set; }
        public double Max { get; set; }

        public override string ToString() {
            return $"Average: {Average}\n" +
                   $"Median: {Median}\n" +
                   $"Min: {Min}\n" +
                   $"Max: {Max}\n";
        }
    }

    public class FunctionGroupStatistics {
        public IDictionary<IRTextFunction, FunctionCodeStatistics> Functions { get; set; }
        public FunctionCodeStatistics Total { get; set; }
        public ValueStatistics Size { get; set; }
        public ValueStatistics Instructions { get; set; }
        public ValueStatistics Calls { get; set; }
        public ValueStatistics Callers { get; set; }

        public FunctionGroupStatistics(IDictionary<IRTextFunction, FunctionCodeStatistics> functions) {
            Functions = functions;
            Total = new FunctionCodeStatistics();
        }
        
        public override string ToString() {
            return $"Totals: {Total}\n" +
                   $"Size: {Size}\n" +
                   $"Instructions: {Instructions}\n" +
                   $"Calls: {Calls}\n" +
                   $"Callers: {Callers}\n";
        }
    }

    public class ModuleReport {
        public ICollection<IRTextFunction> Functions => StatisticsMap.Keys;
        public int FunctionCount => StatisticsMap.Count;
        public IDictionary<IRTextFunction, FunctionCodeStatistics> StatisticsMap { get; set; }
        public FunctionGroupStatistics Statistics { get; set; }
        
        public List<IRTextFunction> SingleCallerFunctions { get; set; }
        public List<IRTextFunction> LeafFunctions { get; set; }
        public Dictionary<int, List<IRTextFunction>> InstructionsDistribution { get; set; }
        public Dictionary<int, List<IRTextFunction>> CallsDistribution { get; set; }
        public Dictionary<int, List<IRTextFunction>> CallersDistribution { get; set; }


        public ModuleReport(IDictionary<IRTextFunction, FunctionCodeStatistics> functionStatisticsMap) {
            StatisticsMap = functionStatisticsMap;
            Statistics = new FunctionGroupStatistics(functionStatisticsMap);
            SingleCallerFunctions = new List<IRTextFunction>();
            LeafFunctions = new List<IRTextFunction>();
            InstructionsDistribution = new Dictionary<int, List<IRTextFunction>>();
            CallsDistribution = new Dictionary<int, List<IRTextFunction>>();
            CallersDistribution = new Dictionary<int, List<IRTextFunction>>();
        }

        public void Generate() {
            foreach (var pair in StatisticsMap) {
                var func = pair.Key;
                var stats = pair.Value;

                AddToDistribution(stats.Instructions, func, InstructionsDistribution);
                AddToDistribution(stats.Calls, func, CallsDistribution);
                AddToDistribution(stats.Callers, func, CallersDistribution);

                if (stats.Callers == 1) {
                    SingleCallerFunctions.Add(func);
                }

                if (stats.Calls == 0) {
                    LeafFunctions.Add(func);
                }
            }
            
            ComputeStatistics();
        }

        public List<Tuple<int, int, List<IRTextFunction>>> ComputeHistogram(int step) {
            return null;
        }

        public void ComputeStatistics() {
            Statistics = ComputeGroupStatistics(StatisticsMap);
        } 
        
        public FunctionGroupStatistics ComputeGroupStatistics(
            IDictionary<IRTextFunction, FunctionCodeStatistics> values) {
            var groupStats = new FunctionGroupStatistics(values);
            var calls = new ValueStatistics.Generator();
            var callers = new ValueStatistics.Generator();
            var size = new ValueStatistics.Generator();
            var instrs = new ValueStatistics.Generator();
            int functions = 0;

            foreach (var pair in values) {
                var func = pair.Key;
                var stats = pair.Value;
                groupStats.Total.Add(stats);
                calls.Add(stats.Calls);
                callers.Add(stats.Callers);
                size.Add(stats.Size);
                instrs.Add(stats.Instructions);
                functions++;
            }

            groupStats.Calls = calls.Compute(functions);
            groupStats.Callers = callers.Compute(functions);
            groupStats.Size = size.Compute(functions);
            groupStats.Instructions = instrs.Compute(functions);
            return groupStats;
        }

        private void AddToDistribution(int times, IRTextFunction function, Dictionary<int, List<IRTextFunction>> map) {
            if (!map.TryGetValue(times, out var list)) {
                list = new List<IRTextFunction>();
                map[times] = list;
            }
            
            list.Add(function);
        }

        public override string ToString() {
            return $"Functions: {Functions}\n" + 
                   $"Statistics: {Statistics}\n" + 
                   $"SingleCallerFunctions: {SingleCallerFunctions.Count}\n" +
                   $"LeafFunctions: {LeafFunctions.Count}";
        }
    }
}