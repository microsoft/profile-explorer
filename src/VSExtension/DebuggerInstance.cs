// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using EnvDTE;
using EnvDTE100;
using EnvDTE90;
using EnvDTE90a;
using Debugger = EnvDTE.Debugger;

namespace IRExplorerExtension {
    public class DebuggerInstance {
        private static readonly int DefaultWaitTime = 30000;
        public static Debugger5 debugger_;

        private static readonly Dictionary<string, string> ElementTypeRules =
            new Dictionary<string, string> {
                {"Tuple", "&({0})"},
                {"Tuple *", "{0}"},
                {"Tuple &", "{0}"},
                {"Tuple * *", "*({0})"},
                {"Tuple * &", "{0}"},
                {"tag_BLOCK *", "{0}"},
                {"SSAIterators::SSAIteratorsDetails::IndexedBlock &", "({0}).Block"},
                {"SSAIterators::SSAIteratorsDetails::IndexedTuple<0,0> &", "({0}).Tuple"},
                {"SSAIterators::SSAIteratorsDetails::IndexedTuple<1,0> &", "({0}).Tuple"},
                {"SSAIterators::SSAIteratorsDetails::IndexedTuple<1,1> &", "({0}).Tuple"},
                {"SSAIterators::SSAIteratorsDetails::IndexedTuple<0,1> &", "({0}).Tuple"},
                {"SSAIterators::SSAIteratorsDetails::IndexedUser &", "({0}).UserInstruction"},
                {"SSAIterators::SSAIteratorsDetails::IndexedBlock *", "({0})->Block"},
                {"SSAIterators::SSAIteratorsDetails::IndexedTuple<0,0> *", "({0})->Tuple"},
                {"SSAIterators::SSAIteratorsDetails::IndexedTuple<1,0> *", "({0})->Tuple"},
                {"SSAIterators::SSAIteratorsDetails::IndexedTuple<1,1> *", "({0})->Tuple"},
                {"SSAIterators::SSAIteratorsDetails::IndexedTuple<0,1> *", "({0})->Tuple"},
                {"SSAIterators::SSAIteratorsDetails::IndexedUser *", "({0})->UserInstruction"}
            };

        private static readonly HashSet<string> CppKeywords = new HashSet<string>() {
            "alignas",
            "alignof",
            "and",
            "and_eq",
            "asm",
            "auto",
            "bitand",
            "bitor",
            "bool",
            "break",
            "case",
            "catch",
            "char",
            "char8_t",
            "char16_t",
            "char32_t",
            "class",
            "compl",
            "concept",
            "const",
            "consteval",
            "constexpr",
            "constinit",
            "const_cast",
            "continue",
            "co_await",
            "co_return",
            "co_y",
            "decltype",
            "default",
            "delete",
            "do",
            "double",
            "dynamic_cast",
            "else",
            "enum",
            "explicit",
            "export",
            "extern",
            "false",
            "float",
            "for",
            "friend",
            "goto",
            "if",
            "inline",
            "int",
            "long",
            "mutable",
            "namespace",
            "new",
            "noexcept",
            "not",
            "not_eq",
            "nullptr",
            "operator",
            "or",
            "or_eq",
            "private",
            "protected",
            "public",
            "register",
            "reinterpret_cast",
            "requires",
            "return",
            "short",
            "signed",
            "sizeof",
            "static",
            "static_assert",
            "static_cast",
            "struct",
            "switch",
            "template",
            "this",
            "thread_local",
            "throw",
            "true",
            "try",
            "typedef",
            "typeid",
            "typename",
            "union",
            "unsigned",
            "using",
            "virtual",
            "void",
            "volatile",
            "wchar_t",
            "while",
            "xor",
            "xor_eq"
        };

