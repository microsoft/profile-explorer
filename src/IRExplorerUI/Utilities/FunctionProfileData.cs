using System;
using System.Collections.Generic;

namespace IRExplorerUI.Utilities {
    public class FunctionProfileData {
        public string SourceFilePath { get; set; }
        public TimeSpan Weight { get; set; }
        public Dictionary<int, TimeSpan> SourceLineWeight { get; set; }
        public Dictionary<long, TimeSpan> InstructionWeight { get; set; }

        public FunctionProfileData(string filePath) {
            SourceFilePath = filePath;
            Weight = TimeSpan.Zero;
            SourceLineWeight = new Dictionary<int, TimeSpan>();
            InstructionWeight = new Dictionary<long, TimeSpan>();
        }

        public void AddLineSample(int sourceLine, TimeSpan weight) {
            if (SourceLineWeight.TryGetValue(sourceLine, out var currentWeight)) {
                SourceLineWeight[sourceLine] = currentWeight + weight;
            }
            else {
                SourceLineWeight[sourceLine] = weight;
            }
        }

        public void AddInstructionSample(long instrOffset, TimeSpan weight) {
            if (InstructionWeight.TryGetValue(instrOffset, out var currentWeight)) {
                InstructionWeight[instrOffset] = currentWeight + weight;
            }
            else {
                InstructionWeight[instrOffset] = weight;
            }
        }

        public double ScaleWeight(TimeSpan weight) {
            return (double)weight.Ticks / (double)Weight.Ticks;
        }
    }
}