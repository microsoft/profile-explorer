// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using IRExplorerCore.IR;
using IRExplorerCore.Lexer;
using LLVMSharp;
using LLVMSharp.Interop;
using llvm = LLVMSharp.Interop.LLVM;

namespace IRExplorerCore.LLVM {
    //? TODO: Map from LLVMopcode -> {text, InstructionKind}
    //? set InstrKind

    public class LLVMSectionParser : IRSectionParser {
        private ParsingErrorHandler errorHandler_;
        private LLVMParser parser_;

        public LLVMSectionParser(ParsingErrorHandler errorHandler = null) {
            if (errorHandler != null) {
                errorHandler_ = errorHandler;
                errorHandler_.Parser = this;
            }
        }

        public FunctionIR ParseSection(IRTextSection section, string sectionText) {
            parser_ = new LLVMParser(errorHandler_, section?.LineMetadata);
            parser_.Initialize(sectionText, section.Output.StartLine);
            var result = parser_.Parse();

            if (result.Count > 0) {
                var function = result.Find((f => f.Name == section.ParentFunction.Name));
                return function;
            }

            return null;
        }

        public FunctionIR ParseSection(IRTextSection section, ReadOnlyMemory<char> sectionText) {
            parser_ = new LLVMParser(errorHandler_, section?.LineMetadata);
            parser_.Initialize(sectionText);
            var result = parser_.Parse();
            return result.Count > 0 ? result[0] : null;
        }
    }

    public unsafe class LLVMParser {
        private ParsingErrorHandler errorHandler_;
        private LLVMOpaqueMemoryBuffer* textBuffer_;
        private LLVMContext context_;
        private Dictionary<IntPtr, BlockIR> blockMap_;
        private Dictionary<IntPtr, InstructionIR> instrMap_;
        private Dictionary<IntPtr, int> valueMap_;
        private Dictionary<int, SSADefinitionTag> ssaDefinitionMap_;
        private int nextBlockNumber_;
        private int nextValueNumber_;
        private IRElementId nextElementId_;
        private int functionStartLine_;

        private string text_;

        public LLVMParser(ParsingErrorHandler errorHandler,
                          Dictionary<int, string> lineMetadata) {
            errorHandler_ = errorHandler;
            context_ = new LLVMContext();
        }

        public void Initialize(string text, long startLine) {
            var inputText = new MarshaledString(text);
            textBuffer_ = llvm.CreateMemoryBufferWithMemoryRange(inputText, (UIntPtr)inputText.Length, new MarshaledString(""), 1);
            //functionStartLine_ = (int)startLine;
            Reset();

            text_ = text;
        }

        public void Initialize(ReadOnlyMemory<char> text) {
            Reset();
            var inputText = new MarshaledString(text.Span);
            textBuffer_ = llvm.CreateMemoryBufferWithMemoryRange(inputText, (UIntPtr)inputText.Length, new MarshaledString(""), 1);

            text_ = text.ToString();
        }

        public List<FunctionIR> Parse() {
            var functions = new List<FunctionIR>();

            if (context_.Handle.TryParseIR(textBuffer_, out var module, out var message)) {
                var dataLayout = llvm.GetModuleDataLayout(module);
                var pointerSize = llvm.PointerSize(dataLayout);
                var pointerIntType = llvm.IntPtrType(dataLayout);

                var func = module.FirstFunction;

                while (func != null) {
                    var parsedFunc = ParseFunction(func);

                    if (parsedFunc != null) {
                        functions.Add(parsedFunc);
                    }

                    func = func.NextFunction;
                }
            }
            else {
                // todo: handle error using messages
                errorHandler_.HandleError(new TextLocation(0, 0), TokenKind.And,
                    new Token(TokenKind.And, new TextLocation(0, 0), 0), message);
            }

            return functions;
        }

