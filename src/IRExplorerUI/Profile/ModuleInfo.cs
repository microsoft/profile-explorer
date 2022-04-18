using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using CSharpTest.Net.Collections;
using IntervalTree;
using IRExplorerCore;
using IRExplorerUI.Compilers;

namespace IRExplorerUI.Profile {
    class ModuleInfo : IDisposable {
        private BinaryFileDescription binaryInfo_;
        private ProfileDataProviderOptions options_;
        private ISession session_;
        private Dictionary<long, IRTextFunction> addressFuncMap_;
        private Dictionary<long, string> externalsFuncMap_;
        private Dictionary<string, IRTextFunction> externalFuncNames_;
        private IntervalTree<long, DebugFunctionInfo> functionRvaTree_; //? TODO: Replace

        public IRTextSummary Summary { get; set; }
        public LoadedDocument ModuleDocument { get; set; }
        public IDebugInfoProvider DebugInfo { get; set; }

        public bool HasDebugInfo { get; set; }
        public bool Initialized { get; set; }

        //? TODO: Needed only for inlinee samples
        public Dictionary<string, IRTextFunction> unmangledFuncNamesMap_;

        public ModuleInfo(ProfileDataProviderOptions options, ISession session) {
            options_ = options;
            session_ = session;
        }
        
        public async Task<bool> Initialize(BinaryFileDescription binaryInfo) {
            if (Initialized) {
                return true;
            }

            binaryInfo_ = binaryInfo;
            var imageName = binaryInfo.ImageName;
            Trace.WriteLine($"ModuleInfo init {imageName}");

            var filePath = await FindBinaryFilePath();

            if (filePath == null) {
                Trace.TraceWarning($"Could not find local path for image {imageName}");
                return false;
            }
            else {
                Trace.TraceInformation($"Found local path for image {imageName}: {filePath}");
            }

            var loadedDoc = await session_.LoadBinaryDocument(filePath, filePath);
            
            if (loadedDoc == null) {
                Trace.TraceWarning($"Failed to load document for image {imageName}");
                return false;
            }
            
            ModuleDocument = loadedDoc;
            Summary = loadedDoc.Summary;

            Trace.TraceInformation($"Initialized image {imageName}");
            Initialized = true;
            return true;
        }

        public async Task<bool> InitializeDebugInfo() {
            if (DebugInfo != null) {
                return HasDebugInfo;
            }

            DebugInfo = session_.CompilerInfo.CreateDebugInfoProvider(ModuleDocument.BinaryFilePath);
            HasDebugInfo = await Task.Run(() => DebugInfo.LoadDebugInfo(ModuleDocument.DebugInfoFilePath));

            if (HasDebugInfo) {
                HasDebugInfo = await Task.Run(() => BuildAddressFunctionMap());
                BuildUnmangledFunctionNameMap();
            }
            else {
                Trace.TraceWarning($"Failed to load debug info: {ModuleDocument.DebugInfoFilePath}");
            }

            return HasDebugInfo;
        }

        private bool BuildAddressFunctionMap() {
            // An "external" function here is considered any func. that
            // has no associated IR in the module.
            addressFuncMap_ = new Dictionary<long, IRTextFunction>(Summary.Functions.Count);
            externalsFuncMap_ = new Dictionary<long, string>();
            externalFuncNames_ = new Dictionary<string, IRTextFunction>();
            functionRvaTree_ = new IntervalTree<long, DebugFunctionInfo>();

            Trace.WriteLine($"Building address mapping for {Summary.ModuleName}, PDB {ModuleDocument.DebugInfoFilePath}");

            foreach (var funcInfo in DebugInfo.EnumerateFunctions(false)) {
                // There can be 0 size func. such as __guard_xfg, ignore.
                if (funcInfo.RVA != 0 && funcInfo.Size > 0) {
                    functionRvaTree_.Add(funcInfo.StartRVA, funcInfo.EndRVA, funcInfo);
                }
                
                var func = Summary.FindFunction(funcInfo.Name);

                if (func != null) {
                    addressFuncMap_[funcInfo.RVA] = func;
                }
                else {
                    externalsFuncMap_[funcInfo.RVA] = funcInfo.Name;
                }

            }

#if DEBUG
            //Trace.WriteLine($"Address mapping for {Summary.ModuleName}, PDB {ModuleDocument.DebugInfoFilePath}");
            //
            //foreach (var pair in addressFuncMap) {
            //    Trace.WriteLine($"  {pair.Key:X}, RVA {pair.Value.Name}");
            //}
#endif

            return true;
        }

