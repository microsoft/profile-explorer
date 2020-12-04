// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Buffers;

namespace IRExplorerCore.Lexer {
    public sealed class Lexer {
        private char current_; // The current character.
        private int line_;            // The current line.
        private int lineStart_;       // The position of the current line start.
        private Stack<Token> returnedTokens_; // Tokens returned back to lexer.
        private CharSource source_;   // The text being analyzed.

        public Lexer() {
            source_ = new CharSource();
            returnedTokens_ = new Stack<Token>(8);            
        }

        private void NextChar() {
            current_ = source_.NextChar();
        }

        private Token MakeToken(TokenKind kind, int length = 1) {
            return new Token {
                Kind = kind,
                Location = new TextLocation {
                    Offset = source_.Position - length - 1,
                    Line = line_,
                    Column = source_.Position - lineStart_ - length - 1
                },
                Length = length,
                Data = ReadOnlyMemory<char>.Empty
            };
        }

        private Token MakeDataToken(TokenKind kind, int startPosition, int length) {
            return new Token {
                Kind = kind,
                Location = new TextLocation {
                    Offset = startPosition,
                    Line = line_,
                    Column = startPosition - lineStart_
                },
                Length = length,
                Data = source_.Extract(startPosition, length)
            };
        }

        private Token ScanNumber() {
            int startPosition = source_.Position;
            char previous = current_;
            NextChar(); // Skip first digit.

            while (CharTable.IsNumberChar(current_)) {
                previous = current_;
                NextChar();
            }

            if ((current_ == '+' || current_ == '-') && (previous == 'E' || previous == 'e')) {
                NextChar(); // Skip over +/-.

                while (CharTable.IsDigit(current_)) {
                    NextChar();
                }
            }

            // A dot that is not followed by digits
            // should not be handled as part of a float number.
            if (previous == '.') {
                source_.GoBack(2);
                NextChar();
            }
            else if (previous == 'x' || previous == 'X') {
                // .x from 123.x should not be part of a number either,
                // since the x does not denote a hex number.
                source_.GoBack(3);
                NextChar();
            }

            int length = source_.Position - startPosition - 1;
            return MakeDataToken(TokenKind.Number, startPosition, length);
        }

        private Token ScanString(char delimiter) {
            int startPosition = source_.Position;
            NextChar(); // Skip start delimiter.

            while (current_ != delimiter) {
                if (current_ == CharSource.EOF || current_ == CharSource.NewLine) {
                    return MakeToken(TokenKind.Invalid);
                }

                NextChar();
            }

            NextChar(); // Skip end delimiter.
            int length = source_.Position - startPosition - 2;
            return MakeDataToken(TokenKind.String, startPosition, length);
        }

        private Token ScanIdentifier() {
            int startPosition = source_.Position;
            NextChar();

            while (CharTable.IsIdentifierChar(current_)) {
                NextChar();
            }

            int length = source_.Position - startPosition - 1;
            return MakeDataToken(TokenKind.Identifier, startPosition, length);
        }

