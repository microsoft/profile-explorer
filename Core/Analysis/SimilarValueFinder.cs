using System;
using System.Collections.Generic;
using System.Text;
using Core.IR;

namespace Core.Analysis
{
    public class SimilarValueFinder
    {
        FunctionIR function_;

        public SimilarValueFinder(FunctionIR function)
        {
            function_ = function;
        }

        public InstructionIR Find(InstructionIR instr)
        {
            if (instr.Destinations.Count == 0)
            {
                return null;
            }

            var ssaDefId = ReferenceFinder.GetSSADefinitionId(instr.Destinations[0]);
            
            if(!ssaDefId.HasValue)
            {
                return null;
            }

            //? TODO: Some iterators like "ForEachInstr(func)" are needed
            foreach(var block in function_.Blocks)
            {
                foreach(var tuple in block.Tuples)
                {
                    if(tuple is InstructionIR candidateInstr &&
                        candidateInstr.Destinations.Count > 0)
                    {
                        var candidateSSADefId = ReferenceFinder.GetSSADefinitionId(candidateInstr.Destinations[0]);
                        
                        if(candidateSSADefId == ssaDefId &&
                            IsSimilarInstruction(candidateInstr, instr)) {
                            return candidateInstr;
                        }
                    }
                }
            }
            

            return null;
        }

        bool IsSimilarInstruction(InstructionIR instr, InstructionIR otherInstr)
        {
            if (!instr.Opcode.Equals(otherInstr.Opcode) ||
                instr.Destinations.Count != otherInstr.Destinations.Count ||
                instr.Sources.Count != otherInstr.Sources.Count)
            {
                return false;
            }

            return true;
        }
    }
}
