using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using IRExplorerCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IRExplorerCoreTests {
    [TestClass]
    public class StringTrieTests {
        [TestMethod]
        public void StringValueTest() {
            var trie = new StringTrie<string>();
            var values = new Dictionary<string, string> {
                {"abc", "123" },
                {"def", "456" },
                {"abcd", "234" },
                {"abce", "235" },
                {"abcr", "236" },
                {"abcdef", "124" },
                {"abcdet", "125" },
                {"dxyz", "126" },
                {"dexyz", "127" },
            };

            trie.Build(values);
            
            foreach(var pair in values) {
                bool found = trie.TryGetValue(pair.Key, out var outValue);
                Assert.IsTrue(found);
                Assert.AreEqual(outValue, pair.Value);
            }
        }
    }
}
