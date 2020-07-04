// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using IRExplorerCore.IR;

namespace IRExplorerCore.UTC {
    public enum UTCOpcode {
        OPARG,
        OPASSIGN,
        OPVOLATILEASSIGN,
        OPLOAD,
        OPCONVERT,
        OPNOT,
        OPNEG,
        OPENTERLOAD,
        OPEXITSAVE,
        OPHASH,
        OPHARDINTRIN,
        OPMBARG,
        OPMBASSIGN,
        OPMBVOLATILEASSIGN,
        OPPHI,
        OPRHO,
        OPINLINEARGS,
        OPADD,
        OPSUB,
        OPMUL,
        OPMAX,
        OPMIN,
        OPEMUL,
        OPMULHI,
        OPSHL,
        OPAND,
        OPOR,
        OPXOR,
        OPDIV,
        OPREM,
        OPDIVREM,
        OPSHR,
        OPBITISSET,
        OPBITISNOTSET,
        OPCMP,
        OPCALL,
        OPRET,
        OPBRANCH,
        OPGOTO,
        OPCATCHRET,
        OPFINALLYCALL,
        OPFINALLYRET,
        OPSCOPEGOTO,
        OPSCOPEEXITGOTO,
        OPONERROR,
        OPEXITGOTO,
        OPLEAVE,
        OPSWITCH,
        OPSCOPEEXIT,
        OPQUESTION,
        OPINTRINSIC,
        OPTUPLIST,
        OPASM,
        OPTRY,
        OPTRYEND,
        OPFINALLY,
        OPFINALLYEND,
        OPEXCEPT,
        OPEXCEPTEND,
        OPFILTERSTART,
        OPFILTER,
        OPFILTEREND,
        OPKILLREGS,
        OPSIDEEFFECT,
        OPSCOPEINDEX,
        OPPUSHSTATE,
        OPPOPSTATE,
        OPDTORACTION,
        OPDTOREND,
        OPEHTRY,
        OPCATCH,
        OPEHENDTRY,
        OPEHRET,
        OPNEWSTATE,
        OPSETSTATE,
        OPARGPLACE,
        OPNEWSTATE_DONTOPT,
        OPASSUME,
        OPPROBE,
        OPDOLOOP,
        OPVECTBAND,
        OPVECTBANDNOT,
        OPVECTBOR,
        OPVECTBNOT,
        OPVECTBXOR,
        OPVECTCMPEQ,
        OPVECTCMPNE,
        OPVECTCMPLT,
        OPVECTCMPLE,
        OPVECTCMPGT,
        OPVECTCMPGE,
        OPVECTCMP,
        OPVECTMIN,
        OPVECTMAX,
        OPVECTSQRT,
        OPVECTRSQRT,
        OPVECTRECIP,
        OPVECTSIN,
        OPVECTCOS,
        OPVECTTAN,
        OPVECTASIN,
        OPVECTACOS,
        OPVECTATAN,
        OPVECTATAN2,
        OPVECTSINH,
        OPVECTCOSH,
        OPVECTTANH,
        OPVECTLOG,
        OPVECTLOG10,
        OPVECTEXP,
        OPVECTPOW,
        OPVECTFLOOR,
        OPVECTCEIL,
        OPVECTABS,
        OPVECTEXPAND,
        OPVECTSET1,
        OPVECTBROADCAST,
        OPVECTSETFIRST,
        OPVECTSETZERO,
        OPVECTSETSCALAR,
        OPVECTSETSCALARX,
        OPVECTFILL,
        OPVECTFILLREV,
        OPVECTHADD,
        OPVECTBYTESWAP,
        OPVECTAVG,
        OPVECTSAD,
        OPVECTFMADD,
        OPVECTFMSUB,
        OPVECTFNMADD,
        OPVECTFNMSUB,
        OPVECTFMADDSUB,
        OPVECTFMSUBADD,
        OPVECTEXTRACT,
        OPVECTEXTRACTFIRST,
        OPVECTSHUFFLE,
        OPVECTUNPACKLOW,
        OPVECTUNPACKHIGH,
        OPVECTSIGNMASK,
        OPVECTBLEND,
        OPVECTADDSCALAR,
        OPVECTSUBSCALAR,
        OPVECTMULSCALAR,
        OPVECTDIVSCALAR,
        OPVECTRSQRTSCALAR,
        OPVECTFMADDSCALAR,
        OPVECTFMSUBSCALAR,
        OPVECTFNMADDSCALAR,
        OPVECTFNMSUBSCALAR,
        OPVECTPACKS,
        OPVECTADDS,
        OPVECTSUBS,
        OPVECTADDUS,
        OPVECTSUBUS,
        OPVECTREDUCEADD,
        OPVECTREDUCEMIN,
        OPVECTREDUCEMAX,
        OPVECTREDUCEAND,
        OPVECTREDUCEOR,
        OPVECTREDUCEXOR
    }

