// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using ProfileExplorerCore2.IR;
using ProfileExplorerCore2.IR.Tags;
using ProfileExplorerCore2.Lexer;

namespace ProfileExplorerCore2.Parser;

public class ParserBase {
  protected readonly Lexer.Lexer lexer_ = new();
  private readonly Dictionary<int, BlockIR> blockMap_ = new();
  private readonly IRParsingErrorHandler errorHandler_;
  private readonly RegisterTable registerTable_;
  protected ICompilerIRInfo irInfo_;
  protected Token current_;
  protected Token previous_;
  protected IRTextSection section_;
  private IRElementId nextElementId_;
  private AssemblyMetadataTag metadataTag_; // Lazy-init.

  protected ParserBase(ICompilerIRInfo irInfo, IRParsingErrorHandler errorHandler,
                       RegisterTable registerTable, IRTextSection section) {
    irInfo_ = irInfo;
    errorHandler_ = errorHandler;
    registerTable_ = registerTable;
    section_ = section;
  }

  protected RegisterTable RegisterTable => registerTable_;
  protected AssemblyMetadataTag MetadataTag => metadataTag_ ??= new AssemblyMetadataTag();
  protected IRElementId NextElementId => nextElementId_;

  public bool IsDone() {
    return current_.IsEOF();
  }

  public void SkipCurrentToken() {
    SkipToken();
  }

  protected virtual void Reset() {
    nextElementId_ = IRElementId.FromLong(0);
    blockMap_.Clear();
    metadataTag_ = null;
  }

  protected virtual void Initialize(string text) {
    lexer_.Initialize(text);
  }

  protected virtual void Initialize(ReadOnlyMemory<char> text) {
    lexer_.Initialize(text);
  }

  protected void AddMetadata(FunctionIR function) {
    if (metadataTag_ != null) {
      function.AddTag(metadataTag_);
    }
  }

  protected void ReportError(TokenKind expectedToken, string message = "") {
    errorHandler_?.HandleError(current_.Location, expectedToken, current_, message);
  }

  protected void ReportErrorAndSkipLine(TokenKind expectedToken, string message) {
    ReportError(expectedToken, message);
    SkipToLineStart();
  }

  protected BlockIR GetOrCreateBlock(int blockNumber, FunctionIR function) {
    if (blockMap_.TryGetValue(blockNumber, out var block)) {
      return block;
    }

    var blockId = NextElementId.NewBlock(blockNumber);
    var newBlock = new BlockIR(blockId, blockNumber, function);
    blockMap_[blockNumber] = newBlock;
    return newBlock;
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

  protected long? ParseHexAddress() {
    if (TokenLongHexNumber(out long value)) {
      SkipToken();
      return value;
    }

    return null;
  }

  protected bool SkipHexNumber(int requiredLength = 0) {
    if (!IsNumber() && !IsIdentifier()) {
      return false;
    }

    if (IsHexNumber(TokenData().Span)) {
      // Check if the number has the required number of digits.
      if (requiredLength != 0 &&
          TokenData().Length != requiredLength) {
        return false;
      }

      SkipToken();
      return true;
    }

    return false;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  protected bool TokenLongHexNumber(out long value) {
    if (!IsNumber() && !IsIdentifier()) {
      value = 0;
      return false;
    }

    // Try to parse again as a HEX int.
    // Since a parsing failure is very expensive, first check if the token
    // could be a hex value and reject it early if it cannot be.
    try {
      var data = TokenData();

      if (!IsHexNumber(data.Span)) {
        value = 0;
        return false;
      }

      value = Convert.ToInt64(TokenString(), 16);
      return true;
    }
    catch (Exception) {
      value = 0;
      return false;
    }
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  protected bool TokenFloatNumber(out double value) {
    return double.TryParse(TokenStringData(), NumberStyles.Any, CultureInfo.InvariantCulture, out value);
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
  protected bool ExpectAndSkipToken(params TokenKind[] kind) {
    if (kind.Contains(current_.Kind)) {
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
  protected bool IsColon() {
    return current_.Kind == TokenKind.Colon;
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

  protected void SetTextRange(IRElement element, Token startToken, Token endToken, int adjustment = 0) {
    int distance = Math.Max(0, endToken.Location.Offset -
                            startToken.Location.Offset + endToken.Length - adjustment);
    element.SetTextRange(startToken.Location, distance);
  }

  protected void SetTextRange(IRElement element) {
    element.SetTextRange(current_.Location, current_.Length);
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private bool IsHexLetter(char c) {
    return c >= '0' && c <= '9' ||
           c >= 'a' && c <= 'f' ||
           c >= 'A' && c <= 'F' ||
           c == 'x' || c == 'X';
  }

  private bool IsHexNumber(ReadOnlySpan<char> span) {
    for (int i = 0; i < span.Length; i++) {
      if (!IsHexLetter(span[i])) {
        return false;
      }
    }

    return true;
  }
}