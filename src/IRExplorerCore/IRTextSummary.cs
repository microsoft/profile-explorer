// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;

namespace IRExplorerCore {
    public class IRTextSummary {
        private Dictionary<string, IRTextFunction> functionMap_;
        private ulong nextSectionId_;
        private Dictionary<ulong, IRTextSection> sectionMap_;

        public List<IRTextFunction> Functions { get; set; }

        public IRTextSummary() {
            Functions = new List<IRTextFunction>();
            functionMap_ = new Dictionary<string, IRTextFunction>();
            sectionMap_ = new Dictionary<ulong, IRTextSection>();
            nextSectionId_ = 1;
        }

        public void AddFunction(IRTextFunction function) {
            Functions.Add(function);
            functionMap_.Add(function.Name, function);
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

        public IRTextFunction FindFunction(string name) {
            return functionMap_.TryGetValue(name, out var result) ? result : null;
        }

        public List<IRTextFunction> FindAllFunctions(string nameSubstring) {
            return Functions.FindAll((func) => func.Name.Contains(nameSubstring, StringComparison.Ordinal));
        }

        public IRTextFunction FindFunction(IRTextFunction function) {
            return functionMap_.TryGetValue(function.Name, out var result) ? result : null;
        }

        //? TODO: Compute for each section the SHA256 signature
        //? to speed up diffs and other tasks that would check the text for equality.
        public void ComputeSectionSignatures() {

        }
    }
}