        private FunctionIR ParseFunction(LLVMValueRef llvmFunc) {
            Reset();
            var function = CreateFunction(llvmFunc);

            // Create parameters.
            var llvmParams = llvmFunc.Params;

            foreach(var llvmParam in llvmParams) {
                var param = ParseOperand(llvmParam, null);
                function.Parameters.Add(param);
            }

            // Create basic blocks.
            var llvmBlock = llvmFunc.FirstBasicBlock;

            while (llvmBlock != null) {
                var block = GetOrCreateBlock(llvmBlock.Handle, function);
                //? TODO: Extract a label name
                var name = $"%{nextValueNumber_}".AsMemory();
                block.Label = new BlockLabelIR(nextElementId_, name, block);
                nextValueNumber_++; // Blocks are also counted as values.

                var llvmInstr = llvmBlock.FirstInstruction;

                while (llvmInstr != null) {
                    var instr = ParseInstruction(llvmInstr, block);
                    block.Tuples.Add(instr);
                    llvmInstr = llvmInstr.NextInstruction;
                }

                // Set block text range.
                //? TODO: Label text range must be set before it can be used here
                if (block.Label != null) {
                    block.TextLocation = block.Label.TextLocation;
                }
                
                if (block.Tuples.Count > 0) {
                    //if (block.Label == null) {
                        block.TextLocation = block.Tuples[0].TextLocation;
                    //}

                    block.TextLength = block.Tuples[^1].TextLocation.Offset - block.TextLocation.Offset;
                }
                
                function.Blocks.Add(block);
                llvmBlock = llvmBlock.Next;
            }
            
            return function;
        }

        private FunctionIR CreateFunction(LLVMValueRef llvmFunc) {
            return new FunctionIR() {
                Name = llvmFunc.Name, 
                ReturnType = ParseType(llvmFunc)
            };
        }

        private InstructionIR ParseInstruction(LLVMValueRef llvmInstr, BlockIR parent) {
            var instr = CreateInstruction(llvmInstr, parent);
            int opCount = llvmInstr.OperandCount;

            for (uint i = 0; i < opCount; i++) {
                var op = ParseOperand(llvmInstr.GetOperand(i), instr);
                instr.Sources.Add(op);
            }
            
            // Extract the list of successor blocks from the terminator instr.
            var termInstr = llvmInstr.IsATerminatorInst;

            if (termInstr != null) {
                uint succCount = termInstr.SuccessorsCount;

                for (uint i = 0; i < succCount; i++) {
                    var llvmSuccBlock = termInstr.GetSuccessor(i);
                    var succBlock = GetOrCreateBlock(llvmSuccBlock.Handle, parent.ParentFunction);

                    // For switch a block can appear multiple times as a successor.
                    if (!parent.Successors.Contains(succBlock)) {
                        parent.Successors.Add(succBlock);
                        succBlock.Predecessors.Add(parent);
                    }
                }
            }

            // Extract the text location metadata for the IR elements.
            if (llvmInstr.HasMetadata) {
                var md = llvmInstr.GetMetadata(llvm.LLVMIRXTextLocationKind);
                if (md != null) {
                    if (llvmInstr.InstructionOpcode == LLVMOpcode.LLVMICmp) {
                        md = md;
                    }

                    var mdNode = llvm.ValueAsMetadata(md);
                    llvm.IRXTextLocationGetInstrRange(mdNode, out var offset, out var length, out var line);
                    instr.TextLocation = new TextLocation((int)offset, (int)line - functionStartLine_);
                    instr.TextLength = (int)length;

                    if (instr.Destinations.Count > 0) {
                        llvm.LLVMIRXTextLocationGetInstrDestRange(mdNode, 
                            out var destOffset, out var destLength, out var destLine);
                        if (destOffset > 0) {
                            instr.Destinations[0].TextLocation =
                                new TextLocation((int)destOffset, (int)destLine - functionStartLine_);
                            instr.Destinations[0].TextLength = (int)destLength;
                        }
                    }
                    
                    for (int i = 0; i < instr.Sources.Count; i++) {
                        llvm.LLVMIRXTextLocationGetInstrSourceRange(mdNode, (uint)i,
                            out var sourceOffset, out var sourceLength, out var sourceLine);
                        instr.Sources[i].TextLocation = new TextLocation((int)sourceOffset, (int)sourceLine - functionStartLine_);
                        instr.Sources[i].TextLength = (int)sourceLength;
                    }
                }
            }

            return instr;
        }

