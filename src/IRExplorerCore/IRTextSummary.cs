// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Text;

namespace IRExplorerCore {
    public class IRTextSummary {
        private Dictionary<string, IRTextFunction> functionNameMap_;
        private Dictionary<int, IRTextFunction> functionMap_;
        private Dictionary<int, IRTextSection> sectionMap_;
        private int hashCode_;

        public Guid Id { get; set; }
        public string ModuleName { get; set; }
        public List<IRTextFunction> Functions { get; set; }

        public IRTextSummary(string moduleName = null) {
            ModuleName = moduleName;
            Functions = new List<IRTextFunction>();
            functionNameMap_ = new Dictionary<string, IRTextFunction>();
            functionMap_ = new Dictionary<int, IRTextFunction>();
            sectionMap_ = new Dictionary<int, IRTextSection>();
        }

        public void AddFunction(IRTextFunction function) {
            if (functionNameMap_.ContainsKey(function.Name)) {
                return; //? remove
            }
            
            function.Number = Functions.Count;
            Functions.Add(function);
            functionNameMap_.Add(function.Name, function);
            functionMap_.Add(function.Number, function);
            function.ParentSummary = this;
        }

        public void AddSection(IRTextSection section) {
            section.Id = sectionMap_.Count;
            sectionMap_.Add(sectionMap_.Count, section);
        }

        public IRTextSection GetSectionWithId(int id) {
            return sectionMap_.TryGetValue(id, out var value) ? value : null;
        }

        public IRTextFunction GetFunctionWithId(int id) {
            return functionMap_.TryGetValue(id, out var result) ? result : null;
        }

        public IRTextFunction FindFunction(string name) {
            return functionNameMap_.TryGetValue(name, out var result) ? result : null;
        }

        public delegate bool FunctionNameMatchDelegate(string name);

        public IRTextFunction FindFunction(FunctionNameMatchDelegate matchCheck) {
            foreach (var function in Functions) {
                if (matchCheck(function.Name)) {
                    return function;
                }
            }

            return null;
        }

        public List<IRTextFunction> FindAllFunctions(string nameSubstring) {
            return Functions.FindAll((func) => func.Name.Contains(nameSubstring, StringComparison.Ordinal));
        }

        public List<IRTextFunction> FindAllFunctions(string[] nameSubstrings) {
            return Functions.FindAll((func) => {
                foreach (var name in nameSubstrings) {
                    if (!func.Name.Contains(name, StringComparison.Ordinal)) {
                        return false;
                    }
                }

                return true;
            });
        }

        public IRTextFunction FindFunction(IRTextFunction function) {
            return functionNameMap_.TryGetValue(function.Name, out var result) ? result : null;
        }

        public override int GetHashCode() {
            if (hashCode_ == 0) {
                hashCode_ = HashCode.Combine(ModuleName);
            }

            return hashCode_;
        }

        public override string ToString() {
            var sb = new StringBuilder();
            sb.AppendLine($"Summary: {ModuleName}");
            sb.AppendLine($"Functions: {Functions.Count}");

            for (var i = 0; i < Functions.Count; i++) {
                sb.AppendLine($"    {i}: {Functions[i].Name}, sections: {Functions[i].SectionCount}");
            }

            return sb.ToString();
        }
    }
}