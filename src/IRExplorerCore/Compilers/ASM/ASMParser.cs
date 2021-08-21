using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using IRExplorerCore.IR;
using IRExplorerCore.Lexer;
using IRExplorerCore.Analysis;

namespace IRExplorerCore.ASM {
    public sealed class ASMParser : ParserBase {
        enum Keyword {
            None,
            Byte,
            Word,
            Dword,
            Qword,
            Ptr,
            Hex
        }

        private static Dictionary<string, Keyword> keywordMap_ =
            new Dictionary<string, Keyword> {
                {"byte", Keyword.Byte},
                {"word", Keyword.Word},
                {"dword", Keyword.Dword},
                {"qword", Keyword.Qword},
                {"ptr", Keyword.Ptr},
                {"BYTE", Keyword.Byte},
                {"WORD", Keyword.Word},
                {"DWORD", Keyword.Dword},
                {"QWORD", Keyword.Qword},
                {"PTR", Keyword.Ptr},
                {"h", Keyword.Hex},
                {"H", Keyword.Hex},
            };

        static readonly StringTrie<Keyword> keywordTrie_ = new StringTrie<Keyword>(keywordMap_);

        private long? initialAddress_;
        private bool makeNewBlock_;
        private bool connectNewBlock_;
        private InstructionIR previousInstr_;
        private Dictionary<long, int> addressToBlockNumberMap_;
        private Dictionary<long, Tuple<TextLocation, int>> potentialLabelMap_;
        private HashSet<BlockIR> comittedBlocks_;
        private Dictionary<BlockIR, long> referencedBlocks_;

        public ASMParser(ICompilerIRInfo irInfo, IRParsingErrorHandler errorHandler,
                         RegisterTable registerTable,
                         ReadOnlyMemory<char> sectionText, IRTextSection section)
            : base(irInfo, errorHandler, registerTable, section) {
            Reset();
            Initialize(sectionText);
            MetadataTag.EnsureCapacity(section.LineCount + 1);
            SkipToken();
        }

        public ASMParser(ICompilerIRInfo irInfo, IRParsingErrorHandler errorHandler,
            RegisterTable registerTable,
            string sectionText, IRTextSection section)
            : base(irInfo, errorHandler, registerTable, section) {
            Reset();
            Initialize(sectionText);
            MetadataTag.EnsureCapacity(section.LineCount + 1);
            SkipToken();
        }

        private void Reset() {
            base.Reset();
            makeNewBlock_ = true;
            addressToBlockNumberMap_ = new Dictionary<long, int>();
            potentialLabelMap_ = new Dictionary<long, Tuple<TextLocation, int>>();
            comittedBlocks_ = new HashSet<BlockIR>();
            referencedBlocks_ = new Dictionary<BlockIR, long>();
        }

        private int GetBlockNumber(long address) {
            if(addressToBlockNumberMap_.TryGetValue(address, out var number)) {
                return number;
            }

            number = addressToBlockNumberMap_.Count;
            addressToBlockNumberMap_[address] = number;
            return number;
        }

        private BlockIR GetOrCreateBlock(long address, FunctionIR function) {
            int number = GetBlockNumber(address);
            return base.GetOrCreateBlock(number, function);
        }

        private BlockLabelIR GetOrCreateBlockLabel(BlockIR block) {
            // Create a dummy label to be set later when parsing the block.
            if (block.Label == null) {
                block.Label = new BlockLabelIR(NextElementId, ReadOnlyMemory<char>.Empty, block);
            }
            
            return block.Label;
        }

        public FunctionIR Parse() {
            var function = new FunctionIR(section_.ParentFunction.Name);
            Token startElement = default;
            BlockIR block = null;

            while (!IsEOF()) {
                if (makeNewBlock_) {
                    // Make a new block.
                    if (current_.Kind == TokenKind.Number &&
                        NextTokenIs(TokenKind.Colon) &&
                        TokenLongHexNumber(out long address)) {
                        var newBlock = GetOrCreateBlock(address, function);
                        var blockLabel = GetOrCreateBlockLabel(newBlock);

                        blockLabel.TextLocation = current_.Location;
                        blockLabel.TextLength = current_.Length;

                        if (block != null && connectNewBlock_) {
                            ConnectBlocks(block, newBlock);
                        }

                        function.Blocks.Add(newBlock);
                        comittedBlocks_.Add(newBlock);
                        block = newBlock;
                        startElement = current_;
                    }

                    makeNewBlock_ = false;
                    connectNewBlock_ = false;
                }

                if (ParseLine(block)) {
                    // Block ended with this line.
                    SetTextRange(block, startElement, previous_);
                }

                SkipToLineStart();
            }

            if (block != null) {
                SetTextRange(block, startElement, current_);
            }

            FixBlockReferences(function);
            AssignBlockNumbers(function);
            AddMetadata(function);
            return function;
        }

