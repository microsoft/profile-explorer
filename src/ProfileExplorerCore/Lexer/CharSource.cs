// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Runtime.CompilerServices;

namespace ProfileExplorerCore.Lexer;

public sealed class CharSource {
  public static readonly char EOF = '\0';
  public static readonly char NewLine = '\n';
  public ReadOnlyMemory<char> TextSpan {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get;
    private set;
  }
  public int Position {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get;
    set;
  }

  public void Initialize(ReadOnlyMemory<char> text) {
    TextSpan = text;
    Position = 0;
  }

  public void Initialize(string text) {
    TextSpan = text.AsMemory();
    Position = 0;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public char NextChar() {
    if (Position >= TextSpan.Length) {
      Position++;
      return EOF;
    }

    return TextSpan.Span[Position++];
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public char PeekChar() {
    return Position >= TextSpan.Length ? EOF :
      TextSpan.Span[Position];
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public char Skip(int count = 1) {
    Position += count;
    return PeekChar();
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public char GoBack(int count = 1) {
    Position -= count;
    return PeekChar();
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public ReadOnlyMemory<char> Extract(int position, int length) {
    return TextSpan.Slice(position, length);
  }
}