// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using IRExplorerCore;

namespace IRExplorerUI {
    public class ValueStatistics {
        public class Generator {
            private List<Tuple<IRTextFunction, long>> values_ = new List<Tuple<IRTextFunction, long>>();
            private long total_;
            private long min_ = long.MaxValue;
            private long max_ = long.MinValue;

            public void Add(long value, IRTextFunction func) {
                values_.Add(new Tuple<IRTextFunction, long>(func, value));
                total_ += value;
                min_ = Math.Min(min_, value);
                max_ = Math.Max(max_, value);
            }

            public ValueStatistics Compute(int count) {
                var stats = new ValueStatistics();

                if(count == 0 || values_.Count == 0) {
                    return stats;
                }

                values_.Sort((a, b) => a.Item2.CompareTo(b.Item2));
                stats.Values = values_;
                stats.Average = (double)total_ / count;
                stats.Median = values_[values_.Count / 2].Item2;
                stats.Min = min_;
                stats.Max = max_;
                return stats;
            }
        }

        public class DistributionRange {
            public int Index { get; set; }
            public int RangeStart { get; set; }
            public int RangeEnd { get; set; }
            public int Count { get; set; }
            public double Percentage { get; set; }
            public List<Tuple<IRTextFunction, long>> Values { get; set; }

            public DistributionRange(int index, int rangeStart, int rangeEnd) {
                Index = index;
                RangeStart = rangeStart;
                RangeEnd = rangeEnd;
                Values = new List<Tuple<IRTextFunction, long>>();
            }
        }

        public double Average { get; set; }
        public long Median { get; set; }
        public long Min { get; set; }
        public long Max { get; set; }
        public List<Tuple<IRTextFunction, long>> Values;

        public override string ToString() {
            return $"Average: {Average}\n" +
                   $"Median: {Median}\n" +
                   $"Min: {Min}\n" +
                   $"Max: {Max}\n";
        }

        public int MaxDistributionFactor => (int)Math.Ceiling(Math.Log10(Max));
        
        public int GetGroupSize(int factor) {
            return Math.Max(1, (int)Math.Pow(10, Math.Round(Math.Log10(Max)) - factor));
        }

        public List<DistributionRange> ComputeDistribution(int factor) {
            var list = new List<DistributionRange>();
            int groupSize = GetGroupSize(factor);
            var range = new DistributionRange(0, 0, groupSize - 1);
            int total = 0;

            foreach(var value in Values) {
                int rangeIndex = (int)(value.Item2 / groupSize);

                if(rangeIndex != range.Index) {
                    list.Add(range);
                    range = new DistributionRange(rangeIndex, rangeIndex * groupSize, (rangeIndex + 1) * groupSize - 1);
                }

                range.Count++;
                range.Values.Add(value);
                total++;
            }

            if(range.Count > 0) {
                list.Add(range);
            }

            foreach(var item in list) {
                item.Percentage = (double)item.Count / total;
            }

            return list;
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
        public double SingleCallerPercentage => FunctionCount > 0 ? (double)SingleCallerFunctions.Count / FunctionCount : 0;
        public double LeafPercentage => FunctionCount > 0 ? (double)LeafFunctions.Count / FunctionCount : 0;

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
            List<IRTextFunction> values) {
            var dict = new Dictionary<IRTextFunction, FunctionCodeStatistics>();

            foreach(var func in values) {
                dict[func] = StatisticsMap[func];
            }

            return ComputeGroupStatistics(dict);
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
                calls.Add(stats.Calls, func);
                callers.Add(stats.Callers, func);
                size.Add(stats.Size, func);
                instrs.Add(stats.Instructions, func);
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