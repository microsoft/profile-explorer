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

namespace IRExplorerExtension;

public class DebuggerInstance {
  public static Debugger5 debugger_;
  private static readonly int DefaultWaitTime = 30000;
  private static readonly Dictionary<string, string> ElementTypeRules =
    new() {
      {"Type", "&({0})"},
      {"Type *", "{0}"},
      {"Type &", "{0}"},
      {"Type * *", "*({0})"},
      {"Type * &", "{0}"}
    };
  private static readonly HashSet<string> CppKeywords = new() {
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
  private static readonly HashSet<string> CompilerKeywords = new() {
    "Set of common types used in the compiler codebase"
  };
  public static bool InBreakMode => debugger_.CurrentMode == dbgDebugMode.dbgBreakMode;
  public static string ProcessName => debugger_.CurrentProcess.Name;
  public static int ProcessId => debugger_.CurrentProcess.ProcessID;

  public static bool IsDebuggingCompiler {
    get {
      try {
        if (!InBreakMode) {
          return false;
        }

        string processName = Path.GetFileName(debugger_.CurrentProcess.Name);
        return processName == "compiler.exe";
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
           CompilerKeywords.Contains(value);
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

      foreach (object localVar in stackFrame.Locals) {
        //? This is not the var name, but the value?
        var localExpr = (Expression)localVar;

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

  private static Expression GetExpression(string text) {
    return debugger_.GetExpression2(text, false, false, DefaultWaitTime);
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
}
