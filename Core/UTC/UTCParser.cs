// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using Core.IR;
using Core.Lexer;

namespace Core.UTC {
    enum Keyword {
        None,
        Entry,
        Exit,
        Block,
        In,
        Out,
        Pragma,
        PointsAtSet,
        Int8,
        Int16,
        Int32,
        Int64,
        UInt8,
        UInt16,
        UInt32,
        UInt64,
        Float,
        Double,
        Infinity,
        Void,
        CC,
        EHRoot,
        EHTry,
        EHTryCatch,
        EHFinally,
        EHFilter,
        EHDestructor,
        EHExcept
    }

    // keyword -> type map
    // string -> keyword

    public sealed class UTCSectionParser : IRSectionParser {
        private UTCParsingErrorHandler errorHandler_;
        private UTCParser parser_;

        public UTCSectionParser(UTCParsingErrorHandler errorHandler = null) {
            if (errorHandler != null) {
                errorHandler_ = errorHandler;
                errorHandler_.Parser = this;
            }
        }

        public FunctionIR ParseSection(string sectionText) {
            parser_ = new UTCParser(sectionText, errorHandler_);
            return parser_.Parse();
        }

        public void SkipCurrentToken() {
            throw new NotImplementedException();
        }

        public void SkipToFunctionEnd() {
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
    }

    public sealed class UTCParsingErrorHandler : IRParsingErrorHandler {
        public IRSectionParser Parser { get; set; }
        public bool HadParsingErrors { get; set; }
        public List<IRParsingError> ParsingErrors { get; set; }
        public bool ThrowOnError { get; set; }

        public UTCParsingErrorHandler() {
            ParsingErrors = new List<IRParsingError>();
        }

        public bool HandleError(TextLocation location,
                                TokenKind expectedToken, Token actualToken,
                                string message = "") {
            var builder = new StringBuilder();

            if (!string.IsNullOrEmpty(message)) {
                builder.AppendLine(message);
            }

            builder.AppendLine($"Location: {location}");
            builder.AppendLine($"Expected token: {expectedToken}");
            builder.Append($"Actual token: {actualToken}");

            if (ThrowOnError) {
                throw new InvalidOperationException($"UTC IR parsing error:\n{builder}");
            }

            ParsingErrors.Add(new IRParsingError(location, builder.ToString()));
            HadParsingErrors = true;
            return true; // Always continue parsing.
        }
    }

    public sealed class UTCParser {
        private static Dictionary<string, Keyword> keywordMap_ =
            new Dictionary<string, Keyword> {
            { "ENTRY", Keyword.Entry },
            { "EXIT", Keyword.Exit },
            { "BLOCK", Keyword.Block },
            { "PRAGMA", Keyword.Pragma},
            { "PAS", Keyword.PointsAtSet },
            { "In", Keyword.In },
            { "Out", Keyword.Out },
            { "i8", Keyword.Int8 },
            { "i16", Keyword.Int16 },
            { "i32", Keyword.Int32 },
            { "i64", Keyword.Int64 },
            { "u8", Keyword.UInt8 },
            { "u16", Keyword.UInt16 },
            { "u32", Keyword.UInt32 },
            { "u64", Keyword.UInt64 },
            { "f32", Keyword.Float },
            { "f64", Keyword.Double },
            { "vd", Keyword.Void },
            { "cc", Keyword.CC },
            { "cc_sozp", Keyword.CC },
            { "cc_soz", Keyword.CC },
            { "cc_so", Keyword.CC },
            { "cc_of", Keyword.CC },
            { "cc_szp", Keyword.CC },
            { "cc_sf", Keyword.CC },
            { "cc_pf", Keyword.CC },
            { "cc_zc", Keyword.CC },
            { "cc_zf", Keyword.CC },
            { "cc_cf", Keyword.CC },
            { "INF", Keyword.Infinity},
            { "r", Keyword.EHRoot },
            { "d", Keyword.EHDestructor },
            { "T", Keyword.EHTry },
            { "TC", Keyword.EHTryCatch},
            { "TF", Keyword.EHFinally},
            { "Tf", Keyword.EHFilter},
            { "TE", Keyword.EHExcept}
        };


        private static readonly Dictionary<Keyword, TypeIR> keywordTypeMap_ =
            new Dictionary<Keyword, TypeIR> {
                { Keyword.Int8, TypeIR.GetInt8() },
                { Keyword.Int16, TypeIR.GetInt16() },
                { Keyword.Int32, TypeIR.GetInt32() },
                { Keyword.Int64, TypeIR.GetInt64() },
                { Keyword.UInt8,  TypeIR.GetUInt8() },
                { Keyword.UInt16, TypeIR.GetUInt16() },
                { Keyword.UInt32, TypeIR.GetUInt32() },
                { Keyword.UInt64, TypeIR.GetUInt64() },
                { Keyword.Float, TypeIR.GetFloat() },
                { Keyword.Double, TypeIR.GetDouble() },
                { Keyword.Void, TypeIR.GetVoid() },
                { Keyword.CC, TypeIR.GetBool() }
        };

