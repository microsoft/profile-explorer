// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using IRExplorerCore.IR;
using LLVMSharp;
using LLVMSharp.Interop;
using llvm = LLVMSharp.Interop.LLVM;

namespace IRExplorerCore.LLVM {
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
            parser_.Initialize(sectionText);
            var result = parser_.Parse();
            return result.Count > 0 ? result[0] : null;
        }

        public FunctionIR ParseSection(IRTextSection section, ReadOnlyMemory<char> sectionText) {
            parser_ = new LLVMParser(errorHandler_, section?.LineMetadata);
            parser_.Initialize(sectionText);
            var result = parser_.Parse();
            return result.Count > 0 ? result[0] : null;
        }

    }

    public unsafe class LLVMParser {
        private LLVMOpaqueMemoryBuffer* textBuffer_;
        private LLVMContext context_;
        private Dictionary<IntPtr, BlockIR> blockMap_;
        private Dictionary<IntPtr, InstructionIR> instrMap_;
        private Dictionary<IntPtr, int> valueMap_;
        private Dictionary<int, SSADefinitionTag> ssaDefinitionMap_;
        private int nextBlockNumber_;
        private int nextValueNumber_;
        private IRElementId nextElementId_;

        public LLVMParser(ParsingErrorHandler errorHandler,
                          Dictionary<int, string> lineMetadata) {
            context_ = new LLVMContext();
        }

        public void Initialize(string text) {
            var inputText = new MarshaledString(text);
            textBuffer_ = llvm.CreateMemoryBufferWithMemoryRange(inputText, (UIntPtr)inputText.Length, new MarshaledString(""), 1);
            Reset();
        }

        public void Initialize(ReadOnlyMemory<char> text) {
            Reset();
            var inputText = new MarshaledString(text.Span);
            textBuffer_ = llvm.CreateMemoryBufferWithMemoryRange(inputText, (UIntPtr)inputText.Length, new MarshaledString(""), 1);
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
                nextValueNumber_++; // Blocks are also counted as values.

                // todo: block label GetBasicBlockName

                var llvmInstr = llvmBlock.FirstInstruction;

                while (llvmInstr != null) {
                    var instr = ParseInstruction(llvmInstr, block);
                    block.Tuples.Add(instr);
                    llvmInstr = llvmInstr.NextInstruction;
                }

                function.Blocks.Add(block);
                llvmBlock = llvmBlock.Next;
            }

            Console.WriteLine("-------------------------------");
            var t = new IRPrinter(function).Print();
            Console.WriteLine(t);
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

                    // With switch a block can appear multiple times as a successor.
                    if (!parent.Successors.Contains(succBlock)) {
                        parent.Successors.Add(succBlock);
                        succBlock.Predecessors.Add(parent);
                    }
                }
            }


            if (llvmInstr.HasMetadata) {
                Console.WriteLine("Instr MD ");

                var md = llvmInstr.GetMetadata(llvm.LLVMIRXTextLocationKind);

                if (md != null) {
                    llvm.IRXTextLocationGetInstrRange(llvm.ValueAsMetadata(md),
                                                        out var offset, out var length);
                        Console.WriteLine($"Instr location: {offset}, {length}");
                }
            }

            return instr;
        }

        private InstructionIR CreateInstruction(LLVMValueRef llvmInstr, BlockIR parent) {
            var instr = new InstructionIR(nextElementId_, InstructionKind.Other, parent);
            instr.Opcode = llvmInstr.InstructionOpcode;
            instr.OpcodeText = instr.Opcode.ToString().AsMemory();
            instrMap_[llvmInstr.Handle] = instr;

            //? Create a fake destination to attach a name to
            var operand = CreateOperand(nextElementId_, OperandKind.Temporary,
                                        ParseType(llvmInstr), instr);
            operand.Role = OperandRole.Destination;
            operand.Value = $"%{nextValueNumber_}".AsMemory();
            instr.Destinations.Add(operand);
            CreateSSADefinition(llvmInstr, operand);
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

                    Console.WriteLine($"Found LLVMArgumentValueKind: {llvmOperand}");
                    break;
                case LLVMValueKind.LLVMBasicBlockValueKind:
                    //? TODO: A labelAddress ref for a block label
                    //Console.WriteLine($"Found LLVMBasicBlockValueKind: {llvmOperand}");
                    break;
                case LLVMValueKind.LLVMMemoryUseValueKind:
                    Console.WriteLine($"Found LLVMMemoryUseValueKind: {llvmOperand}");
                    break;
                case LLVMValueKind.LLVMMemoryDefValueKind:
                    break;
                case LLVMValueKind.LLVMMemoryPhiValueKind:
                    break;
                case LLVMValueKind.LLVMFunctionValueKind:
                    break;
                case LLVMValueKind.LLVMGlobalAliasValueKind:
                    Console.WriteLine($"Found LLVMGlobalAliasValueKind: {llvmOperand}");
                    break;
                case LLVMValueKind.LLVMGlobalIFuncValueKind:
                    Console.WriteLine($"Found LLVMGlobalIFuncValueKind: {llvmOperand}");
                    break;
                case LLVMValueKind.LLVMGlobalVariableValueKind:
                    Console.WriteLine($"Found LLVMGlobalVariableValueKind: {llvmOperand}");
                    break;
                case LLVMValueKind.LLVMBlockAddressValueKind:
                    var block = GetOrCreateBlock(llvmOperand.Handle, parent.ParentFunction);
                    operand.Kind = OperandKind.LabelAddress;

                    Console.WriteLine($"Found LLVMBlockAddressValueKind: {llvmOperand}");
                    break;
                case LLVMValueKind.LLVMConstantExprValueKind:
                    Console.WriteLine($"Found LLVMConstantExprValueKind: {llvmOperand}");
                    break;
                case LLVMValueKind.LLVMConstantArrayValueKind:
                    Console.WriteLine($"Found LLVMConstantArrayValueKind: {llvmOperand}");
                    break;
                case LLVMValueKind.LLVMConstantStructValueKind:
                    Console.WriteLine($"Found LLVMConstantStructValueKind: {llvmOperand}");
                    break;
                case LLVMValueKind.LLVMConstantVectorValueKind:
                    Console.WriteLine($"Found LLVMConstantVectorValueKind: {llvmOperand}");
                    break;
                case LLVMValueKind.LLVMUndefValueValueKind:
                    operand.Kind = OperandKind.Undefined;
                    break;
                case LLVMValueKind.LLVMConstantAggregateZeroValueKind:
                    Console.WriteLine($"Found LLVMConstantAggregateZeroValueKind: {llvmOperand}");
                    break;
                case LLVMValueKind.LLVMConstantDataArrayValueKind:
                    Console.WriteLine($"Found LLVMConstantDataArrayValueKind: {llvmOperand}");
                    break;
                case LLVMValueKind.LLVMConstantDataVectorValueKind:
                    Console.WriteLine($"Found LLVMConstantDataVectorValueKind: {llvmOperand}");
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
                    Console.WriteLine($"Found LLVMConstantTokenNoneValueKind: {llvmOperand}");
                    break;
                case LLVMValueKind.LLVMMetadataAsValueValueKind:
                    Console.WriteLine($"Found LLVMMetadataAsValueValueKind: {llvmOperand}");
                    break;
                case LLVMValueKind.LLVMInlineAsmValueKind:
                    break;
                case LLVMValueKind.LLVMInstructionValueKind:
                    if(instrMap_.TryGetValue(llvmOperand.Handle, out var instr)) {
                        operand.Kind = OperandKind.Temporary;
                        operand.Role = OperandRole.Source;
                        operand.Value = $"%{nextValueNumber_}".AsMemory();
                        CreateUseDefinitionLink(llvmOperand, operand);
                    }
                    else {
                        throw new InvalidOperationException("Ref to unseen instr");
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            if (llvmOperand.HasMetadata) {
                Console.WriteLine("Operand MD ");
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

            //? TODO: More precise type translation, including size
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

            //? TODO: Extract a label name
            var name = $"%{nextValueNumber_}".AsMemory();
            newBlock.Label = new BlockLabelIR(nextElementId_, name, newBlock);

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