        private Token ScanToken() {
            char letter = current_;

            while (true) {
                NextChar(); // Skip to next letter.

                if (letter == CharSource.EOF) {
                    return MakeToken(TokenKind.EOF);
                }

                switch (letter) {
                    case '\t':
                        break; // Tab.
                    case '\v':
                        break; // Vertical tab.
                    case '\f':
                        break; // Form feed.
                    case '\n': {
                        line_++;
                        lineStart_ = source_.Position + 1;
                        return MakeToken(TokenKind.LineEnd);
                    }
                    case '0':
                    case '1':
                    case '2':
                    case '3':
                    case '4':
                    case '5':
                    case '6':
                    case '7':
                    case '8':
                    case '9': {
                        // Found the start of a number.
                        source_.GoBack(2);
                        return ScanNumber();
                    }
                    case 'a':
                    case 'b':
                    case 'c':
                    case 'd':
                    case 'e':
                    case 'f':
                    case 'g':
                    case 'h':
                    case 'i':
                    case 'j':
                    case 'k':
                    case 'l':
                    case 'm':
                    case 'n':
                    case 'o':
                    case 'p':
                    case 'q':
                    case 'r':
                    case 's':
                    case 't':
                    case 'u':
                    case 'v':
                    case 'w':
                    case 'x':
                    case 'y':
                    case 'z':
                    case 'A':
                    case 'B':
                    case 'C':
                    case 'D':
                    case 'E':
                    case 'F':
                    case 'G':
                    case 'H':
                    case 'I':
                    case 'J':
                    case 'K':
                    case 'L':
                    case 'M':
                    case 'N':
                    case 'O':
                    case 'P':
                    case 'Q':
                    case 'R':
                    case 'S':
                    case 'T':
                    case 'U':
                    case 'V':
                    case 'W':
                    case 'X':
                    case 'Y':
                    case 'Z':
                    case '_':
                    case '@':
                    case '$':
                    case '?': {
                        // Found the start of an identifier or a keyword.
                        source_.GoBack(2);
                        return ScanIdentifier();
                    }
                    case '\"': {
                        // Found the start of a string.
                        source_.GoBack();
                        return ScanString('\"');
                    }
                    case '\'': {
                        return MakeToken(TokenKind.Apostrophe);
                    }
                    case '(': {
                        return MakeToken(TokenKind.OpenParen);
                    }
                    case ')': {
                        return MakeToken(TokenKind.CloseParen);
                    }
                    case '[': {
                        return MakeToken(TokenKind.OpenSquare);
                    }
                    case ']': {
                        return MakeToken(TokenKind.CloseSquare);
                    }
                    case '{': {
                        return MakeToken(TokenKind.OpenCurly);
                    }
                    case '}': {
                        return MakeToken(TokenKind.CloseCurly);
                    }
                    case ':': {
                        return MakeToken(TokenKind.Colon);
                    }
                    case ';': {
                        return MakeToken(TokenKind.SemiColon);
                    }
                    case ',': {
                        return MakeToken(TokenKind.Comma);
                    }
                    case '.': {
                        return MakeToken(TokenKind.Dot);
                    }
                    case '&': {
                        return MakeToken(TokenKind.And);
                    }
                    case '|': {
                        return MakeToken(TokenKind.Or);
                    }
                    case '^': {
                        return MakeToken(TokenKind.Xor);
                    }
                    case '<': {
                        return MakeToken(TokenKind.Less);
                    }
                    case '>': {
                        return MakeToken(TokenKind.Greater);
                    }
                    case '=': {
                        return MakeToken(TokenKind.Equal);
                    }
                    case '+': {
                        return MakeToken(TokenKind.Plus);
                    }
                    case '-': {
                        return MakeToken(TokenKind.Minus);
                    }
                    case '*': {
                        return MakeToken(TokenKind.Star);
                    }
                    case '/': {
                        return MakeToken(TokenKind.Div);
                    }
                    case '~': {
                        return MakeToken(TokenKind.Tilde);
                    }
                    case '!': {
                        return MakeToken(TokenKind.Exclamation);
                    }
                    case '%': {
                        return MakeToken(TokenKind.Percent);
                    }
                    case '#': {
                        //? TODO: Add as custom comment char
                        return MakeToken(TokenKind.Hash);
                    }
                }

                letter = current_;
            }
        }

        private void Reset() {
            returnedTokens_.Clear();
            line_ = 0;
            lineStart_ = 0;
        }

        public void Initialize(string text) {
            Reset();
            source_.Initialize(text);
            current_ = source_.NextChar();
        }

        public void Initialize(ReadOnlyMemory<char> text) {
            Reset();
            source_.Initialize(text);
            current_ = source_.NextChar();
        }

        public Token NextToken() {
            return returnedTokens_.Count > 0 ? 
                   returnedTokens_.Pop() : ScanToken();
        }

        public void ReturnToken(Token token) {
            returnedTokens_.Push(token);
        }

        public Token PeekToken() {
            var token = NextToken();
            ReturnToken(token);
            return token;
        }

        public Token PeekToken(int position) {
            // Use a temp array to store the skipped token,
            // which must be returned after reaching the desired one.
            var tempArray = ArrayPool<Token>.Shared.Rent(position);
            int tempIndex = 0;

            // Get to the token at the position (starting with 1)
            // relative to the previously retrieved token.
            var token = NextToken();
            tempArray[tempIndex++] = token;

            while (!token.IsEOF() && tempIndex < position) {
                token = NextToken();
                tempArray[tempIndex++] = token;
            }

            // Return the tokens so they are are used by NextToken.
            for (int i = tempIndex - 1; i >= 0; i--) {
                ReturnToken(tempArray[i]);
            }

            ArrayPool<Token>.Shared.Return(tempArray);
            return token;
        }

        public delegate bool TokenAction(Token token);

        public void PeekTokenWhile(TokenAction action, int maxLookupLength) {
            var tempArray = ArrayPool<Token>.Shared.Rent(maxLookupLength);
            int tempIndex = 0;
            Token token;

            do {
                token = NextToken();
                tempArray[tempIndex++] = token;
            } while (!token.IsEOF() &&
                     action(token) &&
                     tempIndex < maxLookupLength);

            for (int i = tempIndex - 1; i >= 0; i--) {
                ReturnToken(tempArray[i]);
            }

            ArrayPool<Token>.Shared.Return(tempArray);
        }
    }
}
