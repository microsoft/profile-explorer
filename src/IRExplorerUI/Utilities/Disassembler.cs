using System;
using System.Collections.Generic;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Threading.Tasks;
using Gee.External.Capstone;
using IRExplorerUI.Compilers;

namespace IRExplorerUI.Utilities {
    public class Disassembler {
        private byte[] data_;
        private long startRVA_;
        private Machine architecture_;

        public static Disassembler CreateFromBinary(string binaryFilePath) {
            using var peInfo = new PEBinaryInfoProvider(binaryFilePath);

            if (!peInfo.Initialize()) {
                return null;
            }

            var textSection = peInfo.TextSectionHeader;

            if (textSection.HasValue) {
                return new Disassembler(peInfo.BinaryFileInfo.Architecture,
                                        peInfo.GetSectionData(textSection.Value), 
                                        textSection.Value.VirtualAddress);
            }
            //CapstoneDisassembler.CreateArm64Disassembler()
            return null;
        }

        public Disassembler(Machine architecture, byte[] data, long startRva = 0) {
            architecture_ = architecture;
            data_ = data;
            startRVA_ = startRva;
        }


        public string DisassembleToText(DebugFunctionInfo funcInfo) {
            return DisassembleToText(funcInfo.StartRVA, funcInfo.Size);
        }

        public string DisassembleToText(long startRVA, long size) {
            var builder = new StringBuilder();



            return builder.ToString();
        }
    }
}
