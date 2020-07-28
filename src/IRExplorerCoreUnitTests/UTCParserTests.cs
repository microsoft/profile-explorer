// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using IRExplorerCore.UTC;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IRExplorerCoreTests {
    [TestClass]
    public class UTCParserTests {
        [TestMethod]
        public void ParseFibAfterReader() {
            var functionText = @"BLOCK -1                                                  
PRAGMA          OPINLINE r=n, d=254                       
ENTRY        _fib (_x$)                                                 #45
PRAGMA          OPBLKSTART Level: 2                                     #45
  t64         = OPCMP     _x$, 2(0x2)                     (4|0=4,4)     #46
                OPBRANCH(LE) &$LN2, t64                   (?|N=4,0)     #46
  t66         = OPSUB     _x$, 1(0x1)                     (4|4=4,4)     #47
  t67         = OPARG     t66                             (4|4=4)       #47
  t65         = OPCALL(#42) &_fib, t67                    (4|4=4,4)     #47
  t69         = OPSUB     _x$, 2(0x2)                     (4|4=4,4)     #47
  t70         = OPARG     t69                             (4|4=4)       #47
  t68         = OPCALL(#42) &_fib, t70                    (4|4=4,4)     #47
  t71         = OPADD     t65, t68                        (4|4=4,4)     #47
                OPRET     t71                             (4|N=4)       #47
                OPGOTO    &$LN1                           (?|N=4)       #47
                OPGOTO    &$LN3                           (?|N=4)       #47
$LN2@fib:                                                 ; uses = 1 
                OPRET     1(0x1)                          (4|N=4)       #49
                OPGOTO    &$LN1                           (?|N=4)       #49
$LN3@fib:                                                 ; uses = 1 
PRAGMA          OPBLKEND Level: 2                                       #50
$LN1@fib:                                                 ; uses = 2 
EXIT                                                                    #50
BLOCK                                                     
";
            var parser = new UTCParser(functionText, null, null);
            var function = parser.Parse();
            Assert.AreEqual(1, function.Blocks.Count);
        }

        [TestMethod]
        public void ParsingFunctionWithMultipleBlocks() {
            var functionText = @"BLOCK 0 Out(1)                                            
ENTRY        ___local_stdio_printf_options ()                           #90
BLOCK 1 In(0) Out(3)                                      
PRAGMA          OPBLKSTART Level: 2                                     #90
                OPRET     &?_OptionsStorage@?1??__local_stdio_printf_options@@9@9 (4|N=4) #92
                OPGOTO    &$LN1                           (?|N=4)       #92
BLOCK 2 Out(3)                                            
PRAGMA          OPBLKEND Level: 2                                       #93
BLOCK 3 In(1,2) Out(4)                                    
$LN1@local_stdi:                                          ; uses = 1 
BLOCK 4 In(3)                                             
EXIT                                                                    #93
BLOCK                                                     
";
            var function = new UTCParser(functionText, null, null).Parse();
            Assert.AreEqual(5, function.Blocks.Count);
            for (int i = 0; i < 5; ++i) {
                Assert.AreEqual(i, function.Blocks[i].Number);
            }
        }
    }
}
