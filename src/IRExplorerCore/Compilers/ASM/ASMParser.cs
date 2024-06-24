// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using IRExplorerCore.Analysis;
using IRExplorerCore.Collections;
using IRExplorerCore.IR;
using IRExplorerCore.Lexer;

namespace IRExplorerCore.ASM;

public sealed class ASMParser : ParserBase {
  private static Dictionary<string, Keyword> keywordMap_ =
    new Dictionary<string, Keyword> {
      {"byte", Keyword.Byte},
      {"word", Keyword.Word},
      {"dword", Keyword.Dword},
      {"qword", Keyword.Qword},
      {"xmmword", Keyword.Xmmword},
      {"ymmword", Keyword.Ymmword},
      {"Zmmword", Keyword.Zmmword},
      {"ptr", Keyword.Ptr},
      {"BYTE", Keyword.Byte},
      {"WORD", Keyword.Word},
      {"DWORD", Keyword.Dword},
      {"QWORD", Keyword.Qword},
      {"XMMWORD", Keyword.Xmmword},
      {"YMMWORD", Keyword.Ymmword},
      {"ZMMWORD", Keyword.Zmmword},
      {"PTR", Keyword.Ptr},
      {"h", Keyword.Hex},
      {"H", Keyword.Hex}
    };
  private static readonly StringTrie<Keyword> keywordTrie_ = new StringTrie<Keyword>(keywordMap_);

  //? TODO: ILT+foo func names not parsed properly
  private long functionSize_;
  private long? initialAddress_;
  private bool makeNewBlock_;
  private bool connectNewBlock_;
  private InstructionIR previousInstr_;
  private long previousInstrAddress_;
  private int instrCount_;
  private Dictionary<long, int> addressToBlockNumberMap_;
  private Dictionary<long, Tuple<TextLocation, int>> potentialLabelMap_;
  private HashSet<BlockIR> committedBlocks_;
  private Dictionary<BlockIR, long> referencedBlocks_;

  public ASMParser(ICompilerIRInfo irInfo, IRParsingErrorHandler errorHandler,
                   RegisterTable registerTable, ReadOnlyMemory<char> sectionText,
                   IRTextSection section, long functionSize)
    : base(irInfo, errorHandler, registerTable, section) {
    Reset();
    Initialize(sectionText);
    functionSize_ = functionSize;
    MetadataTag.EnsureCapacity(section.LineCount + 1);
    SkipToken();
  }

  public ASMParser(ICompilerIRInfo irInfo, IRParsingErrorHandler errorHandler,
                   RegisterTable registerTable, string sectionText,
                   IRTextSection section, long functionSize)
    : base(irInfo, errorHandler, registerTable, section) {
    Reset();
    Initialize(sectionText);
    functionSize_ = functionSize;
    MetadataTag.EnsureCapacity(section.LineCount + 1);
    SkipToken();
  }

  private enum Keyword {
    None,
    Byte,
    Word,
    Dword,
    Qword,
    Xmmword,
    Ymmword,
    Zmmword,
    Ptr,
    Hex
  }

