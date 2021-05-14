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
        private HashSet<BlockIR> comittedBlocks_;
        private HashSet<BlockIR> referencedBlocks_;

        public ASMParser(IRMode irMode, IRParsingErrorHandler errorHandler,
                         RegisterTable registerTable,
                         ReadOnlyMemory<char> sectionText)
            : base(irMode, errorHandler, registerTable) {

            Trace.WriteLine("About to parse this:");
            Trace.WriteLine(sectionText);

            Initialize(sectionText);
            SkipToken();
        }

        public ASMParser(IRMode irMode, IRParsingErrorHandler errorHandler,
            RegisterTable registerTable,
            string sectionText)
            : base(irMode, errorHandler, registerTable) {
            
            Trace.WriteLine("About to parse this:");
            Trace.WriteLine(sectionText);

            Reset();
            Initialize(sectionText);
            SkipToken();
        }

        private void Reset() {
            base.Reset();
            makeNewBlock_ = true;
            addressToBlockNumberMap_ = new Dictionary<long, int>();
            comittedBlocks_ = new HashSet<BlockIR>();
            referencedBlocks_ = new HashSet<BlockIR>();
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

        public FunctionIR Parse() {
            var function = new FunctionIR();
            Token startElement = default;
            BlockIR block = null;

            while (!IsEOF()) {
                if(makeNewBlock_) {
                    // Make a new block.
                    if (Current.Kind == TokenKind.Number &&
                        NextTokenIs(TokenKind.Colon) &&
                        TokenLongHexNumber(out long address)) {
                        if (block != null) {
                            
                        }
                        
                        var newBlock = GetOrCreateBlock(address, function);

                        if (block != null && connectNewBlock_) {
                            ConnectBlocks(block, newBlock);
                        }

                        function.Blocks.Add(newBlock);
                        comittedBlocks_.Add(newBlock);
                        block = newBlock;
                        startElement = Current;
                    }

                    makeNewBlock_ = false;
                    connectNewBlock_ = false;
                }

                //Trace.WriteLine($"{Current.Kind}: {Current.Data}");
                if(ParseLine(block)) {
                    SetTextRange(block, startElement, previous_);
                }

                SkipToLineStart();
            }

            if (block != null) {
                SetTextRange(block, startElement, Current);
            }

            // Add any remaining block referenced by jumps.
            foreach(var refBlock in referencedBlocks_) {
                if(!comittedBlocks_.Contains(refBlock)) {
                    function.Blocks.Add(refBlock);
                }
            }

            // Renumber blocks to follow text order.
            int blockNumber = 0;
            var blockOrdering = new CFGBlockOrdering(function);

            blockOrdering.ReversePostorderWalk((b, index) => {
                b.Number = blockNumber++;
                return true;
            });

            AddMetadata(function);
            return function;
        }

        private void ConnectBlocks(BlockIR block, BlockIR newBlock) {
            if(block.Successors.Contains(newBlock)) {
                return;
            }

            block.Successors.Add(newBlock);
            newBlock.Predecessors.Add(block);
        }

        private bool ParseLine(BlockIR block) {
            var startToken = Current;
            bool isJump = false;

            if (Current.Kind != TokenKind.Number) {
                ReportErrorAndSkipLine(TokenKind.Number, "Expected line to start with an address");
                return false;
            }

            long? address = ParseHexAddress();
            if (!address.HasValue) {
                ReportErrorAndSkipLine(TokenKind.Number, "Expected line to start with an address");
                return false;
            }

            if (!ExpectAndSkipToken(TokenKind.Colon)) {
                // Instrs. with more than 6 bytes extend on multiple lines.
                // 0000000140068023: 49 BF 70 89 DE 5E  mov         r15,9375B7955EDE8970h
                //                   95 B7 75 93
                if (previousInstr_ != null) {
                    int prevInstrSize = 1;
                    prevInstrSize = CountInstructionBytes(prevInstrSize);
                    MetadataTag.ElementSizeMap[previousInstr_] += prevInstrSize;
                    return false;
                }

                ReportErrorAndSkipLine(TokenKind.Colon, "Expected a colon to follow the address");
                return false;
            }

            // Skip over the list of instruction bytecodes.
            int instrSize = 0;
            instrSize = CountInstructionBytes(instrSize);

            var instr = new InstructionIR(NextElementId, InstructionKind.Other, block);
            block.Tuples.Add(instr);
            previousInstr_ = instr;

            // Extract the opcode.
            if (IsIdentifier()) {
                //? TODO: Use either x86Opcodes/ARMOpcodes
                instr.Opcode = TokenString().ToUpperInvariant();
                instr.OpcodeText = TokenData();
                instr.OpcodeLocation = Current.Location;
                instr.Kind = GetInstructionKind((string)instr.Opcode);

                if (instr.Kind == InstructionKind.Branch) {
                    instr.Kind = InstructionKind.Branch;
                    isJump = true;
                    makeNewBlock_ = true;
                    connectNewBlock_ = true; // Fall-through.
                }
                else if (instr.Kind == InstructionKind.Goto) {
                    instr.Kind = InstructionKind.Goto;
                    isJump = true;
                    makeNewBlock_ = true;
                    connectNewBlock_ = false;
                }
                else if (instr.Kind == InstructionKind.Return) {
                    instr.Kind = InstructionKind.Return;
                    isJump = true;
                    makeNewBlock_ = true;
                    connectNewBlock_ = false;
                }

                SkipToken(); // Skip opcode.

                if (isJump) {
                    // Connect the block with the jump target.
                    if (Current.Kind == TokenKind.Number) {
                        long? targetAddress = ParseHexAddress();
                        var targetBlock = GetOrCreateBlock(targetAddress.Value, block.ParentFunction);
                        ConnectBlocks(block, targetBlock);
                        referencedBlocks_.Add(targetBlock);
                    }
                }
                else {
                    ParseOperandList(instr, instr.Sources);

                    //? TODO: OPEQ add first source as dest
                }
            }

            // Set the size of the previous instruction.
            initialAddress_ ??= address.Value;
            var offset = address.Value - initialAddress_.Value;

            MetadataTag.AddressToElementMap[address.Value] = instr;
            MetadataTag.OffsetToElementMap[offset] = instr;
            MetadataTag.ElementToOffsetMap[instr] = offset;
            MetadataTag.ElementSizeMap[instr] = instrSize;

            SkipToLineEnd();
            SetTextRange(instr, startToken, Current, adjustment: 1);
            return isJump;
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
            else if (IsNumber() || TokenIs(TokenKind.Minus)) { // int/float const
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
            var startToken = Current;
            var opKind = OperandKind.Other;
            object opValue = null;
            bool isNegated = false;

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

            var startToken = Current;
            SkipToken();
            SetTextRange(operand, startToken);
            return operand;
        }

        private OperandIR ParseIndirection(TupleIR parent) {
            var startToken = Current;
            SkipToken();
            var baseOp = ParseOperand(parent, true);

            // After lowering, indirections can have multiple operands
            // like [base+index+offset].
            //? TODO: Save the extra ops
            while (!TokenIs(TokenKind.CloseSquare)) {
                // Skip over + or *.
                SkipToNextOperand();
                ExpectAndSkipToken(TokenKind.Plus, TokenKind.Star);

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
            while (ParseHexAddress().HasValue) {
                instrSize++;
            }

            return instrSize;
        }

        private InstructionKind GetInstructionKind(string opcode) {
            switch (irMode_) {
                case IRMode.x86: {
                    if (x86Opcodes.GetOpcodeInfo(opcode, out var info)) {
                        return info.Kind;
                    }
                    break; 
                }
                case IRMode.ARM64: {
                    //? TODO
                    break;
                }
            }

            return InstructionKind.Other;
        }

        private Keyword TokenKeyword() {
            if (Current.IsIdentifier()) {
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
