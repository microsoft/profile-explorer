using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Core.Lexer;
using System.Collections.Generic;
using System.Text;

namespace IRExplorerExtension
{
    class DebuggerExpression
    {

        static List<Token> TokenizeString(string value)
        {
            Lexer lexer = new Lexer(value);
            var tokens = new List<Token>();

            while (true)
            {
                var token = lexer.NextToken();

                if (token.IsLineEnd() || token.IsEOF())
                {
                    break;
                }

                tokens.Add(token);
            }

            return tokens;
        }

        static int FindTargetToken(TextLineInfo lineInfo, List<Token> tokens)
        {
            for (int i = 0; i < tokens.Count; i++)
            {
                if (lineInfo.LineColumn >= tokens[i].Location.Offset &&
                    lineInfo.LineColumn < tokens[i].Location.Offset + tokens[i].Length)
                {
                    return i;
                }
            }

            return -1;
        }

        static (int, int) ExpandTargetExpression(int targetToken, List<Token> tokens)
        {
            int left = targetToken;
            int right = targetToken;

            while (left >= 2)
            {
                if (tokens[left - 1].Kind == TokenKind.Dot &&
                   tokens[left - 2].Kind == TokenKind.Identifier)
                {
                    left -= 2; // abc.xyz
                }
                else if (left >= 3 &&
                         tokens[left - 1].Kind == TokenKind.Greater &&
                         tokens[left - 2].Kind == TokenKind.Minus &&
                         tokens[left - 3].Kind == TokenKind.Identifier)
                {
                    left -= 3; // abc->xyz
                }
                else
                {
                    break;
                }
            }

            while (right < tokens.Count - 2)
            {
                if (tokens[right + 1].Kind == TokenKind.Dot &&
                   tokens[right + 2].Kind == TokenKind.Identifier)
                {
                    right += 2; // abc.xyz
                }
                else if (right < tokens.Count - 3 &&
                         tokens[right + 1].Kind == TokenKind.Minus &&
                         tokens[right + 2].Kind == TokenKind.Greater &&
                         tokens[right + 3].Kind == TokenKind.Identifier)
                {
                    right += 3; // abc->xyz
                }
                else
                {
                    break;
                }
            }

            return (left, right);
        }

        static string GetExpressionString(int start, int end, List<Token> tokens)
        {
            var builder = new StringBuilder();

            for (int i = start; i <= end; i++)
            {
                switch (tokens[i].Kind)
                {
                    case TokenKind.Identifier:
                        {
                            builder.Append(tokens[i].Data.ToString());
                            break;
                        }
                    case TokenKind.Dot:
                        {
                            builder.Append(".");
                            break;
                        }
                    case TokenKind.Greater:
                        {
                            builder.Append(">");
                            break;
                        }
                    case TokenKind.Minus:
                        {
                            builder.Append("-");
                            break;
                        }
                    default:
                        {
                            throw new InvalidOperationException("Unexpected token kind");
                        }
                }
            }

            return builder.ToString();
        }

        static public string Create(TextLineInfo lineInfo)
        {
            var tokens = TokenizeString(lineInfo.LineText);
            int targetToken = FindTargetToken(lineInfo, tokens);

            if (targetToken != -1 &&
                tokens[targetToken].IsIdentifier())
            {
                var (left, right) = ExpandTargetExpression(targetToken, tokens);
                return GetExpressionString(left, right, tokens);
            }

            return null;
        }

    }
}
