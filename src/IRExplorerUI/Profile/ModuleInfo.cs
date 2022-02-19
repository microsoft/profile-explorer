using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
        private ISession session_;
        private ProfileDataProviderOptions options_;

        public Dictionary<long, IRTextFunction> addressFuncMap;
        public Dictionary<long, string> externalsFuncMap;
        private Dictionary<string, IRTextFunction> externalFuncNames;
        private PEBinaryInfoProvider binaryInfo_;
        private IntervalTree<long, DebugFunctionInfo> functionRvaTree_;

        public ModuleInfo(ProfileDataProviderOptions options, ISession session) {
            options_ = options;
            session_ = session;
        }

        public void Initialize(IRTextSummary summary) {
            Initialized = true;
            Summary = summary;
            ModuleDocument = session_.SessionState.FindLoadedDocument(summary);
        }

        public async Task<bool> Initialize(string imageName, string imagePath) {
            if (Initialized) {
                return true;
            }

            Trace.WriteLine($"ModuleInfo init {imageName}, path: {imagePath}");

            if (!IsAcceptedModule(imageName)) {
                Trace.TraceInformation($"Ignore not whitelisted image {imageName}");
                return false;
            }

            var filePath = FindBinaryFilePath(imageName, imagePath);

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
            loadedDoc.DebugInfoFilePath = disasmResult.DebugInfoPath;
            ModuleDocument = loadedDoc;
            Summary = loadedDoc.Summary;

            Trace.TraceInformation($"Initialized image {imageName}");
            Initialized = true;
            return true;
        }

        private bool IsAcceptedModule(string imageName) {
            if (options_.BinaryNameWhitelist.Count == 0) {
                return false;
            }

            imageName = Utils.TryGetFileNameWithoutExtension(imageName);
            imageName = imageName.ToLowerInvariant();

            foreach (var file in options_.BinaryNameWhitelist) {
                var fileName = Utils.TryGetFileNameWithoutExtension(file);

                if (fileName.ToLowerInvariant() == imageName) {
                    return true;
                }
            }

            return false;
        }

        public async Task<bool> InitializeDebugInfo() {
            if (DebugInfo != null) {
                return HasDebugInfo;
            }

            //? TODO: Check if it works as MT
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

        private async Task<bool> BuildAddressFunctionMap() {
            // An "external" function here is considered any func. that
            // has no associated IR in the module.
            addressFuncMap = new Dictionary<long, IRTextFunction>(Summary.Functions.Count);
            externalsFuncMap = new Dictionary<long, string>();
            externalFuncNames = new Dictionary<string, IRTextFunction>();
            functionRvaTree_ = new IntervalTree<long, DebugFunctionInfo>();

            Trace.WriteLine($"Building address mapping for {Summary.ModuleName}, PDB {ModuleDocument.DebugInfoFilePath}");

            foreach (var funcInfo in DebugInfo.EnumerateFunctions()) {
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


        public string FindBinaryFilePath(string imageName, string imagePath) {
            // Search in the provided directories.
            imageName = imageName.ToLowerInvariant();
            var imageExtension = Utils.GetFileExtension(imageName);
            var searchPattern = $"*{imageExtension}";

            foreach (var path in options_.BinarySearchPaths) {
                try {
                    var searchPath = Utils.TryGetDirectoryName(path);

                    foreach (var file in Directory.EnumerateFiles(searchPath, searchPattern,
                        SearchOption.TopDirectoryOnly)) {
                        if (Path.GetFileName(file).ToLowerInvariant() == imageName) {
                            return file;
                        }
                    }
                }
                catch (Exception ex) {
                    Trace.TraceError($"Exception searching for binary {imageName} in {path}: {ex.Message}");
                }
            }


            if (File.Exists(imagePath)) {
                return imagePath;
            }

            return null;

            //? look in path dirs, sym server, Environment.GetEnvironmentVariable("PATH")
            //? Similar to resolve PDB file
        }
        
        private IBinaryInfoProvider SetupBinaryInfo(string imagePath) {
            if (binaryInfo_ != null) {
                return binaryInfo_;
            }

            binaryInfo_ = new PEBinaryInfoProvider(imagePath);

            if (binaryInfo_.Initialize()) {
                return binaryInfo_;
            }
            else {
                binaryInfo_.Dispose();
                binaryInfo_ = null;
                return null;
            }
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

        public SourceLineInfo FindSourceLineByRVA(DebugFunctionInfo funcInfo, long rva) {
            if (!HasDebugInfo) {
                return SourceLineInfo.Unknown;
            }

            return DebugInfo.FindSourceLineByRVA(funcInfo, rva);
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
            binaryInfo_?.Dispose();
        }
    }
}