  public FunctionIR Parse() {
    var function = new FunctionIR(section_.ParentFunction.Name);
    Token startElement = default;
    BlockIR block = null;

    while (!IsEOF()) {
      if (makeNewBlock_) {
        // Make a new block.
        if ((IsNumber() || IsIdentifier()) &&
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
          committedBlocks_.Add(newBlock);
          block = newBlock;
          startElement = current_;
        }
        else {
          // Skip over unknown text, such as the function name at the start.
          SkipToLineStart();
          continue;
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

    Debug.Assert(function.InstructionCount == instrCount_);
    Debug.Assert(function.TupleCount == instrCount_);

    SetLastInstructionSize();
    FixBlockReferences(function);
    AssignBlockNumbers(function);
    AddMetadata(function);
    return function;
  }

  private void Reset() {
    base.Reset();
    makeNewBlock_ = true;
    addressToBlockNumberMap_ = new Dictionary<long, int>();
    potentialLabelMap_ = new Dictionary<long, Tuple<TextLocation, int>>();
    committedBlocks_ = new HashSet<BlockIR>();
    referencedBlocks_ = new Dictionary<BlockIR, long>();
  }

  private int GetBlockNumber(long address) {
    if (addressToBlockNumberMap_.TryGetValue(address, out int number)) {
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

  private void AssignBlockNumbers(FunctionIR function) {
    if (function.Blocks.Count == 0) {
      return;
    }

    // Renumber blocks to follow text order.
    int blockNumber = 0;
    var blockOrdering = new CFGBlockOrdering(function);

    blockOrdering.ReversePostorderWalk((b, index) => {
      b.Number = blockNumber++;
      return true;
    });

    // Assign block index as they show up in the text,
    // not RDFO or how the blocks where forward-referenced.
    function.AssignBlockIndices(true);

    // The last block (after RET) is usually unreachable, remove it.
    if (function.EntryBlock != function.ExitBlock &&
        function.ExitBlock.Predecessors.Count == 0) {
      function.Blocks.Remove(function.ExitBlock);
    }
  }

  private void FixBlockReferences(FunctionIR function) {
    // Add any remaining blocks referenced by jumps.
    foreach (var pair in referencedBlocks_) {
      var refBlock = pair.Key;
      long refAddress = pair.Value;

      if (committedBlocks_.Contains(refBlock)) {
        continue;
      }

      // Found a referenced label, but there is no block created for it.
      // This happens when there is no jump/branch before the label.
      // In practice this is fast, although in worst case it's #labels * #blocks complexity.
      bool blockAdded = false;

      if (potentialLabelMap_.TryGetValue(refAddress, out var labelLocation)) {
        var label = GetOrCreateBlockLabel(refBlock);
        label.TextLocation = labelLocation.Item1;
        label.TextLength = labelLocation.Item2;
        refBlock.TextLocation = labelLocation.Item1;

        // Check if there is an overlapping block and split it at the label,
        // move the tuples following the label to the new block.
        // |otherBlock|1|2|..|refBlock label|3|4|..|  =>  |otherBlock|1|2|..| -> |refBlock label|1|2|..|
        for (int i = 0; i < function.Blocks.Count; i++) {
          var otherBlock = function.Blocks[i];

          if (otherBlock == refBlock) {
            continue;
          }

          if (otherBlock.TextLocation <= labelLocation.Item1 &&
              otherBlock.TextLocation.Offset + otherBlock.TextLength > labelLocation.Item1.Offset) {
            int offsetDiff = labelLocation.Item1.Offset - otherBlock.TextLocation.Offset;
            refBlock.TextLength = otherBlock.TextLength - offsetDiff;

            // Move successor blocks from otherBlock to refBlock.
            foreach (var succBlock in otherBlock.Successors) {
              refBlock.Successors.Add(succBlock);
              succBlock.Predecessors.Remove(otherBlock);
              succBlock.Predecessors.Add(refBlock);
            }

            otherBlock.Successors.Clear();

            // Move the tuples to the new block.
            int splitIndex = 0;
            int copiedTuples = 0;

            for (; splitIndex < otherBlock.Tuples.Count; splitIndex++) {
              var tuple = otherBlock.Tuples[splitIndex];

              if (tuple.TextLocation >= labelLocation.Item1) {
                refBlock.Tuples.Add(tuple);
                tuple.Parent = refBlock;
                tuple.IndexInBlock = copiedTuples;
                copiedTuples++;
              }
            }

            // If the block has only NOP, don't connect it, it leaves
            // the NOP block without any predecessor.
            ConnectBlocks(otherBlock, refBlock);

            if (copiedTuples > 0) {
              otherBlock.Tuples.RemoveRange(otherBlock.Tuples.Count - copiedTuples, copiedTuples);

              if (otherBlock.Tuples.Count > 0) {
                otherBlock.TextLength = otherBlock.Tuples[^1].TextLocation.Offset +
                                        otherBlock.Tuples[^1].TextLength -
                                        otherBlock.TextLocation.Offset;
              }
              else {
                otherBlock.TextLength = offsetDiff - 1;
              }
            }

            // Insert block after the other one.
            function.Blocks.Insert(i + 1, refBlock);
            blockAdded = true;
            break; // Stop, there can't be more than one overlapping block.
          }
        }

        committedBlocks_.Add(refBlock);
      }

      if (!blockAdded) {
        function.Blocks.Add(refBlock);
      }
    }
  }

  private void ConnectBlocks(BlockIR block, BlockIR newBlock) {
    if (block.Successors.Contains(newBlock)) {
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
      // Ignore lines that don't start with an address (comments, etc).
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
        SkipInstructionBytes();
        return false;
      }

      ReportErrorAndSkipLine(TokenKind.Colon, "Expected a colon to follow the address");
      return false;
    }

    // Skip over the list of instruction bytecodes.
    SkipInstructionBytes();
    (var instr, bool isJump) = ParseInstruction(block);

    // Update metadata.
    initialAddress_ ??= address;
    long offset = address - initialAddress_.Value;

    MetadataTag.AddressToElementMap[address] = instr;
    MetadataTag.OffsetToElementMap[offset] = instr;
    MetadataTag.ElementToOffsetMap[instr] = offset;
    SetPreviousInstructionSize(address);

    previousInstr_ = instr;
    previousInstrAddress_ = address;

    SetTextRange(instr, startToken, current_, 1);
    SkipToLineEnd();
    return isJump; // A jump ends the current block.
  }

  private void SetPreviousInstructionSize(long address) {
    if (previousInstr_ != null) {
      int instrSize = (int)(address - previousInstrAddress_);
      MetadataTag.ElementSizeMap[previousInstr_] = instrSize;
      MetadataTag.FunctionSize += instrSize;
    }
  }

  private void SetLastInstructionSize() {
    if (previousInstr_ != null) {
      int instrSize = (int)(functionSize_ - MetadataTag.FunctionSize);
      MetadataTag.ElementSizeMap[previousInstr_] = instrSize;
      MetadataTag.FunctionSize += instrSize;
    }
  }

  private (InstructionIR, bool) ParseInstruction(BlockIR block) {
    bool isJump = false;
    var instr = new InstructionIR(NextElementId, InstructionKind.Other, block);
    block.AddTuple(instr);
    instrCount_++;

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
          long targetAddress = targetOp.IntValue;
          var targetBlock = GetOrCreateBlock(targetAddress, block.ParentFunction);
          ConnectBlocks(block, targetBlock);
          referencedBlocks_[targetBlock] = targetAddress;

          int opIndex = instr.Sources.IndexOf(targetOp);
          instr.Sources[opIndex].Kind = OperandKind.LabelAddress;
          instr.Sources[opIndex].Value = GetOrCreateBlockLabel(targetBlock);
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
      var operand = ParseOperand(instr);

      if (operand == null) {
        return false;
      }

      operand.Role = OperandRole.Source;
      list.Add(operand);

      if (IsComma()) {
        SkipToken(); // More operands after ,
      }
      else {
        if (instr.Kind == InstructionKind.Call &&
            operand.IsVariable && operand.HasName) {
          // With demangled names, there can be multiple tokens
          // that form the complete function name, append them together.
          var sb = new StringBuilder();
          sb.Append(operand.Name);

          int funcNameLength = operand.TextLength;
          int prevTokenEnd = operand.TextLocation.Offset + operand.TextLength;

          while (!IsLineEnd()) {
            // Append whitespace if there is some between tokens.
            int spaceCount = current_.Location.Offset - prevTokenEnd;

            if (spaceCount > 0) {
              sb.Append(lexer_.GetText(prevTokenEnd, spaceCount));
              funcNameLength += spaceCount;
            }

            sb.Append(lexer_.GetTokenText(current_));
            funcNameLength += current_.Length;
            prevTokenEnd = current_.Location.Offset + current_.Length;
            SkipToken();
          }

          operand.Value = sb.ToString().AsMemory();
          operand.TextLength = funcNameLength;
        }

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

    // operand = varOp | intOp | floatOp | addressOp | indirOp | labelOp | pasOp
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
      if (TokenIs(TokenKind.Dot)) {
        // Skip over ARM64 operand annotations like v22.4s
        SkipToToken(TokenKind.Comma);
      }
      else if (TokenIs(TokenKind.OpenParen)) {
        SkipAfterToken(TokenKind.CloseParen);
      }
      else if (TokenIs(TokenKind.OpenCurly)) {
        SkipAfterToken(TokenKind.CloseCurly);
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

  private void SkipInstructionBytes() {
    // For ARM64 each instruction is 4 bytes.
    if (irInfo_.Mode == IRMode.ARM64) {
      if (SkipHexNumber(8)) {
        return;
      }
    }

    while (SkipHexNumber(2)) { // Groups of 2 digits.
    }
  }

  private void SetInstructionOpcode(InstructionIR instr) {
    instr.OpcodeLocation = current_.Location;
    instr.OpcodeText = TokenData();

    if (NextTokenIs(TokenKind.Dot) && irInfo_.Mode == IRMode.ARM64) {
      // Some disassemblers for ARM64 print branch opcodes
      // like b.eq b.le instead of beq ble and so on.
      SkipToken(); // Skip b
      SkipToken(); // Skip .

      if (IsIdentifier()) {
        instr.OpcodeText = $"{instr.OpcodeText}{TokenData()}".AsMemory();
      }
    }

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
        Debug.Assert(false, "Unsupported IR mode");
        Trace.WriteLine($"Unsupported IR mode {irInfo_.Mode}");
        break;
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
    if (TokenKeyword() == kind) {
      SkipToken();
    }
  }

  private void SkipKeywords() {
    while (IsKeyword()) {
      SkipToken();
    }
  }
}