        private UTCParsingErrorHandler errorHandler_;
        private Dictionary<int, BlockIR> blockMap_;
        private Dictionary<string, BlockLabelIR> labelMap_;
        private Dictionary<int, SSADefinitionTag> ssaDefinitionMap_;
        private Lexer.Lexer lexer_;
        private Token current_;
        private Token previous_;
        private int nextBlockNumber;
        private IRElementId nextElementId_;

        public UTCParser(string text, UTCParsingErrorHandler errorHandler) {
            nextElementId_ = IRElementId.FromLong(0);
            errorHandler_ = errorHandler;
            lexer_ = new Lexer.Lexer(text);
            SkipToken(); // Get first token.

            blockMap_ = new Dictionary<int, BlockIR>();
            labelMap_ = new Dictionary<string, BlockLabelIR>();
            ssaDefinitionMap_ = new Dictionary<int, SSADefinitionTag>();
        }

        //? TODO: Handle SWITCH
        //? TODO: Branch not parsed if inverted ' in (ORDER')
        //?    also other annotations like {Z}
        //? TODO: Parse special flags for volatile, WT

        public FunctionIR Parse() {
            var function = ParseFunction();

            if (function != null) {
                function.BuildElementIdMap();
            }

            return function;
        }

        public bool IsDone() {
            return current_.IsEOF();
        }


        public void SkipCurrentToken() {
            SkipToken();
        }

        void ReportError(TokenKind expectedToken, string message = "") {
            if (errorHandler_ != null) {
                errorHandler_.HandleError(current_.Location, expectedToken, current_, message);
            }
        }