    public struct UTCOpcodeInfo {
        public UTCOpcode Opcode { get; set; }
        public InstructionKind Kind { get; set; }

        public UTCOpcodeInfo(UTCOpcode opcode, InstructionKind kind) {
            Opcode = opcode;
            Kind = kind;
        }
    }

    public static class UTCOpcodes {
        private static Dictionary<string, UTCOpcodeInfo> opcodes_ =
            new Dictionary<string, UTCOpcodeInfo> {
                {"OPARG", new UTCOpcodeInfo(UTCOpcode.OPARG, InstructionKind.Unary)},
                {"OPASSIGN", new UTCOpcodeInfo(UTCOpcode.OPASSIGN, InstructionKind.Unary)}, {
                    "OPVOLATILEASSIGN",
                    new UTCOpcodeInfo(UTCOpcode.OPVOLATILEASSIGN, InstructionKind.Unary)
                },
                {"OPLOAD", new UTCOpcodeInfo(UTCOpcode.OPLOAD, InstructionKind.Unary)},
                {"OPCONVERT", new UTCOpcodeInfo(UTCOpcode.OPCONVERT, InstructionKind.Unary)},
                {"OPNOT", new UTCOpcodeInfo(UTCOpcode.OPNOT, InstructionKind.Unary)},
                {"OPNEG", new UTCOpcodeInfo(UTCOpcode.OPNEG, InstructionKind.Unary)}, {
                    "OPENTERLOAD",
                    new UTCOpcodeInfo(UTCOpcode.OPENTERLOAD, InstructionKind.Unary)
                },
                {"OPEXITSAVE", new UTCOpcodeInfo(UTCOpcode.OPEXITSAVE, InstructionKind.Unary)},
                {"OPHASH", new UTCOpcodeInfo(UTCOpcode.OPHASH, InstructionKind.Unary)}, {
                    "OPHARDINTRIN",
                    new UTCOpcodeInfo(UTCOpcode.OPHARDINTRIN, InstructionKind.Unary)
                },
                {"OPMBARG", new UTCOpcodeInfo(UTCOpcode.OPMBARG, InstructionKind.Unary)},
                {"OPMBASSIGN", new UTCOpcodeInfo(UTCOpcode.OPMBASSIGN, InstructionKind.Unary)}, {
                    "OPMBVOLATILEASSIGN",
                    new UTCOpcodeInfo(UTCOpcode.OPMBVOLATILEASSIGN, InstructionKind.Unary)
                },
                {"OPPHI", new UTCOpcodeInfo(UTCOpcode.OPPHI, InstructionKind.Phi)},
                {"OPRHO", new UTCOpcodeInfo(UTCOpcode.OPRHO, InstructionKind.Other)}, {
                    "OPINLINEARGS",
                    new UTCOpcodeInfo(UTCOpcode.OPINLINEARGS, InstructionKind.Other)
                },
                {"OPADD", new UTCOpcodeInfo(UTCOpcode.OPADD, InstructionKind.Binary)},
                {"OPSUB", new UTCOpcodeInfo(UTCOpcode.OPSUB, InstructionKind.Binary)},
                {"OPMUL", new UTCOpcodeInfo(UTCOpcode.OPMUL, InstructionKind.Binary)},
                {"OPMAX", new UTCOpcodeInfo(UTCOpcode.OPMAX, InstructionKind.Binary)},
                {"OPMIN", new UTCOpcodeInfo(UTCOpcode.OPMIN, InstructionKind.Binary)},
                {"OPEMUL", new UTCOpcodeInfo(UTCOpcode.OPEMUL, InstructionKind.Binary)},
                {"OPMULHI", new UTCOpcodeInfo(UTCOpcode.OPMULHI, InstructionKind.Binary)},
                {"OPSHL", new UTCOpcodeInfo(UTCOpcode.OPSHL, InstructionKind.Binary)},
                {"OPAND", new UTCOpcodeInfo(UTCOpcode.OPAND, InstructionKind.Binary)},
                {"OPOR", new UTCOpcodeInfo(UTCOpcode.OPOR, InstructionKind.Binary)},
                {"OPXOR", new UTCOpcodeInfo(UTCOpcode.OPXOR, InstructionKind.Binary)},
                {"OPDIV", new UTCOpcodeInfo(UTCOpcode.OPDIV, InstructionKind.Binary)},
                {"OPREM", new UTCOpcodeInfo(UTCOpcode.OPREM, InstructionKind.Binary)},
                {"OPDIVREM", new UTCOpcodeInfo(UTCOpcode.OPDIVREM, InstructionKind.Binary)},
                {"OPSHR", new UTCOpcodeInfo(UTCOpcode.OPSHR, InstructionKind.Binary)}, {
                    "OPBITISSET",
                    new UTCOpcodeInfo(UTCOpcode.OPBITISSET, InstructionKind.Binary)
                }, {
                    "OPBITISNOTSET",
                    new UTCOpcodeInfo(UTCOpcode.OPBITISNOTSET, InstructionKind.Binary)
                },
                {"OPCMP", new UTCOpcodeInfo(UTCOpcode.OPCMP, InstructionKind.Binary)},
                {"OPCALL", new UTCOpcodeInfo(UTCOpcode.OPCALL, InstructionKind.Call)},
                {"OPRET", new UTCOpcodeInfo(UTCOpcode.OPRET, InstructionKind.Return)},
                {"OPBRANCH", new UTCOpcodeInfo(UTCOpcode.OPBRANCH, InstructionKind.Branch)},
                {"_jcc", new UTCOpcodeInfo(UTCOpcode.OPBRANCH, InstructionKind.Branch)},
                {"OPGOTO", new UTCOpcodeInfo(UTCOpcode.OPGOTO, InstructionKind.Goto)},
                {"jmp", new UTCOpcodeInfo(UTCOpcode.OPGOTO, InstructionKind.Goto)}, {
                    "OPCATCHRET",
                    new UTCOpcodeInfo(UTCOpcode.OPCATCHRET, InstructionKind.Branch)
                }, {
                    "OPFINALLYCALL",
                    new UTCOpcodeInfo(UTCOpcode.OPFINALLYCALL, InstructionKind.Branch)
                }, {
                    "OPFINALLYRET",
                    new UTCOpcodeInfo(UTCOpcode.OPFINALLYRET, InstructionKind.Branch)
                }, {
                    "OPSCOPEGOTO",
                    new UTCOpcodeInfo(UTCOpcode.OPSCOPEGOTO, InstructionKind.Goto)
                }, {
                    "OPSCOPEEXITGOTO",
                    new UTCOpcodeInfo(UTCOpcode.OPSCOPEEXITGOTO, InstructionKind.Goto)
                },
                {"OPONERROR", new UTCOpcodeInfo(UTCOpcode.OPONERROR, InstructionKind.Branch)},
                {"OPEXITGOTO", new UTCOpcodeInfo(UTCOpcode.OPEXITGOTO, InstructionKind.Goto)},
                {"OPLEAVE", new UTCOpcodeInfo(UTCOpcode.OPLEAVE, InstructionKind.Branch)},
                {"OPSWITCH", new UTCOpcodeInfo(UTCOpcode.OPSWITCH, InstructionKind.Switch)}, {
                    "OPSCOPEEXIT",
                    new UTCOpcodeInfo(UTCOpcode.OPSCOPEEXIT, InstructionKind.Other)
                },
                {"OPQUESTION", new UTCOpcodeInfo(UTCOpcode.OPQUESTION, InstructionKind.Other)}, {
                    "OPINTRINSIC",
                    new UTCOpcodeInfo(UTCOpcode.OPINTRINSIC, InstructionKind.Call)
                },
                {"OPTUPLIST", new UTCOpcodeInfo(UTCOpcode.OPTUPLIST, InstructionKind.Other)},
                {"OPASM", new UTCOpcodeInfo(UTCOpcode.OPASM, InstructionKind.Other)},
                {"OPTRY", new UTCOpcodeInfo(UTCOpcode.OPTRY, InstructionKind.Exception)},
                {"OPTRYEND", new UTCOpcodeInfo(UTCOpcode.OPTRYEND, InstructionKind.Exception)}, {
                    "OPFINALLY",
                    new UTCOpcodeInfo(UTCOpcode.OPFINALLY, InstructionKind.Exception)
                }, {
                    "OPFINALLYEND",
                    new UTCOpcodeInfo(UTCOpcode.OPFINALLYEND, InstructionKind.Exception)
                },
                {"OPEXCEPT", new UTCOpcodeInfo(UTCOpcode.OPEXCEPT, InstructionKind.Exception)}, {
                    "OPEXCEPTEND",
                    new UTCOpcodeInfo(UTCOpcode.OPEXCEPTEND, InstructionKind.Exception)
                }, {
                    "OPFILTERSTART",
                    new UTCOpcodeInfo(UTCOpcode.OPFILTERSTART, InstructionKind.Exception)
                },
                {"OPFILTER", new UTCOpcodeInfo(UTCOpcode.OPFILTER, InstructionKind.Exception)}, {
                    "OPFILTEREND",
                    new UTCOpcodeInfo(UTCOpcode.OPFILTEREND, InstructionKind.Exception)
                }, {
                    "OPKILLREGS",
                    new UTCOpcodeInfo(UTCOpcode.OPKILLREGS, InstructionKind.Exception)
                }, {
                    "OPSIDEEFFECT",
                    new UTCOpcodeInfo(UTCOpcode.OPSIDEEFFECT, InstructionKind.Other)
                }, {
                    "OPSCOPEINDEX",
                    new UTCOpcodeInfo(UTCOpcode.OPSCOPEINDEX, InstructionKind.Exception)
                }, {
                    "OPPUSHSTATE",
                    new UTCOpcodeInfo(UTCOpcode.OPPUSHSTATE, InstructionKind.Exception)
                }, {
                    "OPPOPSTATE",
                    new UTCOpcodeInfo(UTCOpcode.OPPOPSTATE, InstructionKind.Exception)
                }, {
                    "OPDTORACTION",
                    new UTCOpcodeInfo(UTCOpcode.OPDTORACTION, InstructionKind.Exception)
                }, {
                    "OPDTOREND",
                    new UTCOpcodeInfo(UTCOpcode.OPDTOREND, InstructionKind.Exception)
                },
                {"OPEHTRY", new UTCOpcodeInfo(UTCOpcode.OPEHTRY, InstructionKind.Exception)},
                {"OPCATCH", new UTCOpcodeInfo(UTCOpcode.OPCATCH, InstructionKind.Exception)}, {
                    "OPEHENDTRY",
                    new UTCOpcodeInfo(UTCOpcode.OPEHENDTRY, InstructionKind.Exception)
                },
                {"OPEHRET", new UTCOpcodeInfo(UTCOpcode.OPEHRET, InstructionKind.Exception)}, {
                    "OPNEWSTATE",
                    new UTCOpcodeInfo(UTCOpcode.OPNEWSTATE, InstructionKind.Exception)
                }, {
                    "OPSETSTATE",
                    new UTCOpcodeInfo(UTCOpcode.OPSETSTATE, InstructionKind.Exception)
                }, {
                    "OPARGPLACE",
                    new UTCOpcodeInfo(UTCOpcode.OPARGPLACE, InstructionKind.Exception)
                }, {
                    "OPNEWSTATE_DONTOPT",
                    new UTCOpcodeInfo(UTCOpcode.OPNEWSTATE_DONTOPT, InstructionKind.Exception)
                },
                {"OPASSUME", new UTCOpcodeInfo(UTCOpcode.OPASSUME, InstructionKind.Exception)},
                {"OPPROBE", new UTCOpcodeInfo(UTCOpcode.OPPROBE, InstructionKind.Exception)},
                {"OPDOLOOP", new UTCOpcodeInfo(UTCOpcode.OPDOLOOP, InstructionKind.Other)},
                {"OPVECTBAND", new UTCOpcodeInfo(UTCOpcode.OPVECTBAND, InstructionKind.Other)}, {
                    "OPVECTBANDNOT",
                    new UTCOpcodeInfo(UTCOpcode.OPVECTBANDNOT, InstructionKind.Other)
                },
                {"OPVECTBOR", new UTCOpcodeInfo(UTCOpcode.OPVECTBOR, InstructionKind.Other)},
                {"OPVECTBNOT", new UTCOpcodeInfo(UTCOpcode.OPVECTBNOT, InstructionKind.Other)},
                {"OPVECTBXOR", new UTCOpcodeInfo(UTCOpcode.OPVECTBXOR, InstructionKind.Other)}, {
                    "OPVECTCMPEQ",
                    new UTCOpcodeInfo(UTCOpcode.OPVECTCMPEQ, InstructionKind.Other)
                }, {
                    "OPVECTCMPNE",
                    new UTCOpcodeInfo(UTCOpcode.OPVECTCMPNE, InstructionKind.Other)
                }, {
                    "OPVECTCMPLT",
                    new UTCOpcodeInfo(UTCOpcode.OPVECTCMPLT, InstructionKind.Other)
                }, {
                    "OPVECTCMPLE",
                    new UTCOpcodeInfo(UTCOpcode.OPVECTCMPLE, InstructionKind.Other)
                }, {
                    "OPVECTCMPGT",
                    new UTCOpcodeInfo(UTCOpcode.OPVECTCMPGT, InstructionKind.Other)
                }, {
                    "OPVECTCMPGE",
                    new UTCOpcodeInfo(UTCOpcode.OPVECTCMPGE, InstructionKind.Other)
                },
                {"OPVECTCMP", new UTCOpcodeInfo(UTCOpcode.OPVECTCMP, InstructionKind.Other)},
                {"OPVECTMIN", new UTCOpcodeInfo(UTCOpcode.OPVECTMIN, InstructionKind.Other)},
                {"OPVECTMAX", new UTCOpcodeInfo(UTCOpcode.OPVECTMAX, InstructionKind.Other)},
                {"OPVECTSQRT", new UTCOpcodeInfo(UTCOpcode.OPVECTSQRT, InstructionKind.Unary)}, {
                    "OPVECTRSQRT",
                    new UTCOpcodeInfo(UTCOpcode.OPVECTRSQRT, InstructionKind.Unary)
                }, {
                    "OPVECTRECIP",
                    new UTCOpcodeInfo(UTCOpcode.OPVECTRECIP, InstructionKind.Unary)
                },
                {"OPVECTSIN", new UTCOpcodeInfo(UTCOpcode.OPVECTSIN, InstructionKind.Unary)},
                {"OPVECTCOS", new UTCOpcodeInfo(UTCOpcode.OPVECTCOS, InstructionKind.Unary)},
                {"OPVECTTAN", new UTCOpcodeInfo(UTCOpcode.OPVECTTAN, InstructionKind.Unary)},
                {"OPVECTASIN", new UTCOpcodeInfo(UTCOpcode.OPVECTASIN, InstructionKind.Unary)},
                {"OPVECTACOS", new UTCOpcodeInfo(UTCOpcode.OPVECTACOS, InstructionKind.Unary)},
                {"OPVECTATAN", new UTCOpcodeInfo(UTCOpcode.OPVECTATAN, InstructionKind.Unary)}, {
                    "OPVECTATAN2",
                    new UTCOpcodeInfo(UTCOpcode.OPVECTATAN2, InstructionKind.Unary)
                },
                {"OPVECTSINH", new UTCOpcodeInfo(UTCOpcode.OPVECTSINH, InstructionKind.Unary)},
                {"OPVECTCOSH", new UTCOpcodeInfo(UTCOpcode.OPVECTCOSH, InstructionKind.Unary)},
                {"OPVECTTANH", new UTCOpcodeInfo(UTCOpcode.OPVECTTANH, InstructionKind.Unary)},
                {"OPVECTLOG", new UTCOpcodeInfo(UTCOpcode.OPVECTLOG, InstructionKind.Unary)}, {
                    "OPVECTLOG10",
                    new UTCOpcodeInfo(UTCOpcode.OPVECTLOG10, InstructionKind.Unary)
                },
                {"OPVECTEXP", new UTCOpcodeInfo(UTCOpcode.OPVECTEXP, InstructionKind.Unary)},
                {"OPVECTPOW", new UTCOpcodeInfo(UTCOpcode.OPVECTPOW, InstructionKind.Unary)}, {
                    "OPVECTFLOOR",
                    new UTCOpcodeInfo(UTCOpcode.OPVECTFLOOR, InstructionKind.Unary)
                },
                {"OPVECTCEIL", new UTCOpcodeInfo(UTCOpcode.OPVECTCEIL, InstructionKind.Unary)},
                {"OPVECTABS", new UTCOpcodeInfo(UTCOpcode.OPVECTABS, InstructionKind.Unary)}, {
                    "OPVECTEXPAND",
                    new UTCOpcodeInfo(UTCOpcode.OPVECTEXPAND, InstructionKind.Other)
                },
                {"OPVECTSET1", new UTCOpcodeInfo(UTCOpcode.OPVECTSET1, InstructionKind.Other)}, {
                    "OPVECTBROADCAST",
                    new UTCOpcodeInfo(UTCOpcode.OPVECTBROADCAST, InstructionKind.Other)
                }, {
                    "OPVECTSETFIRST",
                    new UTCOpcodeInfo(UTCOpcode.OPVECTSETFIRST, InstructionKind.Other)
                }, {
                    "OPVECTSETZERO",
                    new UTCOpcodeInfo(UTCOpcode.OPVECTSETZERO, InstructionKind.Other)
                }, {
                    "OPVECTSETSCALAR",
                    new UTCOpcodeInfo(UTCOpcode.OPVECTSETSCALAR, InstructionKind.Other)
                }, {
                    "OPVECTSETSCALARX",
                    new UTCOpcodeInfo(UTCOpcode.OPVECTSETSCALARX, InstructionKind.Other)
                },
                {"OPVECTFILL", new UTCOpcodeInfo(UTCOpcode.OPVECTFILL, InstructionKind.Other)}, {
                    "OPVECTFILLREV",
                    new UTCOpcodeInfo(UTCOpcode.OPVECTFILLREV, InstructionKind.Other)
                },
                {"OPVECTHADD", new UTCOpcodeInfo(UTCOpcode.OPVECTHADD, InstructionKind.Unary)}, {
                    "OPVECTBYTESWAP",
                    new UTCOpcodeInfo(UTCOpcode.OPVECTBYTESWAP, InstructionKind.Unary)
                },
                {"OPVECTAVG", new UTCOpcodeInfo(UTCOpcode.OPVECTAVG, InstructionKind.Other)},
                {"OPVECTSAD", new UTCOpcodeInfo(UTCOpcode.OPVECTSAD, InstructionKind.Other)}, {
                    "OPVECTFMADD",
                    new UTCOpcodeInfo(UTCOpcode.OPVECTFMADD, InstructionKind.Other)
                }, {
                    "OPVECTFMSUB",
                    new UTCOpcodeInfo(UTCOpcode.OPVECTFMSUB, InstructionKind.Other)
                }, {
                    "OPVECTFNMADD",
                    new UTCOpcodeInfo(UTCOpcode.OPVECTFNMADD, InstructionKind.Other)
                }, {
                    "OPVECTFNMSUB",
                    new UTCOpcodeInfo(UTCOpcode.OPVECTFNMSUB, InstructionKind.Other)
                }, {
                    "OPVECTFMADDSUB",
                    new UTCOpcodeInfo(UTCOpcode.OPVECTFMADDSUB, InstructionKind.Other)
                }, {
                    "OPVECTFMSUBADD",
                    new UTCOpcodeInfo(UTCOpcode.OPVECTFMSUBADD, InstructionKind.Other)
                }, {
                    "OPVECTEXTRACT",
                    new UTCOpcodeInfo(UTCOpcode.OPVECTEXTRACT, InstructionKind.Other)
                }, {
                    "OPVECTEXTRACTFIRST",
                    new UTCOpcodeInfo(UTCOpcode.OPVECTEXTRACTFIRST, InstructionKind.Other)
                }, {
                    "OPVECTSHUFFLE",
                    new UTCOpcodeInfo(UTCOpcode.OPVECTSHUFFLE, InstructionKind.Other)
                }, {
                    "OPVECTUNPACKLOW",
                    new UTCOpcodeInfo(UTCOpcode.OPVECTUNPACKLOW, InstructionKind.Other)
                }, {
                    "OPVECTUNPACKHIGH",
                    new UTCOpcodeInfo(UTCOpcode.OPVECTUNPACKHIGH, InstructionKind.Other)
                }, {
                    "OPVECTSIGNMASK",
                    new UTCOpcodeInfo(UTCOpcode.OPVECTSIGNMASK, InstructionKind.Other)
                }, {
                    "OPVECTBLEND",
                    new UTCOpcodeInfo(UTCOpcode.OPVECTBLEND, InstructionKind.Other)
                }, {
                    "OPVECTADDSCALAR",
                    new UTCOpcodeInfo(UTCOpcode.OPVECTADDSCALAR, InstructionKind.Binary)
                }, {
                    "OPVECTSUBSCALAR",
                    new UTCOpcodeInfo(UTCOpcode.OPVECTSUBSCALAR, InstructionKind.Binary)
                }, {
                    "OPVECTMULSCALAR",
                    new UTCOpcodeInfo(UTCOpcode.OPVECTMULSCALAR, InstructionKind.Binary)
                }, {
                    "OPVECTDIVSCALAR",
                    new UTCOpcodeInfo(UTCOpcode.OPVECTDIVSCALAR, InstructionKind.Binary)
                }, {
                    "OPVECTRSQRTSCALAR",
                    new UTCOpcodeInfo(UTCOpcode.OPVECTRSQRTSCALAR, InstructionKind.Unary)
                }, {
                    "OPVECTFMADDSCALAR",
                    new UTCOpcodeInfo(UTCOpcode.OPVECTFMADDSCALAR, InstructionKind.Other)
                }, {
                    "OPVECTFMSUBSCALAR",
                    new UTCOpcodeInfo(UTCOpcode.OPVECTFMSUBSCALAR, InstructionKind.Other)
                }, {
                    "OPVECTFNMADDSCALAR",
                    new UTCOpcodeInfo(UTCOpcode.OPVECTFNMADDSCALAR, InstructionKind.Other)
                }, {
                    "OPVECTFNMSUBSCALAR",
                    new UTCOpcodeInfo(UTCOpcode.OPVECTFNMSUBSCALAR, InstructionKind.Other)
                }, {
                    "OPVECTPACKS",
                    new UTCOpcodeInfo(UTCOpcode.OPVECTPACKS, InstructionKind.Unary)
                }, {
                    "OPVECTADDS",
                    new UTCOpcodeInfo(UTCOpcode.OPVECTADDS, InstructionKind.Binary)
                }, {
                    "OPVECTSUBS",
                    new UTCOpcodeInfo(UTCOpcode.OPVECTSUBS, InstructionKind.Binary)
                }, {
                    "OPVECTADDUS",
                    new UTCOpcodeInfo(UTCOpcode.OPVECTADDUS, InstructionKind.Binary)
                }, {
                    "OPVECTSUBUS",
                    new UTCOpcodeInfo(UTCOpcode.OPVECTSUBUS, InstructionKind.Binary)
                }, {
                    "OPVECTREDUCEADD",
                    new UTCOpcodeInfo(UTCOpcode.OPVECTREDUCEADD, InstructionKind.Unary)
                }, {
                    "OPVECTREDUCEMIN",
                    new UTCOpcodeInfo(UTCOpcode.OPVECTREDUCEMIN, InstructionKind.Unary)
                }, {
                    "OPVECTREDUCEMAX",
                    new UTCOpcodeInfo(UTCOpcode.OPVECTREDUCEMAX, InstructionKind.Unary)
                }, {
                    "OPVECTREDUCEAND",
                    new UTCOpcodeInfo(UTCOpcode.OPVECTREDUCEAND, InstructionKind.Unary)
                }, {
                    "OPVECTREDUCEOR",
                    new UTCOpcodeInfo(UTCOpcode.OPVECTREDUCEOR, InstructionKind.Unary)
                }, {
                    "OPVECTREDUCEXOR",
                    new UTCOpcodeInfo(UTCOpcode.OPVECTREDUCEXOR, InstructionKind.Unary)
                }
            };

        public static bool GetOpcodeInfo(string value, out UTCOpcodeInfo info) {
            return opcodes_.TryGetValue(value, out info);
        }

        public static bool GetOpcodeInfo(ReadOnlyMemory<char> value, out UTCOpcodeInfo info) {
            return opcodes_.TryGetValue(value.ToString(), out info);
        }

        public static bool IsOpcode(string value) {
            return GetOpcodeInfo(value, out _);
        }
    }
}