        private void AssignBlockNumbers(FunctionIR function) {
            if(function.Blocks.Count == 0) {
                return;
            }

            // Renumber blocks to follow text order.
            int blockNumber = 0;
            var blockOrdering = new CFGBlockOrdering(function);

            blockOrdering.ReversePostorderWalk((b, index) => {
                b.Number = blockNumber++;
                return true;
            });

            // The last block (after RET) is usually unreachable, remove it.
            if (function.EntryBlock != function.ExitBlock &&
                function.ExitBlock.Predecessors.Count == 0) {
                function.Blocks.Remove(function.ExitBlock);
            }
        }

        private void FixBlockReferences(FunctionIR function) {
            // Add any remaining block referenced by jumps.
            foreach (var pair in referencedBlocks_) {
                var refBlock = pair.Key;
                var refAddress = pair.Value;

                if (comittedBlocks_.Contains(refBlock)) {
                    continue;
                }

                // Found a referenced label, but there is no block created for it.
                // This happens when there is no jump/branch before the label.
                bool blockAdded = false;

                if (potentialLabelMap_.TryGetValue(refAddress, out var location)) {
                    var label = GetOrCreateBlockLabel(refBlock);
                    label.TextLocation = location.Item1;
                    label.TextLength = location.Item2;
                    refBlock.TextLocation = location.Item1;

                    // Check if there is an overlapping block and split it at the label,
                    // move the tuples following the label to the new block.
                    for (int i = 0; i < function.Blocks.Count; i++) {
                        var otherBlock = function.Blocks[i];

                        if(otherBlock == refBlock) {
                            continue;
                        }

                        if (otherBlock.TextLocation.Offset <= location.Item1.Offset &&
                            otherBlock.TextLocation.Offset + otherBlock.TextLength > location.Item1.Offset) {
                            var offsetDiff = location.Item1.Offset - otherBlock.TextLocation.Offset;
                            refBlock.TextLength = otherBlock.TextLength - offsetDiff;
                            otherBlock.TextLength = offsetDiff - 1;

                            // Move sucessor blocks from otherBlock to refBlock.
                            foreach(var succBlock in otherBlock.Successors) {
                                refBlock.Successors.Add(succBlock);
                                succBlock.Predecessors.Remove(otherBlock);
                                succBlock.Predecessors.Add(refBlock);
                            }

                            otherBlock.Successors.Clear();
                            ConnectBlocks(otherBlock, refBlock);

                            // Move the tuples to the new block.
                            int splitIndex = 0;
                            int copiedTuples = 0;

                            for (; splitIndex < otherBlock.Tuples.Count; splitIndex++) {
                                var tuple = otherBlock.Tuples[splitIndex];

                                if (tuple.TextLocation.Offset >= location.Item1.Offset) {
                                    refBlock.Tuples.Add(tuple);
                                    tuple.Parent = refBlock;
                                    copiedTuples++;
                                }
                            }

                            if (copiedTuples > 0) {
                                otherBlock.Tuples.RemoveRange(otherBlock.Tuples.Count - copiedTuples, copiedTuples);
                            }

                            // Add block after the other one.
                            function.Blocks.Insert(i + 1, refBlock);
                            blockAdded = true;
                            break;
                        }
                    }

                    comittedBlocks_.Add(refBlock);
                }

                if (!blockAdded) {
                    function.Blocks.Add(refBlock);
                }
            }
        }

        private void ConnectBlocks(BlockIR block, BlockIR newBlock) {
            if(block.Successors.Contains(newBlock)) {
                return;
            }

            block.Successors.Add(newBlock);
            newBlock.Predecessors.Add(block);
        }

        private bool ParseLine(BlockIR block) {
            var startToken = current_;
            long address = 0;

            if (!(IsNumber() || IsIdentifier()) ||
                !TokenLongHexNumber(out address)) {
                ReportErrorAndSkipLine(TokenKind.Number, "Expected line to start with an address");
                return false;
            }

            // Record address to be used for jump in the middle of blocks.
            potentialLabelMap_[address] = new Tuple<TextLocation, int>(current_.Location, current_.Length);
            SkipToken();

            if (!ExpectAndSkipToken(TokenKind.Colon)) {
                // Instrs. with more than 6 bytes extend on multiple lines.
                // 0000000140068023: 49 BF 70 89 DE 5E  mov         r15,9375B7955EDE8970h
                //                   95 B7 75 93
                if (previousInstr_ != null) {
                    int prevInstrSize = 1;
                    prevInstrSize = CountInstructionBytes(prevInstrSize);
                    MetadataTag.ElementSizeMap[previousInstr_] += prevInstrSize;
                    MetadataTag.FunctionSize += prevInstrSize;
                    return false;
                }

                ReportErrorAndSkipLine(TokenKind.Colon, "Expected a colon to follow the address");
                return false;
            }

            // Skip over the list of instruction bytecodes.
            int instrSize = 0;
            instrSize = CountInstructionBytes(instrSize);
            var (instr, isJump) = ParseInstruction(block);

            // Update metadata.
            initialAddress_ ??= address;
            var offset = address - initialAddress_.Value;

            MetadataTag.AddressToElementMap[address] = instr;
            MetadataTag.OffsetToElementMap[offset] = instr;
            MetadataTag.ElementToOffsetMap[instr] = offset;
            MetadataTag.ElementSizeMap[instr] = instrSize;
            MetadataTag.FunctionSize += instrSize;

            SetTextRange(instr, startToken, current_, adjustment: 1);
            SkipToLineEnd();
            return isJump; // A jump ends the current block.
        }

