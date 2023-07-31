using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Dynamic;
using System.IO;
using IRExplorerCore.IR;
using IRExplorerUI;

namespace IRExplorerCore.LLVM {

    public sealed class MLIRSectionParser : IRSectionParser {
        private IRParsingErrorHandler errorHandler_;
        private MLIRParser parser_;
        private ICompilerIRInfo irInfo_;

        public MLIRSectionParser(ICompilerIRInfo irInfo, IRParsingErrorHandler errorHandler = null) {
            irInfo_ = irInfo;

            if (errorHandler != null) {
                errorHandler_ = errorHandler;
                errorHandler_.Parser = this;
            }
        }

        public FunctionIR ParseSection(IRTextSection section, string sectionText) {
            return ParseSection(section, sectionText.AsMemory());
        }

        public FunctionIR ParseSection(IRTextSection section, ReadOnlyMemory<char> sectionText) {
            var inputFile = Path.GetTempFileName();
            var outputFile = Path.ChangeExtension(Path.GetTempFileName(), ".json");

            var parserPath = @"D:\llvm-project\build\Debug\bin\mlir-lsp-server.exe";
            var psi = new ProcessStartInfo(parserPath) {
                Arguments = $"\"{inputFile}\" \"{outputFile}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            try {
                File.WriteAllText(inputFile, sectionText.ToString());

                using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
                process.Start();
                process.WaitForExit();

                if (!File.Exists(outputFile)) {
                    return null;
                }
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed to parse MLIR for section: {section.Name}");
                return null;
            }

            var jsonData = File.ReadAllText(outputFile);

            if (JsonUtils.Deserialize(jsonData, out IRExplorerCore.RawIRModel.ModuleIR module)) {
                var parser = new MLIRParser(irInfo_, errorHandler_, null, section);

                if (module.Functions.Count > 0) {
                    return parser.Parse(module.Functions[0]);
                }
            }

            return null;
        }

        public void SkipCurrentToken() {
            throw new NotImplementedException();
        }

        public void SkipToLineEnd() {
            throw new NotImplementedException();
        }

        public void SkipToLineStart() {
            throw new NotImplementedException();
        }

        public void SkipToNextBlock() {
            throw new NotImplementedException();
        }

        public void SkipToFunctionEnd() {
            throw new NotImplementedException();
        }
    }

    public sealed class MLIRParser : ParserBase {
        private int nextBlockNumber_;
        private FunctionIR function_;

        public MLIRParser(ICompilerIRInfo irInfo, IRParsingErrorHandler errorHandler,
                          RegisterTable registerTable, IRTextSection section) :
            base(irInfo, errorHandler, registerTable, section) {
            idToBlockMap_ = new Dictionary<long, BlockIR>();
            idToInstrMap_ = new Dictionary<long, InstructionIR>();
            idToSourceOperandMap_ = new Dictionary<long, OperandIR>();
            idToResultOperandMap_ = new Dictionary<long, OperandIR>();
            ssaDefinitionMap_ = new Dictionary<long, SSADefinitionTag>();
        }

        public FunctionIR Parse(RawIRModel.FunctionIR rawFunction) {
            Reset();
            function_ = new FunctionIR(rawFunction.Name);

            if(rawFunction.Regions.Count > 0) {
                function_.RootRegion = ParseRegion(rawFunction.Regions[0]);
            }

            return function_;
        }

        protected override void Reset() {
            base.Reset();
            nextBlockNumber_ = 0;
        }

        public RegionIR ParseRegion(RawIRModel.RegionIR rawRegion, RegionIR parentRegion = null, IRElement owner = null) {
            RegionIR region = new RegionIR(NextElementId.NewBlock(nextBlockNumber_), owner, parentRegion);

            foreach (var rawBlock in rawRegion.Blocks) {
                region.Blocks.Add(ParseBlock(rawBlock, region));
            }

            return region;
        }

        public BlockIR ParseBlock(RawIRModel.BlockIR rawBlock, RegionIR parentRegion) {
            BlockIR block = GetOrCreateBlock(rawBlock.Id);
            block.ParentRegion = parentRegion;

            foreach (var rawOperation in rawBlock.Operations) {
                block.Tuples.Add(ParseOperation(rawOperation, block));
            }

            foreach(var predBlock in rawBlock.Predecessors) {
                block.Predecessors.Add(GetOrCreateBlock(predBlock));
            }

            foreach(var succBlock in rawBlock.Successors) {
                block.Successors.Add(GetOrCreateBlock(succBlock));
            }

            block.BlockArguments = new List<OperandIR>();
            InstructionIR dummyInstr = null;

            foreach(var blockArg in rawBlock.Arguments) {
                dummyInstr ??= new InstructionIR(IRElementId.FromLong(0), InstructionKind.Other, block);
                block.BlockArguments.Add(ParseResult(blockArg, dummyInstr));
            }

            function_.Blocks.Add(block);
            return block;
        }

