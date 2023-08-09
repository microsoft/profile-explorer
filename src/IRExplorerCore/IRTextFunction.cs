// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using IRExplorerCore.IR;

namespace IRExplorerCore {
    public class IRTextFunction : IEquatable<IRTextFunction> {
        public List<IRTextSection> Sections { get; }
        public int Number { get; set; }
        public string Name { get; }
        public IRTextSummary ParentSummary { get; set; }
        public IRTextModule ParentModule { get; set; }
        private int hashCode_;

        public IRTextFunction(string name, IRTextModule parentModule = null) {
            Name = string.Intern(name);
            ParentModule = parentModule;
            Sections = new List<IRTextSection>();

            if (parentModule != null) {
                parentModule.Functions.Add(this);
            }
        }

        public int MaxBlockCount {
            get {
                IRTextSection maxSection = null;

                foreach (var section in Sections) {
                    if (maxSection == null) {
                        maxSection = section;
                    }
                    else if (section.BlockCount > maxSection.BlockCount) {
                        maxSection = section;
                    }
                }

                return maxSection?.BlockCount ?? 0;
            }
        }

        public int SectionCount => Sections?.Count ?? 0;
        public bool HasSections => SectionCount != 0;

        public void AddSection(IRTextSection section) {
            section.Number = Sections.Count + 1;
            Sections.Add(section);
        }

        public IRTextSection FindSection(string name) {
            return Sections.Find(item => item.Name == name);
        }

        public List<IRTextSection> FindAllSections(string nameSubstring) {
            return Sections.FindAll((section) => section.Name.Contains(nameSubstring));
        }

        public bool Equals(IRTextFunction other) {
            return Equals(other, true);
        }

        public bool Equals(IRTextFunction other, bool checkParent) {
            if (ReferenceEquals(null, other)) {
                return false;
            }

            if (ReferenceEquals(this, other)) {
                return true;
            }

            return Name.Equals(other.Name, StringComparison.Ordinal) &&
                   (!checkParent || HasSameModule(other));
        }

        private bool HasSameModule(IRTextFunction other) {
            if (ReferenceEquals(ParentSummary, other.ParentSummary)) {
                return true;
            }

            if (ParentSummary != null && other.ParentSummary != null) {
                return ParentSummary.Name.Equals(other.ParentSummary.Name, StringComparison.Ordinal);
            }

            return ParentSummary == null && other.ParentSummary == null;
        }

        public bool Equals(object obj) {
            if (ReferenceEquals(null, obj)) {
                return false;
            }

            if (ReferenceEquals(this, obj)) {
                return true;
            }

            if (obj.GetType() != this.GetType()) {
                return false;
            }

            return Equals((IRTextFunction)obj, true);
        }

        public override int GetHashCode() {
            // Compute the hash so that functs. with same name in diff. modules
            // don't get the same hash code.
            if (hashCode_ == 0) {
                hashCode_ = ParentSummary != null ? HashCode.Combine(Name, ParentSummary) : HashCode.Combine(Name);
            }

            return hashCode_;
        }

        public override string ToString() {
            return Name;
        }
    }
}