        private static readonly HashSet<string> UTCKeywords = new HashSet<string>() {
            "ALIGNMENT",
            "OFFSET",
            "UOFFSET",
            "OPCODE",
            "CONDCODE",
            "CTRLCODE",
            "TYPE",
            "TUPLETYPEASINT",
            "STORCLASS",
            "PASINDEX",
            "LINENUM",
            "LINEOFS",
            "VARSYMTAG",
            "SYMKEY",
            "REGNUM",
            "BITNUM",
            "EHSTATE",
            "REGCLASS",
            "IVALTYPE",
            "UIVALTYPE",
            "FVALTYPE",
            "DTYPE",
            "CALLINGCONV",
            "CALLRANGE",
            "CALLDESCR",
            "PCALLDESCR",
            "CALLSIGNATURE",
            "PCALLSIGNATURE",
            "CALLSIGNATURETYPE",
            "PCALLSIGNATURETYPE",
            "METHODINFOHEADER",
            "PMETHODINFOHEADER",
            "INTRINNUM",
            "SYMBOL",
            "PSYMBOL",
            "SYMTAB",
            "PSYMTAB",
            "FUNC",
            "PFUNC",
            "BLOCK",
            "PBLOCK",
            "BLOCKLIST",
            "PBLOCKLIST",
            "LOCBLK",
            "PLOCBLK",
            "LOOP",
            "PLOOP",
            "SEHINFO",
            "PSEHINFO",
            "EHINFO",
            "PEHINFO",
            "EXCEPTINFO",
            "PEXCEPTINFO",
            "REGIONLIST",
            "PREGIONLIST",
            "REGION",
            "PREGION",
            "SSRTREE",
            "PSSRTREE",
            "SYMLIST",
            "SYMLIST",
            "PROBETYPE",
            "QWORD",
            "PROBEINFO",
            "PPROBEINFO",
            "PROBELABELINFO",
            "PPROBELABELINFO",
            "PROBECASEINFO",
            "PPROBECASEINFO",
            "POGOVALUEPROBE",
            "PPOGOVALUEPROBE",
            "TRACE",
            "PTRACE",
            "POGODATASET",
            "PPOGODATASET",
            "POGOPGDPREPASSINFO",
            "PPOGOPGDPREPASSINFO",
            "POGOCOMP",
            "PPOGOCOMP",
            "COMPARC",
            "PCOMPARC",
            "POGOCALL",
            "PPOGOCALL",
            "POGOCASE",
            "PPOGOCASE",
            "POGOCOUNTBUCKET",
            "PPOGOCOUNTBUCKET",
            "TUPLE",
            "PTUPLE",
            "DAGNODE",
            "PDAGNODE",
            "DAGSYMLIST",
            "PDAGSYMLIST",
            "HASHKILL",
            "PHASHKILL",
            "HASHASSIGN",
            "PHASHASSIGN",
            "HASHENTRY",
            "PHASHENTRY",
            "GOPTLIST",
            "PGOPTLIST",
            "BESYMTAB",
            "PBESYMTAB",
            "SYMPOOL",
            "PSYMPOOL",
            "SYMPOOLHEADER",
            "PSYMPOOLHEADER",
            "SYMSCRATCH",
            "PSYMSCRATCH",
            "SYM",
            "PSYM",
            "SYMALIASINFO",
            "PSYMALIASINFO",
            "PAS",
            "PPAS",
            "FLOWGRAPH",
            "PFLOWGRAPH",
            "LOOPGRAPH",
            "PLOOPGRAP",
            "NATURALLOOP",
            "PNATURALLOOP",
            "HOTZONE",
            "PHOTZONE",
            "FRAMEINFO",
            "PFRAMEINFO",
            "NODE",
            "PNODE",
            "TUPLIST",
            "PTUPLIST",
            "TUPLISTX",
            "PTUPLISTX",
            "GLOBLIST",
            "PGLOBLIST",
            "MOD",
            "PMOD",
            "MODNAMESPACE",
            "PMODNAMESPACE",
            "SSRMAP",
            "PSSRMAP",
            "HASHBUCKET",
            "PHASHBUCKET",
            "FILENAMEMAP",
            "PFILENAMEMAP",
            "SOURCEHASH",
            "PSOURCEHASH",
            "FILELIST",
            "PFILELIST",
            "HEURISTIC",
            "PHEURISTIC",
            "LINERANGE",
            "PLINERANGE",
            "SYMFIXUP",
            "PSYMFIXUP",
            "CE_IFILE",
            "PCE_IFILE",
            "STATICLOCALS",
            "PSTATICLOCALS",
            "DATABUF",
            "PDATABUF",
            "DATABUFEXTENDED",
            "PDATABUFEXTENDED",
            "CALLGRAPHNODE",
            "PCALLGRAPHNODE",
            "CALLGRAPHEDGE",
            "PCALLGRAPHEDGE"
        };

        public static bool InBreakMode => debugger_.CurrentMode == dbgDebugMode.dbgBreakMode;
        public static string ProcessName => debugger_.CurrentProcess.Name;
        public static int ProcessId => debugger_.CurrentProcess.ProcessID;

        public static bool IsDebuggingUTC {
            get {
                try {
                    if (!InBreakMode) {
                        return false;
                    }

                    string processName = Path.GetFileName(debugger_.CurrentProcess.Name);
                    return processName == "cl.exe" || processName == "link.exe";
                }
                catch (Exception ex) {
                    Logger.LogException(ex, "Failed to check debugged process name");
                    return false;
                }
            }
        }

        public static void Initialize(Debugger debugger) {
            debugger_ = (Debugger5)debugger;
        }

        private static Expression GetExpression(string text) {
            return debugger_.GetExpression2(text, false, false, DefaultWaitTime);
        }

