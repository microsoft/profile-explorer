using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace IRExplorerCore {
    public sealed class SourceFileMapper {
        private readonly Dictionary<string, DirectoryInfo> map_ = new Dictionary<string, DirectoryInfo>();

        public string Map(string sourceFile, Func<string> lookup) {
            if (TryLookupInMap(sourceFile, out var result)) {
                return result;
            }
            result = lookup();
            UpdateMap(sourceFile, result);
            return result;
        }

        private bool TryLookupInMap(string sourceFile, out string result) {
            var index = sourceFile.LastIndexOf(Path.DirectorySeparatorChar);
            while (index != -1) {
                if (map_.TryGetValue(sourceFile.Substring(0, index), out var mappedDirectory)) {
                    result = Path.Combine(mappedDirectory.FullName, sourceFile.Substring(index + 1));
                    return true;
                }

                index = sourceFile.LastIndexOf(Path.DirectorySeparatorChar, index - 1);
            }

            result = null;
            return false;
        }

        private void UpdateMap(string originalPath, string mappedPath) {
            int prevOriginalPath = originalPath.Length;
            int prevMappedPath = mappedPath.Length;
            int originalPathIndex = originalPath.LastIndexOf(Path.DirectorySeparatorChar);
            int mappedPathIndex = mappedPath.LastIndexOf(Path.DirectorySeparatorChar);
            while (originalPathIndex != -1 && mappedPathIndex != -1) {
                if (originalPath.Substring(originalPathIndex, prevOriginalPath - originalPathIndex) !=
                    mappedPath.Substring(mappedPathIndex, prevMappedPath - mappedPathIndex)) {
                    return;
                }
                map_[originalPath.Substring(0, originalPathIndex)] = new DirectoryInfo(mappedPath.Substring(0, mappedPathIndex));
                prevOriginalPath = originalPathIndex;
                prevMappedPath = mappedPathIndex;

                originalPathIndex = originalPath.LastIndexOf(Path.DirectorySeparatorChar, prevOriginalPath - 1);
                mappedPathIndex = mappedPath.LastIndexOf(Path.DirectorySeparatorChar, prevMappedPath - 1);
            }
        }
    }
}