        private (InstructionIR, bool) ParseInstruction(BlockIR block) {
            bool isJump = false;
            var instr = new InstructionIR(NextElementId, InstructionKind.Other, block);
            block.AddTuple(instr);
            previousInstr_ = instr;

            // Extract the opcode.
            if (IsIdentifier()) {
                SetInstructionOpcode(instr);

                if (instr.Kind == InstructionKind.Branch) {
                    isJump = true;
                    makeNewBlock_ = true;
                    connectNewBlock_ = true; // Fall-through.
                }
                else if (instr.Kind == InstructionKind.Goto) {
                    isJump = true;
                    makeNewBlock_ = true;
                    connectNewBlock_ = false;
                }
                else if (instr.Kind == InstructionKind.Return) {
                    isJump = true;
                    makeNewBlock_ = true;
                    connectNewBlock_ = false;
                }

                SkipToken(); // Skip opcode.
                ParseOperandList(instr, instr.Sources);

                if (isJump) {
                    // Connect the block with the jump target.
                    var targetOp = irInfo_.GetBranchTarget(instr);

                    if (targetOp != null && targetOp.IsIntConstant) {
                        var targetAddress = targetOp.IntValue;
                        var targetBlock = GetOrCreateBlock(targetAddress, block.ParentFunction);
                        ConnectBlocks(block, targetBlock);
                        referencedBlocks_[targetBlock] = targetAddress;

                        instr.Sources[0].Kind = OperandKind.LabelAddress;
                        instr.Sources[0].Value = GetOrCreateBlockLabel(targetBlock);
                    }
                }
                else {
                    if (instr.Sources.Count > 0) {
                        instr.Destinations.Add(instr.Sources[0]);
                    }

                    //? TODO: OPEQ add first source as dest
                }
            }

            return (instr, isJump);
        }

        private bool ParseOperandList(InstructionIR instr, List<OperandIR> list) {
            while (!IsLineEnd()) {
                var operand = ParseOperand(instr, false);

                if (operand == null) {
                    return false;
                }

                operand.Role = OperandRole.Source;
                list.Add(operand);

                if (IsComma()) {
                    SkipToken(); // More operands after ,
                }
                else {
                    break;
                }
            }

            return true;
        }

        private OperandIR ParseOperand(TupleIR parent, bool isIndirBaseOp = false,
                                      bool isBlockLabelRef = false,
                                      bool disableSkipToNext = false) {
            SkipKeywords(); // Skip DWORD PTR, etc.
            OperandIR operand = null;

            // operand = varOp | intOp | floatOp | | addressOp | indirOp | labelOp | pasOp
            if (IsIdentifier()) {
                // Variable/temporary.
                //? TODO: If it starts with @ it's address
                operand = ParseVariableOperand(parent, isIndirBaseOp);
            }
            else if (IsNumber() || TokenIs(TokenKind.Minus) || TokenIs(TokenKind.Hash)) { // int/float const
                operand = ParseNumber(parent);
            }
            else if (TokenIs(TokenKind.OpenSquare)) { // [indir]
                if (isIndirBaseOp) {
                    ReportError(TokenKind.OpenSquare, "Failed ParseOperand nested INDIR");
                    return null; // Nested [indir] not allowed.
                }

                operand = ParseIndirection(parent);
            }

            SkipToNextOperand();
            return operand;
        }

        private OperandIR ParseNumber(TupleIR parent) {
            var startToken = current_;
            var opKind = OperandKind.Other;
            object opValue = null;
            bool isNegated = false;

            // ARM64 assembly can have a # in front of a number like in #0x30.
            if (TokenIs(TokenKind.Hash)) {
                SkipToken();
            }

            if (TokenIs(TokenKind.Minus)) {
                SkipToken();
                isNegated = true;
            }

            if (TokenLongHexNumber(out long intValue)) {
                // intConst = DECIMAL [(0xHEX)] [.type]
                SkipToken();
                opKind = OperandKind.IntConstant;

                unchecked {
                    opValue = isNegated ? -intValue : intValue;
                }

                SkipKeyword(Keyword.Hex); // Skip optional h suffix.
            }
            else {
                ReportError(TokenKind.Number, "Failed ParseNumber");
                return null;
            }

            var type = TypeIR.GetUnknown();
            var operand = CreateOperand(NextElementId, opKind, type, parent);
            operand.Value = opValue;
            SetTextRange(operand, startToken);
            return operand;
        }

