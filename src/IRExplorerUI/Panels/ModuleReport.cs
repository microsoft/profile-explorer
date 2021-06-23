// unset

using System;
using System.Collections.Generic;
using IRExplorerCore;

namespace IRExplorerUI {
    public class FunctionStatistics {
        public long Size { get; set; }
        public int Instructions { get; set; }
        public int Loads { get; set; }
        public int Stores { get; set; }
        public int Branches { get; set; }
        public int Calls { get; set; }
        public int Callers { get; set; }
        public int IndirectCalls { get; set; }
        public int Callees { get; set; }

        public bool ComputeDiff(FunctionStatistics other) {
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
        
        public void Add(FunctionStatistics other) {
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

    public class FunctionGroupStatistics {
        public IDictionary<IRTextFunction, FunctionStatistics> Functions { get; set; }
        public FunctionStatistics Total { get; set; }
        public double AverageCalls { get; set; }
        public double MedianCalls { get; set; }
        public int MaxCalls { get; set; }
        public double AverageCallers { get; set; }
        public double MedianCallers { get; set; }
        public int MaxCallers { get; set; }

        public FunctionGroupStatistics(IDictionary<IRTextFunction, FunctionStatistics> functions) {
            Functions = functions;
            Total = new FunctionStatistics();
        }
        
        public override string ToString() {
            return $"Totals: {Total}\n" +
                   $"AverageCalls: {AverageCalls}\n" +
                   $"MedianCalls: {MedianCalls}\n" +
                   $"MaxCalls: {MaxCalls}\n" +
                   $"AverageCallers: {AverageCallers}\n" +
                   $"MedianCallers: {MedianCallers}\n" +
                   $"MaxCallers: {MaxCallers}\n";
        }
    }

    public class ModuleReport {
        public ICollection<IRTextFunction> Functions => StatisticsMap.Keys;
        public int FunctionCount => StatisticsMap.Count;
        public IDictionary<IRTextFunction, FunctionStatistics> StatisticsMap;
        public FunctionGroupStatistics Statistics;
        
        public List<IRTextFunction> SingleCallerFunctions { get; set; }
        public List<IRTextFunction> LeafFunctions { get; set; }
        public Dictionary<int, List<IRTextFunction>> InstructionsDistribution { get; set; }
        public Dictionary<int, List<IRTextFunction>> CallsDistribution { get; set; }
        public Dictionary<int, List<IRTextFunction>> CallersDistribution { get; set; }


        public ModuleReport(IDictionary<IRTextFunction, FunctionStatistics> functionStatisticsMap) {
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
            IDictionary<IRTextFunction, FunctionStatistics> values) {
            var groupStats = new FunctionGroupStatistics(values);
            var callCounts = new List<int>();
            var callerCounts = new List<int>();
            int functions = 0;

            foreach (var pair in values) {
                var func = pair.Key;
                var stats = pair.Value;
                groupStats.Total.Add(stats);
                groupStats.MaxCalls = Math.Max(stats.Calls, groupStats.MaxCalls);
                groupStats.MaxCallers = Math.Max(stats.Callers, groupStats.MaxCallers);
                functions++;
            }
            
            callCounts.Sort();
            callerCounts.Sort();

            if (callCounts.Count > 0 && groupStats.Total.Calls > 0) {
                groupStats.AverageCalls = (double)groupStats.Total.Calls / functions;
                groupStats.MedianCalls = callCounts[callCounts.Count / 2];
            }
            
            if (callerCounts.Count > 0 && groupStats.Total.Callers > 0) {
                groupStats.AverageCallers = (double)groupStats.Total.Callers / functions;
                groupStats.MedianCallers = callerCounts[callerCounts.Count / 2];
            }

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