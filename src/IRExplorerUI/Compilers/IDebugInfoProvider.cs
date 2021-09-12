using IRExplorerCore;
using IRExplorerCore.IR;

namespace IRExplorerUI.Compilers {
    public interface IDebugInfoProvider {
        bool AnnotateSourceLocations(FunctionIR function, IRTextFunction textFunc);
        bool AnnotateSourceLocations(FunctionIR function, string functionName);
        void Dispose();
        public (string, long) FindFunctionByRVA(long rva);
        (string, int) FindFunctionSourceFilePath(IRTextFunction textFunc);
        (string, int) FindFunctionSourceFilePath(string functionName);
        bool LoadDebugInfo(string debugFilePath);
    }
}