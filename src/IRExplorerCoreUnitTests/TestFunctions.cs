using IRExplorerCore.IR;
using IRExplorerCore.UTC;

namespace IRExplorerCoreTests {
    internal static class TestFunctions {
        public const string DiamondFunctionText =
@"BLOCK 0 Out(1)
ENTRY        func ()
BLOCK 1 In(0) Out(2,3)
BLOCK 2 In(1) Out(4)
BLOCK 3 In(1) Out(4)
BLOCK 4 In(2,3)
EXIT
BLOCK";
        public static readonly FunctionIR DiamondFunction = new UTCParser(DiamondFunctionText, null, null).Parse();

        public const string QuirkyFunctionText =
@"BLOCK 0 Out(1)
ENTRY func()
BLOCK 1 In(0) Out(3)
BLOCK 2 Out(3)
BLOCK 3 In(1,2) Out(4)
BLOCK 4 In(3)
EXIT
BLOCK";
        public static readonly FunctionIR QuirkyFunction = new UTCParser(QuirkyFunctionText, null, null).Parse();

        public const string SimpleFunctionText =
@"BLOCK 0 Out(1)
ENTRY        func ()
BLOCK 1 In(0) Out(2,3)
BLOCK 2 In(1) Out(3)
BLOCK 3 In(1,2) Out(4)
BLOCK 4 In(3)
EXIT
BLOCK";
        public static readonly FunctionIR SimpleFunction = new UTCParser(SimpleFunctionText, null, null).Parse();

        public const string EngineeringACompilerSampleFunctionText =
@"BLOCK 0 Out(1)
ENTRY func ()
BLOCK 1 In(0,3) Out(2,5)
BLOCK 2 In(1) Out(3)
BLOCK 3 In(2,7) Out(4,1)
BLOCK 5 In(1) Out(6,8)
BLOCK 6 In(5) Out(7)
BLOCK 7 In(6,8) Out(3)
BLOCK 8 In(5) Out(7)
BLOCK 4 In(3)
EXIT
BLOCK";
        public static readonly FunctionIR EngineeringACompilerSampleFunction = new UTCParser(EngineeringACompilerSampleFunctionText, null, null).Parse();
    }
}
