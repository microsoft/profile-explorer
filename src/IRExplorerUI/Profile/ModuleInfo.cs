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
    // native/managed Moduleinfo

    //? DisassemblerSectionLoader makes fake doc ,
    //? use manual DisassemblerSectionLoader, inject debugInfo for EnumerateFunctions
    //? - split methods by module

    public class ModuleInfo : IDisposable {
        private ISession session_;
        private BinaryFileDescription binaryInfo_;
        private ProfileDataProviderOptions options_;
        private List<DebugFunctionInfo> sortedFuncList_;
        private Dictionary<long, IRTextFunction> addressFuncMap_;
        private Dictionary<long, string> externalsFuncMap_;
        private Dictionary<string, IRTextFunction> externalFuncNames_;

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
        
        public async Task<bool> Initialize(BinaryFileDescription binaryInfo, SymbolFileSourceOptions options,
                                            IDebugInfoProvider debugInfo) {
            if (Initialized) {
                return true;
            }

            binaryInfo_ = binaryInfo;
            var imageName = binaryInfo.ImageName;
            Trace.WriteLine($"ModuleInfo init {imageName}");
            
            var filePath = await FindBinaryFilePath(options).ConfigureAwait(false);

            if (filePath == null) {
                Trace.TraceWarning($"Could not find local path for image {imageName}");
                return false;
            }
            else {
                Trace.TraceInformation($"Found local path for image {imageName}: {filePath}");
            }

            var localBinaryInfo = PEBinaryInfoProvider.GetBinaryFileInfo(filePath);
            bool isManagedImagae = localBinaryInfo != null && localBinaryInfo.IsManagedImage;
            
            //? split into providers for each .net moduel, plus a list of all functs
            var loadedDoc = await session_.LoadBinaryDocument(filePath, binaryInfo.ImageName, debugInfo).ConfigureAwait(false);
            
            if (loadedDoc == null) {
                Trace.TraceWarning($"Failed to load document for image {imageName}");
                return false;
            }

            ModuleDocument = loadedDoc;
            Summary = loadedDoc.Summary;

            if (isManagedImagae) {
                DebugInfo = debugInfo;
                HasDebugInfo = HasDebugInfo = await Task.Run(() => BuildAddressFunctionMap()).ConfigureAwait(false);
            }

            Trace.TraceInformation($"Initialized image {imageName}");
            Initialized = true;
            return true;
        }

        public async Task<bool> InitializeDebugInfo() {
            if (DebugInfo != null) {
                return HasDebugInfo;
            }

            DebugInfo = session_.CompilerInfo.CreateDebugInfoProvider(ModuleDocument.BinaryFilePath);
            HasDebugInfo = await Task.Run(() => DebugInfo.LoadDebugInfo(ModuleDocument.DebugInfoFilePath)).ConfigureAwait(false);

            if (HasDebugInfo) {
                HasDebugInfo = await Task.Run(() => BuildAddressFunctionMap()).ConfigureAwait(false);
                //? TODO: Not thread safe, heap corruption from Undname
                // BuildUnmangledFunctionNameMap();
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
            sortedFuncList_ = new List<DebugFunctionInfo>();

            Trace.WriteLine($"Building address mapping for {Summary.ModuleName}, PDB {ModuleDocument.DebugInfoFilePath}");

            foreach (var funcInfo in DebugInfo.EnumerateFunctions(false)) {
                if (funcInfo.RVA != 0) {
                    sortedFuncList_.Add(funcInfo);
                }
                
                var func = Summary.FindFunction(funcInfo.Name);

                if (func != null) {
                    addressFuncMap_[funcInfo.RVA] = func;
                }
                else {
                    externalsFuncMap_[funcInfo.RVA] = funcInfo.Name;
                }
            }
            
            sortedFuncList_.Sort();

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


        public async Task<string> FindBinaryFilePath(SymbolFileSourceOptions options) {
            // Use the symbol server to locate the image,
            // this will also attempt to download it if not found locally.
            if (options_.DownloadBinaryFiles) {
                var imagePath = await session_.CompilerInfo.FindBinaryFile(binaryInfo_, options).ConfigureAwait(false);

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
                            Trace.WriteLine($"Using unchecked local file {file}");
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
        
        public DebugFunctionInfo FindDebugFunctionInfo(long funcAddress) {
            if (!HasDebugInfo) {
                return DebugFunctionInfo.Unknown;
            }

            //? TODO: Enable sorted list, integrate in PDBProvider
            return DebugFunctionInfo.BinarySearch(sortedFuncList_, funcAddress);
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