        private bool BuildUnmangledFunctionNameMap() {
            unmangledFuncNamesMap_ = new Dictionary<string, IRTextFunction>(Summary.Functions.Count);

            foreach (var function in Summary.Functions) {
                var unmangledName = PDBDebugInfoProvider.DemangleFunctionName(function.Name);
                unmangledFuncNamesMap_[unmangledName] = function;
            }

            return true;
        }


        public async Task<string> FindBinaryFilePath() {
            // Use the symbol server to locate the image,
            // this will also attempt to download it if not found locally.
            if (options_.DownloadBinaryFiles) {
                var imagePath = await session_.CompilerInfo.FindBinaryFile(binaryInfo_);

                if (File.Exists(imagePath)) {
                    return imagePath;
                }
            }

            // Manually search in the provided directories.
            // Give priority to the user directories.
            var imageName = binaryInfo_.ImageName.ToLowerInvariant();
            var imageExtension = Utils.GetFileExtension(imageName);
            var searchPattern = $"*{imageExtension}";

            foreach (var path in options_.BinarySearchPaths) {
                try {
                    var searchPath = Utils.TryGetDirectoryName(path);

                    foreach (var file in Directory.EnumerateFiles(searchPath, searchPattern, SearchOption.TopDirectoryOnly)) {
                        //? TODO: Should also do a checksum match
                        if (Path.GetFileName(file).ToLowerInvariant() == imageName) {
                            return file;
                        }
                    }
                }
                catch (Exception ex) {
                    Trace.TraceError($"Exception searching for binary {imageName} in {path}: {ex.Message}");
                }
            }

            //? TODO: Should also do a checksum match
            if (File.Exists(binaryInfo_.ImagePath)) {
                return binaryInfo_.ImagePath;
            }

            return null;
        }
        
        private List<DebugFunctionInfo> sortedList_;

        DebugFunctionInfo BinarySearch(List<DebugFunctionInfo> ranges, long value) {
            int min = 0;
            int max = ranges.Count - 1;

            while (min <= max) {
                int mid = (min + max) / 2;
                var range = ranges[mid];
                int comparison = range.CompareTo(value);
                
                if (comparison == 0) {
                    return range;
                }
                if (comparison < 0) {
                    min = mid + 1;
                }
                else {
                    max = mid - 1;
                }
            }

            return DebugFunctionInfo.Unknown;
        }

        public DebugFunctionInfo FindDebugFunctionInfo(long funcAddress) {
            if (!HasDebugInfo) {
                return DebugFunctionInfo.Unknown;
            }

            //? TODO: Enable sorted list, integrate in PDBProvider
#if true

            if (sortedList_ == null) {
                sortedList_ = new List<DebugFunctionInfo>();

                foreach (var x in functionRvaTree_) {
                    sortedList_.Add(x.Value);
                }

                sortedList_.Sort();
            }

            return BinarySearch(sortedList_, funcAddress);
#else
            //foreach (var pair in cache_.orderList) {
            //    var func = pair;

            //    if (funcAddress >= func.StartRVA && funcAddress < func.EndRVA) {
            //        Same++;
            //        return func;
            //    }
            //}

            var functs = functionRvaTree_.Query(funcAddress);
            foreach (var func in functs) {
                //cache_.Put(func, true);
                return func;
            }

            return DebugFunctionInfo.Unknown;
#endif
        }

            public IRTextFunction FindFunction(long funcAddress) {
            if (!HasDebugInfo) {
                return null;
            }

            // Try to use the precise address -> function mapping.
            if (addressFuncMap_.TryGetValue(funcAddress, out var textFunction)) {
                return textFunction;
            }

            return null;
        }

        public IRTextFunction FindFunction(long funcAddress, out bool isExternal) {
            var textFunc = FindFunction(funcAddress);

            if (textFunc != null) {
                isExternal = false;
                return textFunc;
            }

            textFunc = FindExternalFunction(funcAddress);

            if (textFunc != null) {
                isExternal = true;
                return textFunc;
            }

            isExternal = true;
            return null;
        }

        private IRTextFunction FindExternalFunction(long funcAddress) {
            if (!HasDebugInfo) {
                return null;
            }

            if (!externalsFuncMap_.TryGetValue(funcAddress, out var externalFuncName)) {
                return null;
            }

            if (!externalFuncNames_.TryGetValue(externalFuncName, out var textFunction)) {
                if (externalFuncName.Contains("quickSortDLL")) {
                    ;
                }

                // Create a dummy external function that will have no sections. 
                textFunction = new IRTextFunction(externalFuncName);
                Summary.AddFunction(textFunction);
                externalFuncNames_[externalFuncName] = textFunction;
            }

            return textFunction;
        }

        public void Dispose() {
        }
    }
}