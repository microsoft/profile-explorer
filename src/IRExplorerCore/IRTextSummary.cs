// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;

namespace IRExplorerCore {
    public class IRTextSummary {
        private Dictionary<string, IRTextFunction> functionNameMap_;
        private Dictionary<int, IRTextFunction> functionMap_;
        private Dictionary<ulong, IRTextSection> sectionMap_;
        private ulong nextSectionId_;

        public string ModuleName { get; set; }
        public List<IRTextFunction> Functions { get; set; }

        public IRTextSummary() {
            Functions = new List<IRTextFunction>();
            functionNameMap_ = new Dictionary<string, IRTextFunction>();
            functionMap_ = new Dictionary<int, IRTextFunction>();
            sectionMap_ = new Dictionary<ulong, IRTextSection>();
            nextSectionId_ = 1;
        }

        public void AddFunction(IRTextFunction function) {
            function.Number = Functions.Count;
            Functions.Add(function);
            functionNameMap_.Add(function.Name, function);
            functionMap_.Add(function.Number, function);
            function.ParentSummary = this;
        }

        public void AddSection(IRTextSection section) {
            sectionMap_.Add(nextSectionId_, section);
            section.Id = nextSectionId_;
            nextSectionId_++;
        }

        public IRTextSection GetSectionWithId(ulong id) {
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
            foreach(var function in Functions) {
                if(matchCheck(function.Name)) {
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

        //? TODO: Compute for each section the SHA256 signature
        //? to speed up diffs and other tasks that would check the text for equality.
        public void ComputeSectionSignatures() {

        }
    }
}