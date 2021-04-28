using System;
using System.Diagnostics;
using IRExplorerCore.IR;
using IRExplorerCore.Lexer;

namespace IRExplorerCore.ASM {
    public sealed class ASMParser : ParserBase {
        private long? initialAddress;

        public ASMParser(IRParsingErrorHandler errorHandler,
                         RegisterTable registerTable,
                         ReadOnlyMemory<char> sectionText)
            : base(errorHandler, registerTable) {
            Reset();
            Debug.WriteLine("About to parse this:");
            Debug.WriteLine(sectionText);
            Initialize(sectionText);
            SkipToken();
        }

        public ASMParser(IRParsingErrorHandler errorHandler,
            RegisterTable registerTable,
            string sectionText)
            : base(errorHandler, registerTable) {
            Reset();
            Debug.WriteLine("About to parse this:");
            Debug.WriteLine(sectionText);
            Initialize(sectionText);
            SkipToken();
        }

        public FunctionIR Parse() {
            var result = new FunctionIR();
            var block = GetOrCreateBlock(blockNumber: 0, result);
            var startElement = Current;
            block.Id = NextElementId.NewBlock(blockId: 0);
            result.Blocks.Add(block);

            while (!IsEOF()) {
                Debug.WriteLine($"{Current.Kind}: {Current.Data}");
                ParseLine(block);
            }
            SetTextRange(block, startElement, Current);
            AddMetadata(result);
            return result;
        }

        private void ParseLine(BlockIR block) {
            var startToken = Current;
            if (Current.Kind != TokenKind.Number) {
                ReportErrorAndSkipLine(TokenKind.Number, "Expected line to start with an address");
                return;
            }
            long? address = ParseHexAddress();
            if (!address.HasValue) {
                ReportErrorAndSkipLine(TokenKind.Number, "Expected line to start with an address");
                return;
            }
            if (!ExpectAndSkipToken(TokenKind.Colon)) {
                ReportErrorAndSkipLine(TokenKind.Colon, "Expected a colon to follow the address");
                return;
            }
            if (!ExpectAndSkipToken(TokenKind.Number, TokenKind.Identifier)) {
                ReportErrorAndSkipLine(TokenKind.Number, "Expected the mem representation of the instruction");
                return;
            }

            initialAddress ??= address.Value;
            var offset = address.Value - initialAddress.Value;
            var instr = new InstructionIR(NextElementId, InstructionKind.Other, block);
            block.Tuples.Add(instr);
            SkipToLineEnd();
            MetadataTag.AddressToElementMap[address.Value] = instr;
            MetadataTag.OffsetToElementMap[offset] = instr;
            MetadataTag.ElementToOffsetMap[instr] = offset;
            SetTextRange(instr, startToken: startToken, Current, adjustment:1);
            SkipToLineStart();
        }
    }
}
