using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EnvDTE;
using EnvDTE80;
using EnvDTE90;

namespace IRExplorerExtension
{
    public class DebuggerInstance
    {
        static readonly int DefaultWaitTime = 30000;
        public static EnvDTE100.Debugger5 debugger_;

        public static void Initialize(EnvDTE.Debugger debugger)
        {
            debugger_ = (EnvDTE100.Debugger5)debugger;
        }

        public static bool InBreakMode => debugger_.CurrentMode == dbgDebugMode.dbgBreakMode;
        public static string ProcessName => debugger_.CurrentProcess.Name;
        public static int ProcessId => debugger_.CurrentProcess.ProcessID;

        static Expression GetExpression(string text)
        {
            return debugger_.GetExpression2(text, false, false, DefaultWaitTime);
        }

        public static bool UpdateIR()
        {
            try
            {
                var result = GetExpression("dumptupaddress()");
                return result.IsValidValue;
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Failed calling dumptupaddress()");
                return false;
            }
        }

        public static int GetPointerSize()
        {
            try
            {
                var expr = GetExpression("sizeof(void*)");
                int size;

                if (expr.IsValidValue &&
                    int.TryParse(expr.Value, out size))
                {
                    return size;
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Failed to execute debugger expression");
            }

            return 0;
        }

        public static long ReadInt32(string variable)
        {
            try
            {
                var expr = GetExpression(string.Format("*((int*)&({0}))", variable));
                int value;

                if (expr.IsValidValue &&
                    int.TryParse(expr.Value, out value))
                {
                    return value;
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Failed to execute debugger expression");
            }

            return 0;
        }

        public static long ReadInt64(string variable)
        {
            try
            {
                var expr = GetExpression(string.Format("*((long long*)&({0}))", variable));
                long value;

                if (expr.IsValidValue &&
                    long.TryParse(expr.Value, out value))
                {
                    return value;
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Failed to execute debugger expression");
            }

            return 0;
        }

        static Dictionary<string, string> ElementTypeRules = new Dictionary<string, string>()
        {
            { "Tuple", "&({0})" },
            { "Tuple *", "{0}" },
            { "Tuple &", "{0}" },
            { "Tuple * *", "*({0})" },
            { "Tuple * &", "{0}" },
            { "tag_BLOCK *", "{0}" },
            { "SSAIterators::SSAIteratorsDetails::IndexedBlock &", "({0}).Block" },
            { "SSAIterators::SSAIteratorsDetails::IndexedTuple<0,0> &", "({0}).Tuple" },
            { "SSAIterators::SSAIteratorsDetails::IndexedTuple<1,0> &", "({0}).Tuple" },
            { "SSAIterators::SSAIteratorsDetails::IndexedTuple<1,1> &", "({0}).Tuple" },
            { "SSAIterators::SSAIteratorsDetails::IndexedTuple<0,1> &", "({0}).Tuple" },
            { "SSAIterators::SSAIteratorsDetails::IndexedUser &", "({0}).UserInstruction" },
            { "SSAIterators::SSAIteratorsDetails::IndexedBlock *", "({0})->Block" },
            { "SSAIterators::SSAIteratorsDetails::IndexedTuple<0,0> *", "({0})->Tuple" },
            { "SSAIterators::SSAIteratorsDetails::IndexedTuple<1,0> *", "({0})->Tuple" },
            { "SSAIterators::SSAIteratorsDetails::IndexedTuple<1,1> *", "({0})->Tuple" },
            { "SSAIterators::SSAIteratorsDetails::IndexedTuple<0,1> *", "({0})->Tuple" },
            { "SSAIterators::SSAIteratorsDetails::IndexedUser *", "({0})->UserInstruction" }
        };

        public static long ReadElementAddress(string expression)
        {
            var type = GetType(expression);

            if (type == null)
            {
                return 0;
            }

            if (ElementTypeRules.TryGetValue(type, out var typeFormat))
            {
                expression = string.Format(typeFormat, expression);
            }

            var ptrSize = GetPointerSize();
            return ptrSize == 4 ? ReadInt32(expression) :
                                        ReadInt64(expression);
        }

        public static bool IsPointer(string variable)
        {
            try
            {
                var expr = GetExpression(variable);
                return expr.IsValidValue && expr.Type.EndsWith("*");
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Failed to execute debugger expression");
                return false;
            }
        }

        public static string GetType(string variable)
        {
            try
            {
                var expr = GetExpression(string.Format("{0}", variable));

                if (!expr.IsValidValue)
                {
                    return null;
                }

                // Remove extra annotations.
                string type = expr.Type;
                int optionalIndex = type.IndexOf('{');

                if (optionalIndex != -1)
                {
                    type = type.Substring(0, optionalIndex);
                }

                return type.Trim();
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Failed to execute debugger expression");
                return null;
            }
        }

        public static StackFrame GetCurrentStackFrame()
        {
            try
            {
                var stackFrame = (EnvDTE90a.StackFrame2)debugger_.CurrentStackFrame;
                return new StackFrame()
                {
                    File = stackFrame.FileName,
                    Function = stackFrame.FunctionName,
                    LineNumber = (int)stackFrame.LineNumber
                };
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Failed to execute debugger expression");
                return null;
            }
        }

        public static List<Process3> GetRunningProcesses()
        {
            try
            {
                return debugger_.LocalProcesses.OfType<Process3>().ToList();
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Failed to get running processes");
                Debug.WriteLine("Failed to get processes: {0}", ex);
                return null;
            }
        }
    }
}
