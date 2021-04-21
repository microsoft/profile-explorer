using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using IRExplorerCore.ASM;
using IRExplorerCore;
using System.Diagnostics;

namespace IRExplorerCoreTests {
    [TestClass]
    public class ASMIRSectionReaderTests {
        [TestMethod]
        public void GenerateSummary_CreatesOneTextInfoPerSection() {
            string data =
@"x264_plane_copy_c:
  str lr,[sp]
x264_frame_init_lowres:
  sub sp,sp,#0x40
";
            var bytes = Encoding.UTF8.GetBytes(data);
            var (_, capturedText, _1) = GenerateSummaryFor(bytes);
            Assert.AreEqual(2, capturedText.Count);
            Assert.AreEqual(1, capturedText[0].TextLines.Count);
            Assert.AreEqual("  str lr,[sp]", capturedText[0].TextLines[0]);
            Assert.AreEqual(1, capturedText[1].TextLines.Count);
            Assert.AreEqual("  sub sp,sp,#0x40", capturedText[1].TextLines[0]);
        }

        [TestMethod]
        public void GenerateSummary_GivesNPlus1ProgressUpdates() {
            string data =
@"x264_plane_copy_c:
  str lr,[sp]
x264_frame_init_lowres:
  sub sp,sp,#0x40
";
            var bytes = Encoding.UTF8.GetBytes(data);
            var (_, _1, capturedProgressInfo) = GenerateSummaryFor(bytes);
            // 1 progress for each section + 1 "done"
            Assert.AreEqual(3, capturedProgressInfo.Count);
            // processed all bytes
            Assert.AreEqual(bytes.Length, capturedProgressInfo[2].BytesProcessed);
            foreach (var info in capturedProgressInfo) {
                Assert.AreEqual(bytes.Length, info.TotalBytes);
            }
        }

        [TestMethod]
        public void GenerateSummary_CreatesCorrectIROutputForFunctions() {
            string data =
@"x264_plane_copy_c:
  str lr,[sp]
x264_frame_init_lowres:
  sub sp,sp,#0x40
";
            var bytes = Encoding.UTF8.GetBytes(data);
            var (summary, _, _1) = GenerateSummaryFor(bytes);
            Assert.AreEqual(2, summary.Functions.Count);

            Action<IRTextFunction, string> verifyFunctionBody = (f, expected) => {
                var start = (int)f.Sections[0].Output.DataStartOffset;
                var size = (int)f.Sections[0].Output.Size;
                Assert.AreEqual(expected, Encoding.UTF8.GetString(bytes, start, size));
            };

            var copy = summary.Functions.Find(f => f.Name == "x264_plane_copy_c");
            Assert.IsNotNull(copy);
            Assert.AreEqual(1, copy.SectionCount);
            verifyFunctionBody(copy, "  str lr,[sp]\r\n");

            var init = summary.Functions.Find(f => f.Name == "x264_frame_init_lowres");
            Assert.IsNotNull(init);
            Assert.AreEqual(1, init.SectionCount);
            verifyFunctionBody(init, "  sub sp,sp,#0x40\r\n");
        }

        private (IRTextSummary, List<SectionReaderText>, List<SectionReaderProgressInfo>) GenerateSummaryFor(byte[] input) {
            var reader = new ASMIRSectionReader(input, expectSectionHeaders: true);
            var capturedText = new List<SectionReaderText>();
            var capturedProgressInfo = new List<SectionReaderProgressInfo>();
            var summary = reader.GenerateSummary((reader, info) => {
                capturedProgressInfo.Add(info);
            }, (reader, text) => {
                capturedText.Add(text);
            });
            return (summary, capturedText, capturedProgressInfo);
        }
    }
}
