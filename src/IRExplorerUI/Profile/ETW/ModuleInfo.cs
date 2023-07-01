using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using DocumentFormat.OpenXml.Wordprocessing;
using IRExplorerCore;
using IRExplorerCore.Utilities;
using IRExplorerUI.Compilers;
using IRExplorerUI.Profile;

namespace IRExplorerUI.Profile {
    public sealed class ModuleInfo {
        private ISession session_;
        private BinaryFileDescriptor binaryInfo_;
        private List<FunctionDebugInfo> sortedFuncList_;
        private Dictionary<long, IRTextFunction> addressFuncMap_;
        private Dictionary<long, IRTextFunction> externalsFuncMap_;
        private ProfileDataReport report_;
        private ReaderWriterLockSlim lock_;

        public IRTextSummary Summary { get; set; }
        public LoadedDocument ModuleDocument { get; set; }
        public IDebugInfoProvider DebugInfo { get; set; }

        public bool HasDebugInfo { get; set; }
        public bool Initialized { get; set; }
        public bool IsManaged { get; set; }

        //? TODO: Needed only for inlinee samples
        public Dictionary<string, IRTextFunction> unmangledFuncNamesMap_;

        public ModuleInfo(ProfileDataReport report, ISession session) {
            report_ = report;
            session_ = session;
            lock_ = new ReaderWriterLockSlim();
        }

        public async Task<bool> Initialize(BinaryFileDescriptor binaryInfo, SymbolFileSourceOptions symbolOptions,
                                           IDebugInfoProvider debugInfo) {
            if (Initialized) {
                return true;
            }

            binaryInfo_ = binaryInfo;
            var imageName = binaryInfo.ImageName;
            Trace.WriteLine($"ModuleInfo init {imageName}");

            var binFile = await FindBinaryFilePath(symbolOptions).ConfigureAwait(false);

            if (binFile == null || !binFile.Found) {
                Trace.TraceWarning($"  Could not find local path for image {imageName}");
                report_.AddModuleInfo(binaryInfo, binFile, ModuleLoadState.NotFound);
                CreateDummyDocument(binaryInfo);
                return false;
            }

            var loadedDoc = await session_.LoadBinaryDocument(binFile.FilePath, binaryInfo.ImageName, debugInfo).ConfigureAwait(false);

            if (loadedDoc == null) {
                Trace.TraceWarning($"  Failed to load document for image {imageName}");
                report_.AddModuleInfo(binaryInfo, binFile, ModuleLoadState.Failed);
                CreateDummyDocument(binaryInfo);
                return false;
            }
            else {
                Trace.TraceWarning($"  Loaded document for image {imageName}");
                report_.AddModuleInfo(binaryInfo, binFile, ModuleLoadState.Loaded);
            }

            ModuleDocument = loadedDoc;
            Summary = loadedDoc.Summary;

            // .Net debug info is passed in by the client.
            IsManaged = binFile.BinaryFile != null && binFile.BinaryFile.IsManagedImage;

            if (IsManaged && debugInfo != null) {
                Trace.TraceInformation($"  Has managed debug {imageName}");
                DebugInfo = debugInfo;
                HasDebugInfo = true;
                await Task.Run(() => BuildAddressFunctionMap()).ConfigureAwait(false);
                loadedDoc.DebugInfo = debugInfo;
            }

            Trace.TraceInformation($"Initialized image {imageName}");
            Initialized = true;
            return true;
        }

        private void CreateDummyDocument(BinaryFileDescriptor binaryInfo) {
            // Create a dummy document to represent the module,
            // AddPlaceholderFunction will populate it.
            ModuleDocument = LoadedDocument.CreateDummyDocument(binaryInfo.ImageName);
            Summary = ModuleDocument.Summary;
        }

        public async Task<bool> InitializeDebugInfo() {
            if (DebugInfo != null) {
                return HasDebugInfo;
            }

            DebugInfo = session_.CompilerInfo.CreateDebugInfoProvider(ModuleDocument.DebugInfoFile);
            HasDebugInfo = DebugInfo != null;

            if (HasDebugInfo) {
                HasDebugInfo = await Task.Run(() => BuildAddressFunctionMap()).ConfigureAwait(false);
                //? TODO: Not thread safe, heap corruption from Undname
                // BuildUnmangledFunctionNameMap();
            }
            else {
                Trace.TraceWarning($"Failed to load debug info: {ModuleDocument.DebugInfoFile}");
            }

            report_.AddDebugInfo(binaryInfo_, ModuleDocument.DebugInfoFile);
            return HasDebugInfo;
        }

        private bool BuildAddressFunctionMap() {
            // An "external" function here is considered any func. that
            // has no associated IR in the module.
            addressFuncMap_ = new Dictionary<long, IRTextFunction>(Summary.Functions.Count);
            externalsFuncMap_ = new Dictionary<long, IRTextFunction>();

            if (!HasDebugInfo) {
                return false;
            }

            Trace.WriteLine($"Building address mapping for {Summary.ModuleName}, PDB {ModuleDocument.DebugInfoFile}");
            sortedFuncList_ = DebugInfo.GetSortedFunctions();

            foreach (var funcInfo in sortedFuncList_) {
                //Trace.WriteLine($"{funcInfo.Name}, {funcInfo.RVA}");
                var func = Summary.FindFunction(funcInfo.Name);

                if (func != null) {
                    addressFuncMap_[funcInfo.RVA] = func;
                }
                else {
                    externalsFuncMap_[funcInfo.RVA] = ModuleDocument.AddDummyFunction(funcInfo.Name);
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

        public async Task<BinaryFileSearchResult> FindBinaryFilePath(SymbolFileSourceOptions options) {
            // Use the symbol server to locate the image,
            // this will also attempt to download it if not found locally.
            return await session_.CompilerInfo.FindBinaryFile(binaryInfo_, options).ConfigureAwait(false);
        }

        public FunctionDebugInfo FindFunctionDebugInfo(long funcAddress) {
            if (!HasDebugInfo || sortedFuncList_ == null) {
                return null;
            }

            //? TODO: Enable sorted list, integrate in PDBProvider
            return FunctionDebugInfo.BinarySearch(sortedFuncList_, funcAddress);
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

        private IRTextFunction FindExternalFunction(long funcAddress, bool useLock = true) {
            try {
                if (useLock) {
                    lock_.EnterReadLock();
                }

                if (externalsFuncMap_ == null) {
                    return null;
                }

                if (externalsFuncMap_.TryGetValue(funcAddress, out var externalFunc)) {
                    return externalFunc;
                }

                return null;
            }
            finally {
                if (useLock) {
                    lock_.ExitReadLock();
                }
            }
        }

        public IRTextFunction AddPlaceholderFunction(string name, long funcAddress) {
            if (ModuleDocument == null) {
                return null;
            }

            try {
                // Search again under the lock.
                lock_.EnterWriteLock();
                var func = FindExternalFunction(funcAddress, false);

                if (func != null) {
                    return func;
                }

                func = ModuleDocument.AddDummyFunction(name);
                externalsFuncMap_ ??= new Dictionary<long, IRTextFunction>();
                externalsFuncMap_[funcAddress] = func;
                return func;
            }
            finally {
                lock_.ExitWriteLock();
            }
        }
    }
}