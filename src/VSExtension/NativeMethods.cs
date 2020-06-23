using System.Runtime.InteropServices;
using System.Text;

namespace IRExplorerExtension {
    static class NativeMethods {
        private const int MAX_PATH = 260;

        public static string GetFullPathFromWindows(string exeName) {
            var sb = new StringBuilder(exeName, MAX_PATH);
            return PathFindOnPath(sb, null) ? sb.ToString() : null;
        }

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, SetLastError = false)]
        private static extern bool PathFindOnPath([In] [Out] StringBuilder pszFile,
                                                  [In] string[] ppszOtherDirs);
    }
}