        private bool HasDestination(LLVMValueRef llvmInstr) {
            //? TODO: Any better way? 0 users?
            switch (llvmInstr.InstructionOpcode) {
                case LLVMOpcode.LLVMStore:
                case LLVMOpcode.LLVMBr:
                case LLVMOpcode.LLVMCallBr:
                case LLVMOpcode.LLVMIndirectBr:
                case LLVMOpcode.LLVMSwitch:
                case LLVMOpcode.LLVMRet: {
                    return false;
                }
                case LLVMOpcode.LLVMCall:
                case LLVMOpcode.LLVMInvoke: {
                    var type = llvmInstr.TypeOf;
                    return llvmInstr.TypeOf.Kind != LLVMTypeKind.LLVMVoidTypeKind;
                }
            }

            return true;
        }

        private InstructionIR GetOrCreateInstruction(LLVMValueRef llvmInstr) {
            if (!instrMap_.TryGetValue(llvmInstr.Handle, out var instr)) {
                instr = new InstructionIR(nextElementId_, InstructionKind.Other, null);
                instrMap_[llvmInstr.Handle] = instr;

                // Create a fake destination to attach a name to.
                if (HasDestination(llvmInstr)) {
                    var operand = CreateOperand(nextElementId_, OperandKind.Temporary,
                                                ParseType(llvmInstr), instr);
                    operand.Role = OperandRole.Destination;

                    //? TODO: Check if instr has a name, see LLParser.cpp,   Inst->setName(NameStr);
                    operand.Value = $"%{nextValueNumber_}".AsMemory();
                    instr.Destinations.Add(operand);
                    CreateSSADefinition(llvmInstr, operand);
                }
            }

            return instr;
        }

        private InstructionIR CreateInstruction(LLVMValueRef llvmInstr, BlockIR parent) {
            // Check if the instr. was already created due to a forward-reference.
            var instr = GetOrCreateInstruction(llvmInstr);
            instr.Opcode = llvmInstr.InstructionOpcode;
            instr.OpcodeText = instr.Opcode.ToString().AsMemory();
            instr.Parent = parent;
            return instr;
        }