        public static bool UpdateIR() {
            try {
                var result = GetExpression("dumptupaddress()");
                return result.IsValidValue;
            }
            catch (Exception ex) {
                Logger.LogException(ex, "Failed calling dumptupaddress()");
                return false;
            }
        }

        public static bool IsReservedKeyword(string value) {
            return CppKeywords.Contains(value) ||
                   UTCKeywords.Contains(value);
        }

        public static int GetPointerSize() {
            try {
                var expr = GetExpression("sizeof(void*)");

                if (expr.IsValidValue && int.TryParse(expr.Value, out int size)) {
                    return size;
                }
            }
            catch (Exception ex) {
                Logger.LogException(ex, "Failed to execute debugger expression");
            }

            return 0;
        }

        public static long GetVariableAddress(string variable) {
            try {
                var expr = GetExpression($"&({variable})");

                if (expr.IsValidValue && TryParseInt64(expr.Value, out long value)) {
                    return value;
                }
            }
            catch (Exception ex) {
                Logger.LogException(ex, "Failed to execute debugger expression");
            }

            return 0;
        }

        private static bool TryParseInt32(string value, out int result) {
            if (int.TryParse(value, out result)) {
                return true;
            }

            // Try to parse as a hex value.
            try {
                result = Convert.ToInt32(value, 16);
                return true;
            }
            catch {
                return false;
            }
        }

        private static bool TryParseInt64(string value, out long result) {
            if (long.TryParse(value, out result)) {
                return true;
            }

            // Try to parse as a hex value.
            try {
                result = Convert.ToInt64(value, 16);
                return true;
            }
            catch {
                return false;
            }
        }

        public static long ReadInt32(string variable) {
            try {
                var expr = GetExpression($"*((int*)&({variable}))");

                if (expr.IsValidValue && TryParseInt32(expr.Value, out int value)) {
                    return value;
                }
            }
            catch (Exception ex) {
                Logger.LogException(ex, "Failed to execute debugger expression");
            }

            return 0;
        }

        public static long ReadInt64(string variable) {
            try {
                var expr = GetExpression($"*((long long*)&({variable}))");

                if (expr.IsValidValue && TryParseInt64(expr.Value, out long value)) {
                    return value;
                }
            }
            catch (Exception ex) {
                Logger.LogException(ex, "Failed to execute debugger expression");
            }

            return 0;
        }

        public static long ReadElementAddress(string expression) {
            string type = GetType(expression);

            if (type == null) {
                return 0;
            }

            if (ElementTypeRules.TryGetValue(type, out string typeFormat)) {
                expression = string.Format(typeFormat, expression);
            }

            int ptrSize = GetPointerSize();
            return ptrSize == 4 ? ReadInt32(expression) : ReadInt64(expression);
        }

        public static bool IsPointer(string variable) {
            try {
                var expr = GetExpression(variable);
                return expr.IsValidValue && expr.Type.EndsWith("*");
            }
            catch (Exception ex) {
                Logger.LogException(ex, "Failed to execute debugger expression");
                return false;
            }
        }

        public static string GetType(string variable) {
            try {
                var expr = GetExpression($"{variable}");

                if (!expr.IsValidValue) {
                    return null;
                }

                // Remove extra annotations.
                string type = expr.Type;
                int optionalIndex = type.IndexOf('{');

                if (optionalIndex != -1) {
                    type = type.Substring(0, optionalIndex);
                }

                return type.Trim();
            }
            catch (Exception ex) {
                Logger.LogException(ex, "Failed to execute debugger expression");
                return null;
            }
        }

        public static StackFrame GetCurrentStackFrame() {
            try {
                var stackFrame = (StackFrame2)debugger_.CurrentStackFrame;

                return new StackFrame {
                    File = stackFrame.FileName,
                    Function = stackFrame.FunctionName,
                    LineNumber = (int)stackFrame.LineNumber
                };
            }
            catch (Exception ex) {
                Logger.LogException(ex, "Failed to execute debugger expression");
                return null;
            }
        }

        public static List<string> GetLocalVariables() {
            try {
                var stackFrame = (StackFrame2)debugger_.CurrentStackFrame;
                var localVars = new List<string>();

                foreach (var localVar in stackFrame.Locals) {
                    //? This is not the var name, but the value?
                    var localExpr = (Expression) localVar;

                    if (localExpr.IsValidValue) {
                        localVars.Add(localExpr.Value);
                    }
                }

                return localVars;
            }
            catch (Exception ex) {
                Logger.LogException(ex, "Failed to get local variables");
                return new List<string>();
            }
        }

        public static List<Process3> GetRunningProcesses() {
            try {
                return debugger_.LocalProcesses.OfType<Process3>().ToList();
            }
            catch (Exception ex) {
                Logger.LogException(ex, "Failed to get running processes");
                Debug.WriteLine("Failed to get processes: {0}", ex);
                return null;
            }
        }
    }
}
