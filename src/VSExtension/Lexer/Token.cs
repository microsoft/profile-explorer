// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Runtime.CompilerServices;

namespace ProfileExplorer.Core.Lexer;

// Definitions for token types.
// Some tokens (identifier, string, number, etc.) have additional associated data.
public enum TokenKind {
  Identifier, // -> Data
  Number, // -> Data
  String, // -> Data
  Char, // -> Data
  Plus, // +
  Minus, // -
  Star, // *
  Tilde, // ~
  Equal, // =
  Exclamation, // !
  Percent, // %
  And, // &
  Or, // |
  Xor, // ^
  Less, // <
  Greater, // >
  Hash, // #  (used by the preprocessor)
  Div, // /
  Colon, // :
  SemiColon, // ;
  Comma, // ,
  Apostrophe, // '
  Dot, // .
  Question, // ?
  OpenSquare, // [
  CloseSquare, // ]
  OpenParen, // (
  CloseParen, // )
  OpenCurly, // {
  CloseCurly, // }
  LineEnd, // Found \n.
  EOF, // The end of the file was reached.
  Invalid, // The token could not be determined.
  Custom, // Used to mark a custom (optional) token.
  Keyword // All values after this one denote tokens.
}

public struct Token {
  public TokenKind Kind {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    set;
  }
  public TextLocation Location {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    set;
  }
  public int Length {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    set;
  }
  public ReadOnlyMemory<char> Data {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    set;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public Token(TokenKind kind, TextLocation location, int length) : this() {
    Kind = kind;
    Location = location;
    Length = length;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public bool IsValid() {
    return Kind != TokenKind.EOF && Kind != TokenKind.Invalid;
  }

  // Returns true if the token describes and end-of-file situation.
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public bool IsEOF() {
    return Kind == TokenKind.EOF;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public bool IsLineEnd() {
    return Kind == TokenKind.LineEnd;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public bool IsIdentifier() {
    return Kind == TokenKind.Identifier;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public bool IsNumber() {
    return Kind == TokenKind.Number;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public bool IsString() {
    return Kind == TokenKind.String;
  }

  // Returns true if the token represents an operator.
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public bool IsOperator() {
    return (int)Kind >= (int)TokenKind.Plus && (int)Kind <= (int)TokenKind.Div;
  }

  public override string ToString() {
    string text = $"kind: {Kind}; location: {Location}; length: {Length}";

    if (!Data.IsEmpty) {
      text += $"; data: {Data.Span.ToString()}";
    }

    return text;
  }
}