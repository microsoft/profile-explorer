// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using IRExplorerUI.Diff;
using IRExplorerUI.UTC;
using IRExplorerUI.Query;
using IRExplorerCore;
using IRExplorerCore.ASM;
using IRExplorerCore.IR;
using IRExplorerCore.Analysis;
using System;

namespace IRExplorerUI.Compilers.ASM {

    internal class ASMCompilerIRInfo : ICompilerIRInfo {
        public IRParsingErrorHandler CreateParsingErrorHandler() => new ParsingErrorHandler();

        public IReachableReferenceFilter CreateReferenceFilter(FunctionIR function) {
            throw new NotImplementedException();
        }

        public IRSectionParser CreateSectionParser(IRParsingErrorHandler errorHandler) {
            return null;
        }

        public IRSectionReader CreateSectionReader(string filePath, bool expectSectionHeaders = true) {
            return new ASMIRSectionReader(filePath, expectSectionHeaders);
        }

        public IRSectionReader CreateSectionReader(byte[] textData, bool expectSectionHeaders = true) {
            return new ASMIRSectionReader(textData, expectSectionHeaders);
        }

        public OperandIR GetCallTarget(InstructionIR instr) {
            throw new NotImplementedException();
        }

        public BlockIR GetIncomingPhiOperandBlock(InstructionIR phiInstr, int opIndex) {
            throw new NotImplementedException();
        }

        public bool IsCallInstruction(InstructionIR instr) {
            throw new NotImplementedException();
        }

        public bool IsCopyInstruction(InstructionIR instr) {
            throw new NotImplementedException();
        }

        public bool IsIntrinsicCallInstruction(InstructionIR instr) {
            throw new NotImplementedException();
        }

        public bool IsLoadInstruction(InstructionIR instr) {
            throw new NotImplementedException();
        }

        public bool IsPhiInstruction(InstructionIR instr) {
            throw new NotImplementedException();
        }

        public bool IsStoreInstruction(InstructionIR instr) {
            throw new NotImplementedException();
        }

        public bool OperandsReferenceSameSymbol(OperandIR opA, OperandIR opB, bool exactCheck) {
            throw new NotImplementedException();
        }

        public IRElement SkipCopyInstruction(InstructionIR instr) {
            throw new NotImplementedException();
        }
    }

    public class ASMCompilerInfoProvider : ICompilerInfoProvider {
        private readonly ISession session_;
        private readonly UTCNameProvider names_ = new UTCNameProvider();
        private readonly UTCSectionStyleProvider styles_ = new UTCSectionStyleProvider();
        private readonly UTCRemarkProvider remarks_;
        public ASMCompilerInfoProvider(ISession session) {
            session_ = session;
            remarks_ = new UTCRemarkProvider(this);
        }

        public string CompilerIRName => "ASM";

        public string OpenFileFilter => "Asm Files|*.asm|All Files|*.*";
        public string DefaultSyntaxHighlightingFile => throw new NotImplementedException();

        public ISession Session => session_;

        public ICompilerIRInfo IR => new ASMCompilerIRInfo();

        public INameProvider NameProvider => names_;

        public ISectionStyleProvider SectionStyleProvider => styles_;

        public IRRemarkProvider RemarkProvider => remarks_;

        public List<QueryDefinition> BuiltinQueries => throw new NotImplementedException();

        public List<FunctionTaskDefinition> BuiltinFunctionTasks => throw new NotImplementedException();

        public List<FunctionTaskDefinition> ScriptFunctionTasks => throw new NotImplementedException();

        public bool AnalyzeLoadedFunction(FunctionIR function, IRTextSection section) {
            return true;
        }

        public IDiffOutputFilter CreateDiffOutputFilter() {
            throw new NotImplementedException();
        }

        public IBlockFoldingStrategy CreateFoldingStrategy(FunctionIR function) {
            return new BaseBlockFoldingStrategy(function);
        }

        public void ReloadSettings() {
        }
    }
}
