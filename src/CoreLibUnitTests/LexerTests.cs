// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using IRExplorerCore.Lexer;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CoreUnitTests {
    [TestClass]
    public class UnitTest1 {
        [TestMethod]
        public void TestMethod1() {
            Lexer lexer = new Lexer("abc 123 xyz abc123_");
            Assert.AreEqual(lexer.NextToken().Kind, TokenKind.Identifier);
        }
    }
}
