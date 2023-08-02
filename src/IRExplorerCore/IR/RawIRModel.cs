using System.Collections.Generic;
using IRExplorerCore.IR;

namespace IRExplorerCore.RawIRModel {
    public class ModuleIR {
        public string Name { get; set; }
        public List<FunctionIR> Functions { get; set; }
    }

    public class FunctionIR {
        public string Name { get; set; }
        public string Opcode { get; set; }
        public List<RegionIR> Regions { get; set; }
    }

    public class RegionIR {
        public List<BlockIR> Blocks { get; set; }
    }

    public class BlockIR {
        public long Id { get; set; }
        public string Label { get; set; }
        public List<BlockArgument> Arguments { get; set; }
        public List<long> Predecessors { get; set; }
        public List<long> Successors { get; set; }
        public List<OperationIR> Operations { get; set; }
    }

    public class BlockArgument {
      public ResultIR Argument { get; set; }
      public List<IncomingValue> IncomingValues { get; set; }
    }

    public class IncomingValue {
        public long BlockId { get; set; }
        public long OperandId { get; set; }
    }

    public class OperationIR {
        public long Id { get; set; }
        public string Opcode { get; set; }
        public InstructionKind Kind { get; set; }
        public int LineNumber { get; set; }
        public int StartOffset { get; set; }
        public int EndOffset { get; set; }
        public List<SourceIR> Sources { get; set; }
        public List<ResultIR> Results { get; set;}
        public List<RegionIR> Regions { get; set; }
    }

    public class SourceIR {
        public long Id { get; set; }
        public long DefinitionId { get; set; }
        public OperandKind Kind { get; set; }
        public int LineNumber { get; set; }
        public int StartOffset { get; set; }
        public int EndOffset { get; set; }
    }

    public class ResultIR {
        public long Id { get; set; }
        public OperandKind Kind { get; set; }
        public int LineNumber { get; set; }
        public int StartOffset { get; set; }
        public int EndOffset { get; set; }
        public List<SSAUse> Uses { get; set; }
    }

    public class SSAUse {
        public long UseId { get; set; }
        public long UserId { get; set; }
    }
}