        int LocationDistance(Token startToken) {
            if (current_.Location.Offset != startToken.Location.Offset) {
                return (previous_.Location.Offset -
                        startToken.Location.Offset) + previous_.Length;
            }

            return startToken.Length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool TokenIntNumber(out int value) {
            return int.TryParse(TokenStringData(), out value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool TokenLongIntNumber(out long value) {
            return long.TryParse(TokenStringData(), out value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool TokenFloatNumber(out double value) {
            return double.TryParse(TokenStringData(), out value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        string TokenString() {
            return current_.Data.ToString();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        ReadOnlySpan<char> TokenStringData() {
            return current_.Data.Span;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        ReadOnlyMemory<char> TokenData() {
            return current_.Data;
        }

        Keyword TokenKeyword() {
            if (current_.IsIdentifier()) {
                if (keywordMap_.TryGetValue(TokenString(), out Keyword keyword)) {
                    return keyword;
                }
            }

            return Keyword.None;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool IsKeyword() {
            return TokenKeyword() != Keyword.None;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool IsOpcode() {
            return IsIdentifier() &&
                   UTCOpcodes.IsOpcode(TokenString());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool IsKeyword(Keyword kind) {
            return TokenKeyword() == kind;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void SkipToken() {
            previous_ = current_;
            current_ = lexer_.NextToken();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool ExpectAndSkipToken(TokenKind kind) {
            if (current_.Kind == kind) {
                SkipToken();
                return true;
            }

            ReportError(kind, "Failed ExpectAndSkipToken");
            return false;
        }

        bool SkipOptionalToken(TokenKind kind) {
            if (current_.Kind == kind) {
                SkipToken();
                return true;
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool ExpectAndSkipKeyword(Keyword keyword) {
            if (TokenKeyword() == keyword) {
                SkipToken();
                return true;
            }

            ReportError(TokenKind.Identifier, "Failed ExpectAndSkipKeyword");
            return false;
        }

        bool SkipToAnyKeyword(params Keyword[] keywords) {
            while (!IsLineEnd()) {
                var current = TokenKeyword();

                if (current != Keyword.None &&
                    Array.IndexOf(keywords, current) != -1) {
                    return true;
                }

                SkipToken();
            }

            return false;
        }

        bool SkipToToken(TokenKind kind) {
            while (!IsLineEnd()) {
                if (TokenIs(kind)) {
                    return true;
                }

                SkipToken();
            }

            return false;
        }

        bool SkipToAnyToken(params TokenKind[] tokens) {
            while (!IsLineEnd()) {
                if (IsAnyToken(tokens)) {
                    return true;
                }

                SkipToken();
            }

            return false;
        }

        bool SkipAfterToken(TokenKind kind) {
            while (!IsLineEnd()) {
                if (TokenIs(kind)) {
                    SkipToken();
                    return true;
                }

                SkipToken();
            }

            return false;
        }

        void SkipToLineEnd() {
            while (!IsLineEnd()) {
                SkipToken();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void SkipToLineStart() {
            SkipToLineEnd();
            SkipToken();
        }

        void SkipToNextBlock() {
            while (!IsEOF()) {
                if (IsKeyword(Keyword.Block) ||
                    IsKeyword(Keyword.Exit) ||
                    (TokenIs(TokenKind.Equal) && //? TODO: Workaround for === Block
                     NextTokenIs(TokenKind.Equal))) {
                    break;
                }

                SkipToken();
            }
        }

        void SkipToFunctionEnd() {
            while (!IsEOF()) {
                if (IsKeyword(Keyword.Exit)) {
                    break;
                }

                SkipToken();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool IsEOF() {
            return current_.IsEOF();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool IsLineEnd() {
            return current_.IsLineEnd() || current_.IsEOF();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool IsDot() {
            return current_.Kind == TokenKind.Dot;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool IsComma() {
            return current_.Kind == TokenKind.Comma;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool IsLess() {
            return current_.Kind == TokenKind.Less;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool IsEqual() {
            return current_.Kind == TokenKind.Equal;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool IsStar() {
            return current_.Kind == TokenKind.Star;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool IsHash() {
            return current_.Kind == TokenKind.Hash;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool IsIdentifier() {
            return current_.Kind == TokenKind.Identifier;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool IsNumber() {
            return current_.Kind == TokenKind.Number;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool TokenIs(TokenKind kind) {
            return current_.Kind == kind;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool IsAnyToken(params TokenKind[] tokens) {
            return Array.IndexOf(tokens, current_.Kind) != -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool NextTokenIs(TokenKind kind) {
            return lexer_.PeekToken().Kind == kind;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool NextAfterTokenIs(TokenKind kind) {
            return lexer_.PeekToken2().Kind == kind;
        }

        bool IsTemporary(ReadOnlySpan<char> name) {
            int prefixLength = 0;

            if (name.StartsWith("tv".AsSpan()) ||
                name.StartsWith("hv".AsSpan())) {
                prefixLength = 2;
            }
            else if (name.StartsWith("t".AsSpan())) {
                prefixLength = 1;
            }

            return int.TryParse(name.Slice(prefixLength), out _);
        }

        FunctionIR ParseFunction() {
            var startToken = current_;
            FunctionIR function = new FunctionIR();

            // Sometimes there is whitespace before the first block, ignore.
            SkipToNextBlock();

            while (!IsEOF()) {
                if (IsKeyword(Keyword.Exit)) {
                    SkipToLineStart();
                    ExpectAndSkipKeyword(Keyword.Block);
                    break; // End of function reached.
                }

                BlockIR block = ParseBlock(function);

                if (block == null) {
                    // Failed to parse block, continue with next one.
                    SkipToNextBlock();
                    continue;
                }

                function.Blocks.Add(block);

                if (IsKeyword(Keyword.Block)) {
                    // Continue to next block.
                }
                else {
                    //? TODO: Any error handling?
                    SkipToNextBlock();
                }
            }

            SetTextRange(function, startToken);
            return function;
        }

        BlockIR GetOrCreateBlock(int blockNumber, FunctionIR function) {
            if (blockMap_.TryGetValue(blockNumber, out BlockIR block)) {
                return block;
            }

            BlockIR newBlock = new BlockIR(nextElementId_, blockNumber, function);
            blockMap_.Add(blockNumber, newBlock);
            return newBlock;
        }

        BlockLabelIR GetOrCreateLabel(ReadOnlyMemory<char> name, BlockIR parent = null) {
            //? TODO: Label name should not include EH prefix  r$LN758@encode_one:

            // Skip any annotations such as EH before the $ and stop at @.
            var labelName = name.Span;
            int nameStart = Math.Max(0, labelName.IndexOf('$'));
            int nameEnd = labelName.IndexOf('@');

            if (nameStart > 0 || nameEnd > 0) {
                int length = nameEnd - nameStart;

                if (length > 0) {
                    labelName = labelName.Slice(nameStart, length);
                }
            }

            var nameString = labelName.ToString();

            if (labelMap_.TryGetValue(nameString, out BlockLabelIR label)) {
                if (parent != null) {
                    label.Parent = parent;
                    SetTextRange(label);
                }

                return label;
            }

            label = new BlockLabelIR(nextElementId_, name, parent);

            if (parent != null) {
                SetTextRange(label);
            }

            labelMap_.Add(nameString, label);
            return label;
        }

        SSADefinitionTag GetOrCreateSSADefinition(int id) {
            if (ssaDefinitionMap_.TryGetValue(id, out SSADefinitionTag value)) {
                return value;
            }

            value = new SSADefinitionTag(id);
            ssaDefinitionMap_.Add(id, value);
            return value;
        }

        BlockIR ParseBlock(FunctionIR function) {
            var startToken = current_;
            bool cfgAvailable = true;

            // For vectorizer output, one or more blocks can be found
            // between line markers, just skip over and parse the code inside.
            // >>> Begin landing pad
            // <<< End landing pad
            if (TokenIs(TokenKind.Greater) &&
                NextTokenIs(TokenKind.Greater) &&
                NextAfterTokenIs(TokenKind.Greater)) {
                SkipToLineStart();
            }

            // For vectorizer output, one or more blocks can be found
            // between line markers, just skip over and parse the code inside.
            // >>> Begin landing pad
            // <<< End landing pad
            if (TokenIs(TokenKind.Greater) &&
                NextTokenIs(TokenKind.Greater) &&
                NextAfterTokenIs(TokenKind.Greater)) {
                SkipToLineStart();
            }

            if (!ExpectAndSkipKeyword(Keyword.Block)) {
                //? TODO: Easiest way is to fix UTC bock printing in fg.c:prBlock
                //? to be BLOCK instead of === Block
                if (TokenIs(TokenKind.Equal)) {
                    SkipToFunctionEnd();
                }

                return null;
            }

            // Start a new block.
            if (!TokenIntNumber(out int blockNumber)) {
                // Unknown block ID, flow graph not built.
                // Generate a new block ID so parsing continues.
                blockNumber = nextBlockNumber++;
                cfgAvailable = false;
            }

            BlockIR block = GetOrCreateBlock(blockNumber, function);
            block.Id = nextElementId_.NewBlock(blockNumber);

            if (cfgAvailable) {
                // Parse the list of predecessor and successor blocks
                // and link them to the current block.
                while (true) {
                    if (SkipToAnyKeyword(Keyword.In, Keyword.Out)) {
                        bool isPredList = IsKeyword(Keyword.In);
                        SkipToken();

                        if (!ParseBlockList(block, isPredList, function)) {
                            return null;
                        }
                    }
                    else break;
                }
            }

            SkipToLineStart(); // Skip to first tuple.

            // Collect all tuples in block, which extends
            // until the next BLOCK keyword is found.
            while (!IsKeyword(Keyword.Block) && !IsKeyword(Keyword.Exit) && !IsEOF()) {
                TupleIR tuple = ParseTuple(block);

                if (tuple == null) {
                    continue;
                }

                tuple.BlockIndex = block.Tuples.Count;
                block.Tuples.Add(tuple);
            }

            SetTextRange(block, startToken, Environment.NewLine.Length);

            // Skip over lines starting with <<<
            if (TokenIs(TokenKind.Less) &&
                NextTokenIs(TokenKind.Less) &&
                NextAfterTokenIs(TokenKind.Less)) {
                SkipToLineStart();
            }

            return block;
        }


        bool ParseBlockList(BlockIR block, bool isPredList, FunctionIR function) {
            if (!ExpectAndSkipToken(TokenKind.OpenParen)) {
                return false;
            }

            while (!TokenIs(TokenKind.CloseParen) && !IsLineEnd()) {
                if (IsComma()) {
                    SkipToken();
                }

                if (!TokenIntNumber(out int otherBlockId)) {
                    if (TokenIs(TokenKind.Minus) &&
                        NextTokenIs(TokenKind.Number)) {
                        SkipToken();
                    }
                    else if(TokenIs(TokenKind.Colon)) {
                        // Skip over extra annotations like :I below
                        // In(52:I, 106:I, 82) Out(108)
                        SkipToken();
                        SkipToken();
                        continue;
                    }
                    else {
                        ReportError(TokenKind.Identifier, "Failed ParseBlockList");
                        return false;
                    }
                }

                SkipToken();
                BlockIR otherBlock = GetOrCreateBlock(otherBlockId, function);

                if (isPredList) {
                    block.Predecessors.Add(otherBlock);
                }
                else {
                    block.Successors.Add(otherBlock);
                }
            }

            return true;
        }

        public bool SkipOptionalPrefix() {
            switch (TokenKeyword()) {
                case Keyword.EHRoot:
                case Keyword.EHDestructor:
                case Keyword.EHTry:
                case Keyword.EHTryCatch:
                case Keyword.EHFinally:
                case Keyword.EHFilter:
                case Keyword.EHExcept: {
                        if (!NextTokenIs(TokenKind.Dot)) {
                            SkipToken();
                            return true;
                        }
                        break;
                    }
            }

            return false;
        }

        public TupleIR ParseTuple(BlockIR parent) {
            TupleIR tuple = null;
            bool stop = false;
            bool sawEHAttribute = false;
            var startToken = current_;

            while (!stop && !IsEOF()) {
                switch (TokenKeyword()) {
                    case Keyword.EHRoot:
                    case Keyword.EHDestructor:
                    case Keyword.EHTry:
                    case Keyword.EHTryCatch:
                    case Keyword.EHFinally:
                    case Keyword.EHFilter:
                    case Keyword.EHExcept: {
                            if (!sawEHAttribute && !NextTokenIs(TokenKind.Dot)) {
                                SkipToken(); //? TODO: save EH annotation
                                sawEHAttribute = true;
                            }
                            else {
                                // This is likely a short variable name that
                                // happens to have the same name as the EH keywords.
                                tuple = ParseCodeTuple(parent);
                                stop = true;
                            }
                            break;
                        }
                    case Keyword.Pragma: {
                            SkipToLineEnd();
                            tuple = new TupleIR(nextElementId_, TupleKind.Metadata, parent);
                            stop = true;
                            break;
                        }
                    case Keyword.Entry: {
                            tuple = ParseEntry(parent);
                            stop = true;
                            break;
                        }
                    default: {
                            tuple = ParseCodeTuple(parent);
                            stop = true;
                            break;
                        }
                }
            }

            // Skip over size annotation.
            if (TokenIs(TokenKind.OpenParen)) {
                SkipAfterToken(TokenKind.CloseParen);
            }

            // Source line annotation can appear as the last item on the line.
            if (tuple != null && TokenIs(TokenKind.Hash)) {
                var sourceLocationTag = ParseSourceLocation();

                if (sourceLocationTag != null) {
                    tuple.AddTag(sourceLocationTag);
                }
            }

            if (!IsKeyword(Keyword.Block)) {
                SkipToLineStart(); // Skip over comments.
            }

            if (tuple == null) {
                // Failed to parse line as a tuple, ignore it,
                // it's usually some verbose output like interf. info.
                return null;
            }

            SetTextRange(tuple, startToken, Environment.NewLine.Length);
            return tuple;
        }

        private SourceLocationTag ParseSourceLocation() {
            // There can be a list of source line numbers in case of inlining,
            // following this pattern: #123| #456
            int lastLineNumber = -1;

            while (TokenIs(TokenKind.Hash)) {
                SkipToken();

                if (IsNumber()) {
                    if (!TokenIntNumber(out lastLineNumber)) {
                        return null;
                    }

                    SkipToken();
                }

                if (!TokenIs(TokenKind.Or)) {
                    break;
                }
            }

            return new SourceLocationTag(lastLineNumber, 0);
        }

        private TupleIR ParseCodeTuple(BlockIR parent) {
            // identifier: usually represnts a label,
            // unless it is a variable with an equiv-class ID like var:2
            if (IsIdentifier() && NextTokenIs(TokenKind.Colon) &&
                !NextAfterTokenIs(TokenKind.Number)) {
                // label: definition
                var startToken = current_;
                var label = GetOrCreateLabel(TokenData(), parent);

                SkipToken();
                SkipToken();
                SetTextRange(label, startToken);

                SkipToLineEnd(); // Skip other attributes.
                parent.Label = label;
                return label;
            }
            else if (TokenIs(TokenKind.Greater) && 
                     NextTokenIs(TokenKind.Greater) &&
                     NextAfterTokenIs(TokenKind.Greater)) {
                // This should be a rare case of >>> Begin NOEVALLIST or vectorizer output.
                SkipToLineStart();
                return null;
            }
            else if (TokenIs(TokenKind.Less) &&
                     NextTokenIs(TokenKind.Less) &&
                     NextAfterTokenIs(TokenKind.Less)) {
                // <<< marking the end of the >>> sequence.
                SkipToLineStart();
                return null;
            }
            else {
                //? TODO: Handle other tuple types
                return ParseInstruction(parent);
            }
        }

        TupleIR ParseEntry(BlockIR entryBlock) {
            var startToken = current_;
            SkipToken(); // ENTRY

            // There can be various attributes before the name, ignore.
            SkipToFirstOperand();

            // Set function name.
            if (!IsIdentifier()) {
                ReportError(TokenKind.Identifier, "Failed ParseEntry function name");
                return null;
            }

            var entry = new TupleIR(nextElementId_, TupleKind.Metadata, entryBlock);
            var function = entryBlock.Parent;
            function.Name = TokenString();
            SkipToken();

            // Collect function parameters.
            if (TokenIs(TokenKind.OpenParen)) {
                SkipToken();

                if (!TokenIs(TokenKind.CloseParen) &&
                    !ParseOperandList(null, false, function.Parameters)) {
                    ReportError(TokenKind.Identifier, "Failed ParseEntry operand list");
                    return null;
                }

                // Assign the parent block, since it's not done
                // by default without an associated instruction.
                foreach (var param in function.Parameters) {
                    param.Parent = entry;
                    param.Role = OperandRole.Parameter;
                }

                ExpectAndSkipToken(TokenKind.CloseParen);
            }

            SetTextRange(entry, startToken);
            return entry;
        }

        InstructionIR ParseInstruction(BlockIR parent) {
            // instr = [opList =] OPCODE [.type] opList
            InstructionIR instr = new InstructionIR(nextElementId_, InstructionKind.Other, parent);

            // Some instrs. don't have a dest. list and start directly with the opcode.
            if (!IsOpcode() &&
                !ParseOperandList(instr, true, instr.Destinations)) {
                ReportError(TokenKind.Identifier, "Failed ParseInstruction destination list");
                return null;
            }

            if (IsEqual()) {
                SkipToken();
            }

            if (!ParseOpcode(instr)) {
                return null;
            }

            //? TODO: Parse switch properly
            if (((UTCOpcode)instr.Opcode) == UTCOpcode.OPSWITCH) {
                SkipToNextBlock();
                return instr;
            }

            // For exception instrs. sources are not parsed.
            if (instr.Kind != InstructionKind.Exception) {
                bool isBlockLabelRef = instr.Kind == InstructionKind.Goto ||
                                       instr.Kind == InstructionKind.Branch;

                // Parse source list.
                if (!ParseOperandList(instr, false, instr.Sources, isBlockLabelRef)) {
                    ReportError(TokenKind.Identifier, "Failed ParseInstruction source list");
                    return null;
                }
            }

            return instr;
        }

        bool ParseOpcode(InstructionIR instr) {
            if (!IsIdentifier()) {
                return false;
            }

            instr.OpcodeText = TokenData();
            instr.OpcodeLocation = current_.Location;
            SkipToken();

            // Opcode can be followed by type and other attributes, ignore.
            //? TODO: For branch/question, extract cond code
            SkipToFirstOperand();
            SetOpcodeAndKind(instr);
            return true;
        }

        void SetOpcodeAndKind(InstructionIR instr) {
            if (UTCOpcodes.GetOpcodeInfo(instr.OpcodeText, out UTCOpcodeInfo info)) {
                instr.Kind = info.Kind;
                instr.Opcode = info.Opcode;
            }
            else {
                instr.Kind = InstructionKind.Other;
                instr.Opcode = 0;
            }
        }

        void SetTextRange(IRElement element, Token startToken, int adjustment = 0) {
            int distance = Math.Max(0, LocationDistance(startToken) - adjustment);
            element.SetTextRange(startToken.Location, distance);
        }

        void SetTextRange(IRElement element) {
            element.SetTextRange(current_.Location, current_.Length);
        }

        bool ParseOperandList(InstructionIR instr, bool isDestList,
                              List<OperandIR> list, bool isBlockLabelRef = false) {
            // opList = opList, op | op
            // destOpList = opList =
            while (!IsEOF()) {
                if (isDestList && IsEqual()) {
                    break; // Handles the case of no dest. operands.
                }
                else if (IsAnyToken(TokenKind.OpenParen, TokenKind.Hash,
                                    TokenKind.Tilde, TokenKind.LineEnd)) {
                    break; // Handles the case of no source operands.
                }

                OperandIR operand = ParseOperand(instr, false, isBlockLabelRef);

                if (operand == null) {
                    return false;
                }

                operand.Role = isDestList ? OperandRole.Destination : OperandRole.Source;
                list.Add(operand);

                if (IsComma()) {
                    SkipToken(); // More operands after ,
                }
                else break;
            }

            return true;
        }

        public OperandIR ParseOperand(TupleIR parent, bool isIndirBaseOp = false,
                                      bool isBlockLabelRef = false, bool disableSkipToNext = false) {
            OperandIR operand = null;

            // operand = varOp | intOp | floatOp | | addressOp | indirOp | labelOp | pasOp
            if (IsIdentifier() || TokenIs(TokenKind.Less)) {
                // Check for PAS(n) first and skip over it.
                if (IsKeyword(Keyword.PointsAtSet) &&
                    NextTokenIs(TokenKind.OpenParen)) {

                    if (!SkipAfterToken(TokenKind.CloseParen)) {
                        ReportError(TokenKind.CloseParen, "Failed ParseOperand skip PAS");
                        return null;
                    }

                    operand = new OperandIR(nextElementId_, OperandKind.Other, TypeIR.GetUnknown(), parent);
                }
                else {
                    // Variable/temporary.
                    operand = ParseVariableOperand(parent, isIndirBaseOp);
                }
            }
            else if (IsNumber() || TokenIs(TokenKind.Minus)) { // int/float const
                operand = ParseNumber(parent);
            }
            else if (TokenIs(TokenKind.And)) { // &address
                SkipToken();

                if (TokenIs(TokenKind.OpenSquare)) {
                    // EA operands are in the form &[INDIR].
                    operand = ParseIndirection(parent);
                }
                else {
                    operand = ParseVariableOperand(parent, isIndirBaseOp);
                }

                if (operand == null) {
                    return null;
                }

                if (isBlockLabelRef) {
                    // Link to the referenced label.
                    operand.Value = GetOrCreateLabel(operand.NameValue);
                    operand.Kind = OperandKind.LabelAddress;
                }
                else {
                    operand.Kind = OperandKind.Address;
                }
            }
            else if (TokenIs(TokenKind.OpenSquare)) { // [indir]
                if (isIndirBaseOp) {
                    ReportError(TokenKind.OpenSquare, "Failed ParseOperand nested INDIR");
                    return null; // Nested [indir] not allowed.
                }

                operand = ParseIndirection(parent);
            }

            // Skip over any other unhandled attributes.
            if (!isIndirBaseOp && !disableSkipToNext) {
                SkipToNextOperand();
            }

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
                if (ParseOperand(parent, true) == null) {
                    break;
                }
            }

            if (!ExpectAndSkipToken(TokenKind.CloseSquare)) {
                ReportError(TokenKind.CloseSquare, "Failed ParseIndirection");
                return null;
            }

            SkipOperandFlags();

            TypeIR type = TryParseType();
            var operand = new OperandIR(nextElementId_, OperandKind.Indirection, type, parent);
            operand.Value = baseOp;
            SetTextRange(operand, startToken);

            if (TokenIs(TokenKind.OpenParen)) {
                SkipAfterToken(TokenKind.CloseParen); // Ignore lexical hash var.
            }

            return operand;
        }

        OperandIR ParseNumber(TupleIR parent) {
            Token startToken = current_;
            OperandKind opKind = OperandKind.Other;
            object opValue = null;
            bool isNegated = false;

            if (TokenIs(TokenKind.Minus)) {
                SkipToken();
                isNegated = true;
            }

            if (TokenLongIntNumber(out long intValue)) {
                // intConst = DECIMAL [(0xHEX)] [.type]
                SkipToken();
                opKind = OperandKind.IntConstant;

                unchecked {
                    opValue = isNegated ? -intValue : intValue;
                }

                // Skip over HEX value.
                if (TokenIs(TokenKind.OpenParen)) {
                    SkipAfterToken(TokenKind.CloseParen);
                }
            }
            else if (TokenFloatNumber(out double floatValue)) {
                // floatConst = FLOAT [.type]
                SkipToken();
                opKind = OperandKind.FloatConstant;

                unchecked {
                    opValue = isNegated ? -floatValue : floatValue;
                }
            }
            else if (IsKeyword(Keyword.Infinity)) {
                SkipToken();
                opKind = OperandKind.FloatConstant;
                opValue = isNegated ? double.NegativeInfinity : double.PositiveInfinity;
            }
            else {
                ReportError(TokenKind.Number, "Failed ParseNumber");
                return null;
            }

            TypeIR type = TryParseType();
            OperandIR operand = new OperandIR(nextElementId_, opKind, type, parent);
            operand.Value = opValue;
            SetTextRange(operand, startToken);
            return operand;
        }

        static readonly TokenKind[] SkipOperandFlagsTokens = new TokenKind[] {
            TokenKind.Xor, TokenKind.Tilde,
            TokenKind.Exclamation, TokenKind.Minus
        };

        void SkipOperandFlags() {
            // Skip various flags for marking ops. as volatile, write-through, etc.
            while (IsAnyToken(SkipOperandFlagsTokens)) {
                SkipToken();
            }
        }

        void SkipTypeAttributes() {
            SkipOperandFlags();

            // Skip various attributes before the type like [dL], [pdS].
            while (TokenIs(TokenKind.OpenSquare)) {
                SkipAfterToken(TokenKind.CloseSquare);
            }

            // Skip over unused {HFA...} type info.
            while (TokenIs(TokenKind.OpenCurly)) {
                SkipAfterToken(TokenKind.CloseCurly);
            }
        }

        static readonly TokenKind[] SkipToTypeTokens = new TokenKind[]
        {
            TokenKind.Dot, TokenKind.Comma,
            TokenKind.OpenParen, TokenKind.CloseParen,
            TokenKind.Less, TokenKind.CloseSquare,
            TokenKind.Equal, TokenKind.Plus,
            TokenKind.Hash, TokenKind.Identifier,
            TokenKind.LineEnd
        };

        void SkipToType() {
            SkipTypeAttributes();
            SkipToAnyToken(SkipToTypeTokens);
        }

        static readonly TokenKind[] SkipToNextOperandTokens = new TokenKind[]
        {
            TokenKind.Comma, TokenKind.Equal,
            TokenKind.OpenParen, TokenKind.CloseParen,
            TokenKind.Hash, TokenKind.LineEnd
        };

        void SkipToNextOperand() {
            SkipToAnyToken(SkipToTypeTokens);
        }

        void SkipToFirstOperand() {
            while (!IsLineEnd()) {
                if (TokenIs(TokenKind.OpenParen)) {
                    SkipAfterToken(TokenKind.CloseParen);
                }
                else if (TokenIs(TokenKind.OpenCurly)) {
                    SkipAfterToken(TokenKind.CloseCurly);
                }
                else if (TokenIs(TokenKind.Less)) {
                    SkipAfterToken(TokenKind.Greater);
                }
                else if (TokenIs(TokenKind.Dot) &&
                         NextTokenIs(TokenKind.Identifier)) {
                    SkipToken();
                    SkipToken();
                }
                else if (TokenIs(TokenKind.Apostrophe)) {
                    SkipToken();
                }
                else break;
            }
        }

        TypeIR TryParseType() {
            SkipToType();

            if (IsDot()) {
                SkipToken();
                return ParseType();
            }

            return TypeIR.GetUnknown();
        }

        OperandIR ParseVariableOperand(TupleIR parent, bool isIndirBaseOp = false) {
            // varOp = name [(hashvar)] [+offset] [:equivNumber] [!~^] [.type] [.alignment] [<*SSA>]
            bool isSpecialName = false;
            int arrowLevel = 0;

            // Handle rare variables named like <Text>$.
            // Multiple < > are also possible, like <<FuncArgs_0>>$.
            while (TokenIs(TokenKind.Less)) {
                SkipToken();
                isSpecialName = true;
                arrowLevel++;
            }

            if (!IsIdentifier()) {
                ReportError(TokenKind.Identifier, "Failed ParseVariableOperand");
                return null;
            }

            // Save variable name.
            var opName = TokenData();
            var opKind = OperandKind.Variable;

            if (IsTemporary(TokenStringData())) {
                opKind = OperandKind.Temporary;
            }

            Token startToken = current_;
            SkipToken();

            // Handle lambda names that appear as long identifiers with < > like
            // PREFIX<lambda_7999cf272a0d536f028fd80dd2243ff5>
            // If after < a * or number is found, then it's a SSA number.
            if (TokenIs(TokenKind.Less) &&
                !(NextTokenIs(TokenKind.Star) || NextTokenIs(TokenKind.Number))) {
                SkipToken();
                isSpecialName = true;
                arrowLevel++;
            }

            if (isSpecialName) {
                // Skip until > is found, then the rest of the name.
                while (!IsLineEnd()) {
                    if (TokenIs(TokenKind.Greater)) {
                        SkipToken();
                        arrowLevel--;

                        if (arrowLevel == 0) {
                            break;
                        }
                    }

                    SkipToken();
                }

                SkipOptionalToken(TokenKind.Identifier);
            }

            OperandIR operand = new OperandIR(nextElementId_, opKind, TypeIR.GetUnknown(), parent);
            operand.Value = opName;
            SetTextRange(operand, startToken);

            // Parse type and other attributes.
            // Skip to the optional type, or to the end of the operand.
            bool foundType = false;

            while (!IsLineEnd() && !IsHash()) {
                if (isIndirBaseOp && TokenIs(TokenKind.CloseSquare)) {
                    break; // Found end of [indir].
                }

                SkipToType();

                if (IsDot()) { // .type
                    SkipToken();

                    if (!foundType) {
                        operand.Type = ParseType();
                        SetTextRange(operand, startToken);
                        foundType = true;
                    }
                    else {
                        // Alignment appears as aN, like a64.
                        if (IsAlignmentInfo()) {
                            SkipToken();
                        }
                    }
                }
                else if (IsLess()) { // <*SSA>
                    var ssaDefTag = ParseSSADefNumber(parent);

                    if (ssaDefTag != null) {
                        operand.AddTag(ssaDefTag);
                        SetTextRange(operand, startToken);
                    }
                }
                else if (TokenIs(TokenKind.Plus)) {
                    //? TODO: Save symbol offset as tag
                    SkipToken();

                    if (IsNumber()) {
                        SkipToken();
                    }
                }
                else if (TokenIs(TokenKind.OpenParen)) {
                    SkipAfterToken(TokenKind.CloseParen); // Ignore lexical hash var.
                }
                else break;
            }

            return operand;
        }

        private bool IsAlignmentInfo() {
            if (IsIdentifier()) {
                var text = TokenStringData();
                return text.Length > 1 && text.StartsWith("a".AsSpan());
            }

            return false;
        }

        ITag ParseSSADefNumber(TupleIR parent) {
            if (!ExpectAndSkipToken(TokenKind.Less)) {
                ReportError(TokenKind.Less, "Failed ParseSSADefNumber start");
                return null;
            }

            bool isDefinition = false;
            ITag tag = null;

            if (IsStar()) {
                isDefinition = true;
                SkipToken();
            }

            if (IsNumber()) {
                if (!TokenIntNumber(out int defNumber)) {
                    ReportError(TokenKind.Less, "Failed ParseSSADefNumber number");
                    return null;
                }

                SkipToken();

                // Get the associated SSA tag. If this is a definition,
                // associate the instruction with it.
                var ssaDefTag = GetOrCreateSSADefinition(defNumber);

                if (isDefinition) {
                    ssaDefTag.Parent = parent;
                    tag = ssaDefTag;
                }
                else {
                    // Create a use-def link for the source,
                    // and add it as an user of the definition.
                    var ssaUDLinkTag = new SSAUseTag(defNumber, ssaDefTag);
                    ssaUDLinkTag.Parent = parent;
                    ssaDefTag.Users.Add(ssaUDLinkTag);
                    tag = ssaUDLinkTag;
                }
            }
            else {
                // Ignore annotations such as <b>.
                SkipToToken(TokenKind.Greater);
            }

            // In verbose output, there are can be l-value and r-value annotations.
            if (IsIdentifier()) {
                var text = TokenStringData();

                if (text.Length == 1 &&
                    (text.StartsWith("l".AsSpan()) ||
                     text.StartsWith("r".AsSpan()))) {
                    SkipToken();
                }
                else {
                    ReportError(TokenKind.Identifier, "Failed ParseSSADefNumber annotation");
                    return null;
                }
            }

            if (!ExpectAndSkipToken(TokenKind.Greater)) {
                ReportError(TokenKind.Greater, "Failed ParseSSADefNumber end");
                return null;
            }

            return tag;
        }

        TypeIR CreatePrimitiveType(Keyword keyword) {
            if (keywordTypeMap_.TryGetValue(keyword, out TypeIR primitiveTypes)) {
                return primitiveTypes;
            }

            return null;
        }

        TypeIR CreateType(ReadOnlySpan<char> typeName) {
            if (typeName.StartsWith("mb".AsSpan())) {
                if (int.TryParse(typeName.Slice(2), out int typeSize)) {
                    return TypeIR.GetType(TypeKind.Multibyte, typeSize);
                }
            }
            else if (typeName.StartsWith("xmm".AsSpan()) ||
                     typeName.StartsWith("ymm".AsSpan()) ||
                     typeName.StartsWith("zmm".AsSpan())) {
                //? TODO: Extract proper size
                return TypeIR.GetType(TypeKind.Vector, 0);
            }

            return null;
        }

        TypeIR ParseType() {
            if (!IsIdentifier()) {
                return null;
            }

            // Most types are int/float primitives.
            Keyword keyword = TokenKeyword();

            if (keyword != Keyword.None) {
                SkipToken();
                return CreatePrimitiveType(keyword);
            }

            // Handle other types.
            TypeIR type = CreateType(TokenStringData());
            SkipToken();
            SkipTypeAttributes();
            return type;
        }

    }
}