        public InstructionIR ParseOperation(RawIRModel.OperationIR rawInstr, BlockIR parentBlock) {
            InstructionIR instr = GetOrCreateInstruction(rawInstr.Id);
            instr.Opcode = rawInstr.Opcode;
            instr.OpcodeText = rawInstr.Opcode.AsMemory();
            instr.Parent = parentBlock;
            instr.TextLocation = new TextLocation(rawInstr.StartOffset, rawInstr.LineNumber, 0);
            instr.TextLength = rawInstr.EndOffset - rawInstr.StartOffset;

            foreach(var rawSource in rawInstr.Sources) {
                instr.Sources.Add(ParseSource(rawSource, instr));
            }

            foreach(var rawResult in rawInstr.Results) {
                instr.Destinations.Add(ParseResult(rawResult, instr));
            }

            foreach(var rawRegion in rawInstr.Regions) {
                instr.NestedRegions ??= new List<RegionIR>();
                instr.NestedRegions.Add(ParseRegion(rawRegion, parentBlock.ParentRegion, instr));
            }

            return instr;
        }

        public OperandIR ParseSource(RawIRModel.SourceIR rawSource, InstructionIR parentInstr) {
            OperandIR source = GetOrCreateSourceOperand(rawSource.Id);
            source.Parent = parentInstr;
            source.TextLocation = new TextLocation(rawSource.StartOffset, rawSource.LineNumber, 0);
            source.TextLength = rawSource.EndOffset - rawSource.StartOffset;
            source.Role = OperandRole.Source;
            source.Value = new ReadOnlyMemory<char>("src".ToCharArray());
            return source;
        }

        public OperandIR ParseResult(RawIRModel.ResultIR rawResult, InstructionIR parentInstr) {
            OperandIR result = GetOrCreateResultOperand(rawResult.Id);
            result.Parent = parentInstr;
            result.TextLocation = new TextLocation(rawResult.StartOffset, rawResult.LineNumber, 0);
            result.TextLength = rawResult.EndOffset - rawResult.StartOffset;
            result.Role = OperandRole.Destination;
            result.Value = new ReadOnlyMemory<char>("dest".ToCharArray());

            if (rawResult.Uses != null) {
                foreach (var use in rawResult.Uses) {
                    var ssaDefTag = GetOrCreateSSADefinition(rawResult.Id);
                    ssaDefTag.Owner = result;
                    result.AddTag(ssaDefTag);

                    var useOp = GetOrCreateSourceOperand(use.UseId);
                    var ssaUDLinkTag = new SSAUseTag(use.UseId, ssaDefTag) { Owner = result };
                    ssaDefTag.Users.Add(ssaUDLinkTag);
                    useOp.AddTag(ssaUDLinkTag);
                }
            }

            return result;
        }

        private BlockIR GetOrCreateBlock(long id) {
            if(!idToBlockMap_.TryGetValue(id, out var block)) {
                block = new BlockIR(NextElementId.NewBlock(nextBlockNumber_), nextBlockNumber_, function_, null);
                idToBlockMap_.Add(id, block);
                nextBlockNumber_++;
            }

            return block;
        }

        private OperandIR GetOrCreateSourceOperand(long id) {
            if(!idToSourceOperandMap_.TryGetValue(id, out var operand)) {
                operand = new OperandIR(NextElementId.NextOperand(), OperandKind.Temporary, TypeIR.GetUnknown(), null);
                idToSourceOperandMap_.Add(id, operand);
            }

            return operand;
        }

        private OperandIR GetOrCreateResultOperand(long id) {
            if(!idToResultOperandMap_.TryGetValue(id, out var operand)) {
                operand = new OperandIR(NextElementId.NextOperand(), OperandKind.Temporary, TypeIR.GetUnknown(), null);
                idToResultOperandMap_.Add(id, operand);
            }

            return operand;
        }

        private InstructionIR GetOrCreateInstruction(long id) {
            if(!idToInstrMap_.TryGetValue(id, out var instr)) {
                instr = new InstructionIR(NextElementId.NextTuple(), InstructionKind.Other, null);
                idToInstrMap_.Add(id, instr);
            }

            return instr;
        }

        private SSADefinitionTag GetOrCreateSSADefinition(long id) {
            if(!ssaDefinitionMap_.TryGetValue((int)id, out var ssaDefTag)) {
                ssaDefTag = new SSADefinitionTag(id);
                ssaDefinitionMap_.Add(id, ssaDefTag);
            }

            return ssaDefTag;
        }

        private Dictionary<long, OperandIR> idToSourceOperandMap_;
        private Dictionary<long, OperandIR> idToResultOperandMap_;
        private Dictionary<long, BlockIR> idToBlockMap_;
        private Dictionary<long, InstructionIR> idToInstrMap_;
        private Dictionary<long, SSADefinitionTag> ssaDefinitionMap_;
    }
}