        private OperandIR ParseVariableOperand(TupleIR parent, bool isIndirBaseOp = false) {
            // Save variable name.
            var opName = TokenData();
            var operand = CreateOperand(NextElementId, OperandKind.Variable, TypeIR.GetUnknown(), parent);
            operand.Value = opName;

            // Try to associate with a register.
            var register = RegisterTable.GetRegister(TokenString());

            if (register != null) {
                operand.AddTag(new RegisterTag(register, operand));
            }

            var startToken = current_;
            SkipToken();
            SetTextRange(operand, startToken);
            return operand;
        }

        private OperandIR ParseIndirection(TupleIR parent) {
            var startToken = current_;
            SkipToken();
            var baseOp = ParseOperand(parent, true);

            // After lowering, indirections can have multiple operands
            // like [base+index+offset].
            //? TODO: Save the extra ops
            while (!TokenIs(TokenKind.CloseSquare)) {
                // Skip over + or *.
                SkipToNextOperand();
                ExpectAndSkipToken(TokenKind.Plus, TokenKind.Star, TokenKind.Comma);

                if (ParseOperand(parent, true) == null) {
                    break;
                }
                
                //? TODO: Add to list - maybe introduce IndirectOperand
            }

            var operand = CreateOperand(NextElementId, OperandKind.Indirection,
                                        TypeIR.GetUnknown(), parent);
            operand.Value = baseOp;

            if (!ExpectAndSkipToken(TokenKind.CloseSquare)) {
                ReportError(TokenKind.CloseSquare, "Failed ParseIndirection");
                return null;
            }

            SetTextRange(operand, startToken);
            return operand;
        }

        private void SkipToNextOperand() {
            while (!(TokenIs(TokenKind.Comma) || IsLineEnd())) {
                if (TokenIs(TokenKind.OpenParen)) {
                    SkipAfterToken(TokenKind.CloseParen);
                }
                else if (TokenIs(TokenKind.OpenCurly)) {
                    SkipAfterToken(TokenKind.CloseCurly);
                }
                else if (TokenIs(TokenKind.Less)) {
                    SkipAfterToken(TokenKind.Greater);
                }
                else {
                    break;
                }
            }
        }

        private OperandIR CreateOperand(IRElementId elementId, OperandKind kind,
                                        TypeIR type, TupleIR parent) {
#if USE_POOL
            var op = operandPool_.Get();
            op.Id = elementId.NextOperand();
            op.Kind = kind;
            op.Type = type;
            op.Parent = parent;
            return op;
#else
            return new OperandIR(elementId, kind, type, parent);
#endif
        }

        private int CountInstructionBytes(int instrSize) {
            // For ARM64, the bytecodes are not found in the assembly listing
            // and each instruction is 4 bytes.
            if (irInfo_.Mode == IRMode.ARM64) {
                SkipHexNumber(8);
                return 4;
            }

            while (SkipHexNumber(2)) { // Groups of 2 digits.
                instrSize++;
            }

            return instrSize;
        }

        private void SetInstructionOpcode(InstructionIR instr) {
            instr.OpcodeText = TokenData();
            instr.OpcodeLocation = current_.Location;

            switch (irInfo_.Mode) {
                case IRMode.x86_64: {
                    if (x86Opcodes.GetOpcodeInfo(instr.OpcodeText, out var info)) {
                        instr.Opcode = info.Opcode;
                        instr.Kind = info.Kind;
                    }
                    break; 
                }
                case IRMode.ARM64: {
                    if (ARMOpcodes.GetOpcodeInfo(instr.OpcodeText, out var info)) {
                        instr.Opcode = info.Opcode;
                        instr.Kind = info.Kind;
                    }
                    break;
                }
                default: {
                    throw new NotImplementedException();
                }
            }
        }

        private Keyword TokenKeyword() {
            if (current_.IsIdentifier()) {
                if (keywordTrie_.TryGetValue(TokenStringData(), out var keyword)) {
                    return keyword;
                }
            }

            return Keyword.None;
        }

        private bool IsKeyword() {
            return TokenKeyword() != Keyword.None;
        }

        private void SkipKeyword(Keyword kind) {
            if(TokenKeyword() == kind) {
                SkipToken();
            }
        }

        private void SkipKeywords() {
            while(IsKeyword()) {
                SkipToken();
            }
        }
    }
}
