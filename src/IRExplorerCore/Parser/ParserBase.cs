using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using IRExplorerCore.IR;
using IRExplorerCore.IR.Tags;
using IRExplorerCore.Lexer;

namespace IRExplorerCore {
    public class ParserBase {
        protected readonly Lexer.Lexer lexer_ = new Lexer.Lexer();
        protected Token current_;
        protected Token previous_;
        private IRElementId nextElementId_;

        protected IRElementId NextElementId => nextElementId_;

        protected virtual void Reset() {
            nextElementId_ = IRElementId.FromLong(0);
        }

        protected virtual void Initialize(string text) {
            lexer_.Initialize(text);
        }

        protected virtual void Initialize(ReadOnlyMemory<char> text) {
            lexer_.Initialize(text);
        }

        public bool IsDone() {
            return current_.IsEOF();
        }

        public void SkipCurrentToken() {
            SkipToken();
        }

        protected int LocationDistance(Token startToken) {
            if (current_.Location.Offset != startToken.Location.Offset) {
                return previous_.Location.Offset -
                       startToken.Location.Offset +
                       previous_.Length;
            }

            return startToken.Length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected bool TokenIntNumber(out int value) {
            return int.TryParse(TokenStringData(), NumberStyles.Integer,
                                NumberFormatInfo.InvariantInfo, out value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected bool TokenLongIntNumber(out long value) {
            return long.TryParse(TokenStringData(), NumberStyles.Integer,
                                 NumberFormatInfo.InvariantInfo, out value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected bool TokenFloatNumber(out double value) {
            return double.TryParse(TokenStringData(), out value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected string TokenString() {
            return current_.Data.ToString();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected ReadOnlySpan<char> TokenStringData() {
            return current_.Data.Span;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected ReadOnlyMemory<char> TokenData() {
            return current_.Data;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected void SkipToken() {
            previous_ = current_;
            current_ = lexer_.NextToken();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected bool ExpectAndSkipToken(TokenKind kind) {
            if (current_.Kind == kind) {
                SkipToken();
                return true;
            }

            return false;
        }

        protected bool SkipOptionalToken(TokenKind kind) {
            if (current_.Kind == kind) {
                SkipToken();
                return true;
            }

            return false;
        }

        protected bool SkipToToken(TokenKind kind) {
            while (!IsLineEnd()) {
                if (TokenIs(kind)) {
                    return true;
                }

                SkipToken();
            }

            return false;
        }

        protected bool SkipToAnyToken(params TokenKind[] tokens) {
            while (!IsLineEnd()) {
                if (IsAnyToken(tokens)) {
                    return true;
                }

                SkipToken();
            }

            return false;
        }

        protected bool SkipAfterToken(TokenKind kind) {
            while (!IsLineEnd()) {
                if (TokenIs(kind)) {
                    SkipToken();
                    return true;
                }

                SkipToken();
            }

            return false;
        }

        protected void SkipToLineEnd() {
            while (!IsLineEnd()) {
                SkipToken();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected void SkipToLineStart() {
            SkipToLineEnd();
            SkipToken();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected bool IsEOF() {
            return current_.IsEOF();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected bool IsLineEnd() {
            return current_.IsLineEnd() || current_.IsEOF();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected bool IsDot() {
            return current_.Kind == TokenKind.Dot;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected bool IsComma() {
            return current_.Kind == TokenKind.Comma;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected bool IsLess() {
            return current_.Kind == TokenKind.Less;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected bool IsEqual() {
            return current_.Kind == TokenKind.Equal;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected bool IsStar() {
            return current_.Kind == TokenKind.Star;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected bool IsHash() {
            return current_.Kind == TokenKind.Hash;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected bool IsIdentifier() {
            return current_.Kind == TokenKind.Identifier;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected bool IsNumber() {
            return current_.Kind == TokenKind.Number;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected bool TokenIs(TokenKind kind) {
            return current_.Kind == kind;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected bool IsAnyToken(params TokenKind[] tokens) {
            return Array.IndexOf(tokens, current_.Kind) != -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected bool NextTokenIs(TokenKind kind) {
            return lexer_.PeekToken().Kind == kind;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected bool NextAfterTokenIs(TokenKind kind) {
            return lexer_.PeekToken(2).Kind == kind;
        }

        protected void SetTextRange(IRElement element, Token startToken, int adjustment = 0) {
            int distance = Math.Max(0, LocationDistance(startToken) - adjustment);
            element.SetTextRange(startToken.Location, distance);
        }

        protected void SetTextRange(IRElement element, Token startToken, Token endToken) {
            int distance = Math.Max(0, endToken.Location.Offset - 
                                       startToken.Location.Offset + endToken.Length);
            element.SetTextRange(startToken.Location, distance);
        }

        protected void SetTextRange(IRElement element) {
            element.SetTextRange(current_.Location, current_.Length);
        }

    }
}
