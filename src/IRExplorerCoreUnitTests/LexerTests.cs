// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using IRExplorerCore.Lexer;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IRExplorerCoreTests {
    [TestClass]
    public class LexerTests {
        [TestMethod]
        public void BasicTest() {
            Lexer lexer = new Lexer("abc 123 xyz abc123_");
            Assert.AreEqual(lexer.NextToken().Kind, TokenKind.Identifier);
        }
    }
}
