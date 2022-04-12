using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using IntervalTree;
using IRExplorerCore;
using IRExplorerUI.Compilers;

namespace IRExplorerUI.Profile {
    class ModuleInfo : IDisposable {
        public bool HasDebugInfo { get; private set; }
        public bool Initialized { get; private set; }

        public IRTextSummary Summary;
        public IDebugInfoProvider DebugInfo;

        //? TODO: Needed only for inlinee samples
        public Dictionary<string, IRTextFunction> unmangledFuncNamesMap_;
        public LoadedDocument ModuleDocument;

        private BinaryFileDescription binaryInfo_;
        private ProfileDataProviderOptions options_;
        private ISession session_;

        public Dictionary<long, IRTextFunction> addressFuncMap;
        public Dictionary<long, string> externalsFuncMap;
        private Dictionary<string, IRTextFunction> externalFuncNames;
        private IntervalTree<long, DebugFunctionInfo> functionRvaTree_;

        public ModuleInfo(ProfileDataProviderOptions options, ISession session) {
            options_ = options;
            session_ = session;
        }

        public void Initialize(IRTextSummary summary, BinaryFileDescription binaryInfo) {
            Initialized = true;
            Summary = summary;
            ModuleDocument = session_.SessionState.FindLoadedDocument(summary);
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

            var disassembler = session_.CompilerInfo.CreateDisassembler(filePath);
            var disasmResult = await disassembler.DisassembleAsync(filePath, session_.CompilerInfo);

            if(disasmResult == null) {
                Trace.TraceWarning($"Failed to disassemble image {imageName}");
                return false;
            }

            var loadedDoc = await Task.Run(() => {
                var loadedDoc = new LoadedDocument(disasmResult.DisassemblyPath, imageName, Guid.NewGuid());
                loadedDoc.Loader = new DocumentSectionLoader(disasmResult.DisassemblyPath, session_.CompilerInfo.IR);
                loadedDoc.Summary = loadedDoc.Loader.LoadDocument(null);

                if (loadedDoc.Summary == null) {
                    loadedDoc.Dispose();
                    return null;
                }

                return loadedDoc;
            });

            if (loadedDoc == null) {
                Trace.TraceWarning($"Failed to load document for image {imageName}");
                return false;
            }

            //? TODO: Register must be done at the end
            session_.SessionState.RegisterLoadedDocument(loadedDoc);
            loadedDoc.BinaryFilePath = filePath;
            loadedDoc.DebugInfoFilePath = disasmResult.DebugInfoFilePath;
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
            addressFuncMap = new Dictionary<long, IRTextFunction>(Summary.Functions.Count);
            externalsFuncMap = new Dictionary<long, string>();
            externalFuncNames = new Dictionary<string, IRTextFunction>();
            functionRvaTree_ = new IntervalTree<long, DebugFunctionInfo>();

            Trace.WriteLine($"Building address mapping for {Summary.ModuleName}, PDB {ModuleDocument.DebugInfoFilePath}");

            foreach (var funcInfo in DebugInfo.EnumerateFunctions(true)) {
                // There can be 0 size func. such as __guard_xfg, ignore.
                if (funcInfo.RVA != 0 && funcInfo.Size > 0) {
                    functionRvaTree_.Add(funcInfo.StartRVA, funcInfo.EndRVA, funcInfo);
                }
                
                var func = Summary.FindFunction(funcInfo.Name);

                if (func != null) {
                    addressFuncMap[funcInfo.RVA] = func;
                }
                else {
                    externalsFuncMap[funcInfo.RVA] = funcInfo.Name;
                }

            }

            Trace.Flush();

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
        
        public DebugFunctionInfo FindFunctionByRVA(long funcAddress) {
            if (!HasDebugInfo) {
                return DebugFunctionInfo.Unknown;
            }

            return DebugInfo.FindFunctionByRVA(funcAddress);
        }

        public DebugFunctionInfo FindFunctionByRVA2(long funcAddress) {
            if (!HasDebugInfo) {
                return DebugFunctionInfo.Unknown;
            }

            var functs = functionRvaTree_.Query(funcAddress);
            foreach (var func in functs) {
                return func;
            }

            return DebugFunctionInfo.Unknown;
        }

        public DebugSourceLineInfo FindSourceLineByRVA(DebugFunctionInfo funcInfo, long rva) {
            if (!HasDebugInfo) {
                return DebugSourceLineInfo.Unknown;
            }

            return DebugInfo.FindSourceLineByRVA(rva);
        }

        public IRTextFunction FindFunction(long funcAddress) {
            if (!HasDebugInfo) {
                return null;
            }

            // Try to use the precise address -> function mapping.
            if (addressFuncMap.TryGetValue(funcAddress, out var textFunction)) {
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

            if (!externalsFuncMap.TryGetValue(funcAddress, out var externalFuncName)) {
                return null;
            }

            if (!externalFuncNames.TryGetValue(externalFuncName, out var textFunction)) {
                // Create a dummy external function that will have no sections. 
                textFunction = new IRTextFunction(externalFuncName);
                Summary.AddFunction(textFunction);
                externalFuncNames[externalFuncName] = textFunction;
            }

            return textFunction;
        }

        public void Dispose() {
        }
    }
}