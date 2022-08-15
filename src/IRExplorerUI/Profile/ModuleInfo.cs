using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
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
        private BinaryFileDescriptor binaryInfo_;
        private ProfileDataProviderOptions options_;
        public List<DebugFunctionInfo> sortedFuncList_;
        private Dictionary<long, IRTextFunction> addressFuncMap_;
        private Dictionary<long, string> externalsFuncMap_;
        private Dictionary<string, IRTextFunction> externalFuncNames_;
        private ProfileDataProviderReport report_;

        public IRTextSummary Summary { get; set; }
        public LoadedDocument ModuleDocument { get; set; }
        public IDebugInfoProvider DebugInfo { get; set; }

        public bool HasDebugInfo { get; set; }
        public bool Initialized { get; set; }

        //? TODO: Needed only for inlinee samples
        public Dictionary<string, IRTextFunction> unmangledFuncNamesMap_;

        public ModuleInfo(ProfileDataProviderOptions options, ProfileDataProviderReport report,  ISession session) {
            options_ = options;
            report_ = report;
            session_ = session;
        }
        
        public async Task<bool> Initialize(BinaryFileDescriptor binaryInfo, SymbolFileSourceOptions options,
                                            IDebugInfoProvider debugInfo) {
            if (Initialized) {
                return true;
            }

            binaryInfo_ = binaryInfo;
            var imageName = binaryInfo.ImageName;
            Trace.WriteLine($"ModuleInfo init {imageName}");
            
            var binFile = await FindBinaryFilePath(options).ConfigureAwait(false);

            if (binFile == null || !binFile.Found) {
                Trace.TraceWarning($"  Could not find local path for image {imageName}");
                report_.AddModuleInfo(binaryInfo, binFile, ProfileDataProviderReport.LoadState.NotFound);
                return false;
            }

            bool isManagedImage = binFile.BinaryFile != null && binFile.BinaryFile.IsManagedImage;
            
            var loadedDoc = await session_.LoadBinaryDocument(binFile.FilePath, binaryInfo.ImageName, debugInfo).ConfigureAwait(false);
            
            if (loadedDoc == null) {
                Trace.TraceWarning($"  Failed to load document for image {imageName}");
                report_.AddModuleInfo(binaryInfo, binFile, ProfileDataProviderReport.LoadState.Failed);
                return false;
            }
            else {
                Trace.TraceWarning($"  Loaded document for image {imageName}");
                report_.AddModuleInfo(binaryInfo, binFile, ProfileDataProviderReport.LoadState.Loaded);
            }

            ModuleDocument = loadedDoc;
            Summary = loadedDoc.Summary;

            if (isManagedImage && debugInfo != null) {
                Trace.TraceInformation($"  Has managed debug {imageName}");
                DebugInfo = debugInfo;
                HasDebugInfo = await Task.Run(() => BuildAddressFunctionMap()).ConfigureAwait(false);
                loadedDoc.DebugInfo = debugInfo;
            }

            Trace.TraceInformation($"Initialized image {imageName}");
            Initialized = true;
            return true;
        }

        public async Task<bool> InitializeDebugInfo() {
            if (DebugInfo != null) {
                return HasDebugInfo;
            }

            DebugInfo = session_.CompilerInfo.CreateDebugInfoProvider(ModuleDocument.BinaryFile.FilePath);
            HasDebugInfo = await Task.Run(() => DebugInfo.LoadDebugInfo(ModuleDocument.DebugInfoFile)).ConfigureAwait(false);

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
            externalsFuncMap_ = new Dictionary<long, string>();
            externalFuncNames_ = new Dictionary<string, IRTextFunction>();
            sortedFuncList_ = new List<DebugFunctionInfo>();

            Trace.WriteLine($"Building address mapping for {Summary.ModuleName}, PDB {ModuleDocument.DebugInfoFile}");

            foreach (var funcInfo in DebugInfo.EnumerateFunctions(false)) {
                //Trace.WriteLine($"{funcInfo.Name}, {funcInfo.RVA}");
                
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

        public async Task<BinaryFileSearchResult> FindBinaryFilePath(SymbolFileSourceOptions options) {
            // Use the symbol server to locate the image,
            // this will also attempt to download it if not found locally.
            return await session_.CompilerInfo.FindBinaryFile(binaryInfo_, options).ConfigureAwait(false);
        }
        
        public DebugFunctionInfo FindDebugFunctionInfo(long funcAddress) {
            if (!HasDebugInfo || sortedFuncList_ == null) {
                return null;
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
                textFunction = AddPlaceholderFunction(externalFuncName, funcAddress);
            }

            return textFunction;
        }

        public IRTextFunction AddPlaceholderFunction(string name, long funcAddress) {
            if (ModuleDocument == null) {
                return null;
            }

            var func = new IRTextFunction(name);
            var section = new IRTextSection(func, func.Name, IRPassOutput.Empty);
            func.AddSection(section);
            
            ModuleDocument.Summary.AddFunction(func);
            ModuleDocument.Summary.AddSection(section);
            externalFuncNames_ ??= new Dictionary<string, IRTextFunction>();
            externalsFuncMap_ ??= new Dictionary<long, string>();
            externalFuncNames_[name] = func;
            externalsFuncMap_[funcAddress] = name;
            HasDebugInfo = true;
            return func;
        }

        public void Dispose() {
        }
    }
}