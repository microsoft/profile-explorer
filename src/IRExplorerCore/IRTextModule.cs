using System.Collections.Generic;
using System.Text;

namespace IRExplorerCore {
    public class IRTextModule {
        public string Name { get; set; }
        public IRTextModule ParentModule { get; set; }
        public List<IRTextModule> Submodules { get; set; }
        public List<IRTextFunction> Functions { get; set; }
        public bool HasName => !string.IsNullOrEmpty(Name);

        public IRTextModule(string name = "", IRTextModule parent = null) {
            Name = name;
            ParentModule = parent;
            Submodules = new List<IRTextModule>();
            Functions = new List<IRTextFunction>();

            if (parent != null) {
                parent.Submodules.Add(this);
            }
        }

        public override string ToString() {
            return $"Module: {Name}, Submodules: {Submodules.Count}, Functions: {Functions.Count}";
        }
    }
}