        private OperandIR ParseOperand(LLVMValueRef llvmOperand, InstructionIR parent) {
            var operand = CreateOperand(nextElementId_, OperandKind.Other,
                                        ParseType(llvmOperand), parent);
            switch (llvmOperand.Kind) {
                case LLVMValueKind.LLVMArgumentValueKind:
                    operand.Kind = OperandKind.Variable;
                    operand.Value = $"%{nextValueNumber_}".AsMemory();

                    if (parent == null) {
                        // This is a parameter definition.
                        CreateSSADefinition(llvmOperand, operand);
                        operand.Role = OperandRole.Parameter;
                    }
                    else {
                        CreateUseDefinitionLink(llvmOperand, operand);
                        operand.Role = OperandRole.Source;
                    }

                    // Console.WriteLine($"Found LLVMArgumentValueKind: {llvmOperand}");
                    break;
                case LLVMValueKind.LLVMBasicBlockValueKind:
                    var targetBlock = GetOrCreateBlock(llvmOperand.Handle, parent.ParentFunction);
                    operand.Kind = OperandKind.LabelAddress;
                    operand.Value = targetBlock.Label;
                    break;
                case LLVMValueKind.LLVMMemoryUseValueKind:
                    // Console.WriteLine($"Found LLVMMemoryUseValueKind: {llvmOperand}");
                    break;
                case LLVMValueKind.LLVMMemoryDefValueKind:
                    break;
                case LLVMValueKind.LLVMMemoryPhiValueKind:
                    break;
                case LLVMValueKind.LLVMFunctionValueKind:
                    break;
                case LLVMValueKind.LLVMGlobalAliasValueKind:
                    // Console.WriteLine($"Found LLVMGlobalAliasValueKind: {llvmOperand}");
                    break;
                case LLVMValueKind.LLVMGlobalIFuncValueKind:
                    // Console.WriteLine($"Found LLVMGlobalIFuncValueKind: {llvmOperand}");
                    break;
                case LLVMValueKind.LLVMGlobalVariableValueKind:
                    // Console.WriteLine($"Found LLVMGlobalVariableValueKind: {llvmOperand}");
                    break;
                case LLVMValueKind.LLVMBlockAddressValueKind:
                    var block = GetOrCreateBlock(llvmOperand.Handle, parent.ParentFunction);
                    operand.Kind = OperandKind.LabelAddress;

                    // Console.WriteLine($"Found LLVMBlockAddressValueKind: {llvmOperand}");
                    break;
                case LLVMValueKind.LLVMConstantExprValueKind:
                    // Console.WriteLine($"Found LLVMConstantExprValueKind: {llvmOperand}");
                    break;
                case LLVMValueKind.LLVMConstantArrayValueKind:
                    // Console.WriteLine($"Found LLVMConstantArrayValueKind: {llvmOperand}");
                    break;
                case LLVMValueKind.LLVMConstantStructValueKind:
                    // Console.WriteLine($"Found LLVMConstantStructValueKind: {llvmOperand}");
                    break;
                case LLVMValueKind.LLVMConstantVectorValueKind:
                    // Console.WriteLine($"Found LLVMConstantVectorValueKind: {llvmOperand}");
                    break;
                case LLVMValueKind.LLVMUndefValueValueKind:
                    operand.Kind = OperandKind.Undefined;
                    break;
                case LLVMValueKind.LLVMConstantAggregateZeroValueKind:
                    // Console.WriteLine($"Found LLVMConstantAggregateZeroValueKind: {llvmOperand}");
                    break;
                case LLVMValueKind.LLVMConstantDataArrayValueKind:
                    // Console.WriteLine($"Found LLVMConstantDataArrayValueKind: {llvmOperand}");
                    break;
                case LLVMValueKind.LLVMConstantDataVectorValueKind:
                    // Console.WriteLine($"Found LLVMConstantDataVectorValueKind: {llvmOperand}");
                    break;
                case LLVMValueKind.LLVMConstantIntValueKind:
                    operand.Kind = OperandKind.IntConstant;
                    operand.Value = llvmOperand.ConstIntSExt;
                    break;
                case LLVMValueKind.LLVMConstantFPValueKind:
                    operand.Kind = OperandKind.FloatConstant;
                    operand.Value = llvmOperand.GetConstRealDouble(out bool _);
                    break;
                case LLVMValueKind.LLVMConstantPointerNullValueKind:
                    operand.Kind = OperandKind.IntConstant;
                    operand.Value = 0;
                    break;
                case LLVMValueKind.LLVMConstantTokenNoneValueKind:
                    // Console.WriteLine($"Found LLVMConstantTokenNoneValueKind: {llvmOperand}");
                    break;
                case LLVMValueKind.LLVMMetadataAsValueValueKind:
                    // Console.WriteLine($"Found LLVMMetadataAsValueValueKind: {llvmOperand}");
                    break;
                case LLVMValueKind.LLVMInlineAsmValueKind:
                    break;
                case LLVMValueKind.LLVMInstructionValueKind:
                    // This will create an instr. in case of a forward-reference,
                    // which will get the opcode and other info set later.
                    var instr = GetOrCreateInstruction(llvmOperand);
                    operand.Kind = OperandKind.Temporary;
                    operand.Role = OperandRole.Source;

                    //? TODO: Set the operand name with forward-declared instrs somehow
                    //? Maybe have a dict of {fwd instr -> list of users}, then update the users later
                    if (instr.Destinations != null && instr.Destinations.Count > 0) {
                        operand.Value = instr.Destinations[0].NameValue;
                    }

                    CreateUseDefinitionLink(llvmOperand, operand);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            return operand;
        }

        private SSADefinitionTag CreateSSADefinition(LLVMValueRef value, IRElement element) {
            int defNumber = nextValueNumber_++;
            valueMap_[value.Handle] = defNumber;

            var ssaDefTag = GetOrCreateSSADefinition(defNumber);
            ssaDefTag.Owner = element;
            element.AddTag(ssaDefTag);
            return ssaDefTag;
        }

        private SSAUseTag CreateUseDefinitionLink(LLVMValueRef llvmOperand, OperandIR operand) {
            int defNumber = valueMap_[llvmOperand.Handle];
            var ssaDefTag = ssaDefinitionMap_[defNumber];
            var ssaUDLinkTag = new SSAUseTag(defNumber, ssaDefTag) {Owner = operand};
            operand.AddTag(ssaUDLinkTag);
            ssaDefTag.Users.Add(ssaUDLinkTag);
            return ssaUDLinkTag;
        }

        private TypeIR ParseType(LLVMValueRef llvmOperand) {
            var llvmType = llvmOperand.TypeOf;

            //? TODO: More precise type translation, including bit size
            switch (llvmType.Kind) {
                case LLVMTypeKind.LLVMVoidTypeKind:
                    return TypeIR.GetVoid();
                case LLVMTypeKind.LLVMHalfTypeKind:
                    return TypeIR.GetType(TypeKind.Unknown, 0);
                case LLVMTypeKind.LLVMFloatTypeKind:
                    return TypeIR.GetFloat();
                case LLVMTypeKind.LLVMDoubleTypeKind:
                    return TypeIR.GetDouble();
                case LLVMTypeKind.LLVMX86_FP80TypeKind:
                    return TypeIR.GetDouble();
                case LLVMTypeKind.LLVMFP128TypeKind:
                    return TypeIR.GetDouble();
                case LLVMTypeKind.LLVMPPC_FP128TypeKind:
                    return TypeIR.GetDouble();
                case LLVMTypeKind.LLVMLabelTypeKind:
                    return TypeIR.GetType(TypeKind.Unknown, 0);
                case LLVMTypeKind.LLVMIntegerTypeKind:
                    return TypeIR.GetInt((int)llvmType.IntWidth / 8);
                case LLVMTypeKind.LLVMFunctionTypeKind:
                    return TypeIR.GetType(TypeKind.Function, 0);
                case LLVMTypeKind.LLVMStructTypeKind:
                    return TypeIR.GetType(TypeKind.Struct, 0);
                case LLVMTypeKind.LLVMArrayTypeKind:
                    return TypeIR.GetType(TypeKind.Array, 0);
                case LLVMTypeKind.LLVMPointerTypeKind:
                    return TypeIR.GetType(TypeKind.Pointer, (int)llvmType.IntWidth / 8);
                case LLVMTypeKind.LLVMVectorTypeKind:
                    return TypeIR.GetType(TypeKind.Vector, (int)llvmType.IntWidth / 8);
                case LLVMTypeKind.LLVMMetadataTypeKind:
                    return TypeIR.GetType(TypeKind.Unknown, 0);
                case LLVMTypeKind.LLVMX86_MMXTypeKind:
                    return TypeIR.GetType(TypeKind.Vector, (int)llvmType.IntWidth / 8);
                case LLVMTypeKind.LLVMTokenTypeKind:
                    return TypeIR.GetType(TypeKind.Unknown, 0);
                case LLVMTypeKind.LLVMScalableVectorTypeKind:
                    return TypeIR.GetType(TypeKind.Vector, (int)llvmType.IntWidth / 8);
                case LLVMTypeKind.LLVMBFloatTypeKind:
                    return TypeIR.GetFloat();
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private BlockIR GetOrCreateBlock(IntPtr blockHandle, FunctionIR function) {
            if (blockMap_.TryGetValue(blockHandle, out var block)) {
                return block;
            }

            var newBlock = new BlockIR(nextElementId_, nextBlockNumber_, function) {
                Id = nextElementId_.NewBlock(nextBlockNumber_)
            };

            nextBlockNumber_++;
            blockMap_[blockHandle] = newBlock;
            return newBlock;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private OperandIR CreateOperand(IRElementId elementId, OperandKind kind,
            TypeIR type, TupleIR parent) {
#if USE_POOL
            var op = operandPool_.Get();
            op.Id = elementId.NextOperand();
            op.Kind = kind;
            op.Type = type;
            op.Parent = parent;
            return op;
#else
            return new OperandIR(elementId, kind, type, parent);
#endif
        }

        private SSADefinitionTag GetOrCreateSSADefinition(int id) {
            if (ssaDefinitionMap_.TryGetValue(id, out var value)) {
                return value;
            }

            value = new SSADefinitionTag(id);
            ssaDefinitionMap_[id] = value;
            return value;
        }

        private void Reset() {
            nextBlockNumber_ = 0;
            nextValueNumber_ = 0;
            nextElementId_ = IRElementId.FromLong(0);
            blockMap_ = new Dictionary<IntPtr, BlockIR>();
            instrMap_ = new Dictionary<IntPtr, InstructionIR>();
            valueMap_ = new Dictionary<IntPtr, int>();
            ssaDefinitionMap_ = new Dictionary<int, SSADefinitionTag>();
        }
    }
}

