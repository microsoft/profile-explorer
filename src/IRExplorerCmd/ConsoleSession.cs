using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using IRExplorerUI;
using IRExplorerUI.Diff;
using IRExplorerUI.Query;
using IRExplorerUI.Document;
using IRExplorerCore;
using IRExplorerCore.Analysis;
using IRExplorerCore.IR;
using IRExplorerUI.Controls;
using IRExplorerCore.Graph;
using IRExplorerUI.Compilers;
using IRExplorerUI.Profile;
using IRExplorerUI.Utilities;

namespace IRExplorerCmd {
    public class ConsoleCompilerInfoProvider : ICompilerInfoProvider {
        public ConsoleCompilerInfoProvider(string irName, ICompilerIRInfo irInfo) {
            CompilerIRName = irName;
            IR = irInfo;
        }

        public string CompilerIRName { get; set; }
        public string CompilerDisplayName { get; }
        public string DefaultSyntaxHighlightingFile { get; set; }
        public ICompilerIRInfo IR { get; set; }

        public INameProvider NameProvider => throw new NotImplementedException();
        public ISectionStyleProvider SectionStyleProvider => throw new NotImplementedException();
        public IRRemarkProvider RemarkProvider => throw new NotImplementedException();

        public bool AnalyzeLoadedFunction(FunctionIR function, IRTextSection section) {
            throw new NotImplementedException();
        }

        public Task HandleLoadedSection(IRDocument document, FunctionIR function, IRTextSection section) {
            return null;
        }

        public void HandleLoadedDocument(LoadedDocument document, string modulePath) {
        }

        public IDiffInputFilter CreateDiffInputFilter() {
            return null;
        }

        public IDiffOutputFilter CreateDiffOutputFilter() {
            throw new NotImplementedException();
        }

        public IDebugInfoProvider CreateDebugInfoProvider() {
            return null;
        }

        public string FindDebugInfoFile(string modulePath) {
            return null;
        }

        public IBlockFoldingStrategy CreateFoldingStrategy(FunctionIR function) {
            throw new NotImplementedException();
        }

        public string OpenDebugFileFilter { get; }

        public void ReloadSettings() {
            throw new NotImplementedException();
        }

        public List<QueryDefinition> BuiltinQueries => new List<QueryDefinition>() { };
        public List<FunctionTaskDefinition> BuiltinFunctionTasks => throw new NotImplementedException();
        public List<FunctionTaskDefinition> ScriptFunctionTasks => throw new NotImplementedException();
        public string OpenFileFilter { get; }

        public ISession Session { get; set; }
    }

    public class ConsoleSession : ISession, IDisposable {
        ICompilerInfoProvider compilerInfo_;
        SessionStateManager sessionState_;
        LoadedDocument mainDocument_;
        LoadedDocument diffDocument_;

        public ConsoleSession(ICompilerInfoProvider compilerInfo) {
            compilerInfo_ = compilerInfo;
            sessionState_ = new SessionStateManager("", SessionKind.FileSession);
        }

        public bool LoadMainDocument(string path, bool useCache = true) {
            mainDocument_ = LoadDocument(path, useCache);
            return mainDocument_ != null;
        }

        public bool LoadDiffDocument(string path, bool useCache = true) {
            diffDocument_ = LoadDocument(path, useCache);
            return diffDocument_ != null;
        }

        private LoadedDocument LoadDocument(string path, bool useCache) {
            var result = new LoadedDocument(path, path, Guid.NewGuid());
            result.Loader = new DocumentSectionLoader(path, CompilerInfo.IR, useCache);
            result.Summary = result.Loader.LoadDocument(null);
            sessionState_.RegisterLoadedDocument(result);
            return result;
        }

        public ICompilerInfoProvider CompilerInfo => compilerInfo_;
        public bool IsInDiffMode => false;
        public bool IsInTwoDocumentsDiffMode => diffDocument_ != null;
        public DiffModeInfo DiffModeInfo => null;
        public IRTextSummary MainDocumentSummary => mainDocument_?.Summary;
        public IRTextSummary DiffDocumentSummary => diffDocument_?.Summary;
        public ETWProfileDataProvider ProfileData => throw new NotImplementedException();

        public Task<string> GetSectionTextAsync(IRTextSection section, IRDocument targetDiffDocument = null) {
            var docInfo = sessionState_.FindLoadedDocument(section);
            return Task.Run(() => docInfo.Loader.GetSectionText(section));
        }

        public Task<List<string>> GetSectionOutputTextLinesAsync(IRPassOutput output, IRTextSection section) {
            var docInfo = sessionState_.FindLoadedDocument(section);
            return Task.Run(() => docInfo.Loader.GetSectionOutputTextLines(output));
        }

        public Task<string> GetDocumentTextAsync(IRTextSummary summary) {
            throw new NotImplementedException();
        }

        public IRDocument CurrentDocument => throw new NotImplementedException();
        public IRTextSection CurrentDocumentSection => throw new NotImplementedException();
        public List<IRDocument> OpenDocuments => throw new NotImplementedException();
        public SessionStateManager SessionState => throw new NotImplementedException();

        public bool IsSessionStarted => throw new NotImplementedException();

        ProfileData ISession.ProfileData => throw new NotImplementedException();

        public void BindToDocument(IToolPanel panel, BindMenuItem args) {
            throw new NotImplementedException();
        }

        public void DuplicatePanel(IToolPanel panel, DuplicatePanelKind duplicateKind) {
            throw new NotImplementedException();
        }

        public void ShowAllReferences(IRElement element, IRDocument document) {
            throw new NotImplementedException();
        }

        public IRDocument FindAssociatedDocument(IToolPanel panel) {
            throw new NotImplementedException();
        }

        public IRDocumentHost FindAssociatedDocumentHost(IToolPanel panel) {
            throw new NotImplementedException();
        }

        public void ShowSSAUses(IRElement element, IRDocument document) {
            throw new NotImplementedException();
        }

        public IRTextSummary GetDocumentSummary(IRTextSection section) {
            throw new NotImplementedException();
        }

        public Task<string> GetDocumentTextAsync(IRTextSection section) {
            throw new NotImplementedException();
        }

        public Task<string> GetSectionOutputTextAsync(IRPassOutput output, IRTextSection section) {
            throw new NotImplementedException();
        }

        public object LoadDocumentState(IRTextSection section) {
            throw new NotImplementedException();
        }

        public object LoadPanelState(IToolPanel panel, IRTextSection section, IRDocument document) {
            throw new NotImplementedException();
        }

        public Task<bool> OpenSessionDocument(string filePath) {
            throw new NotImplementedException();
        }

        public void PopulateBindMenu(IToolPanel panel, BindMenuItemsArgs args) {
            throw new NotImplementedException();
        }

        public void RegisterDetachedPanel(DraggablePopup panel) {
            throw new NotImplementedException();
        }

        public void ReloadDocumentSettings(DocumentSettings newSettings, IRDocument document) {
            throw new NotImplementedException();
        }

        public void ReloadRemarkSettings(RemarkSettings newSettings, IRDocument document) {
            throw new NotImplementedException();
        }

        public void SaveDocumentState(object stateObject, IRTextSection section) {
            throw new NotImplementedException();
        }

        public void SavePanelState(object stateObject, IToolPanel panel, IRTextSection section, IRDocument document) {
            throw new NotImplementedException();
        }

        public Task<bool> SaveSessionDocument(string filePath) {
            throw new NotImplementedException();
        }

        public Task<SectionSearchResult> SearchSectionAsync(SearchInfo searchInfo, IRTextSection section, IRDocument document) {
            throw new NotImplementedException();
        }

        public void SetSectionAnnotationState(IRTextSection section, bool hasAnnotations) {
            throw new NotImplementedException();
        }

        public Task SwitchDocumentSectionAsync(OpenSectionEventArgs args, IRDocument document) {
            throw new NotImplementedException();
        }

        public Task SwitchGraphsAsync(GraphPanel flowGraphPanel, IRTextSection section, IRDocument document) {
            throw new NotImplementedException();
        }

        public Task<Graph> ComputeGraphAsync(GraphKind kind, IRTextSection section,
                                             IRDocument document, CancelableTask loadTask = null,
                                             object options = null) {
            throw new NotImplementedException();
        }

        public bool SwitchToNextSection(IRTextSection section, IRDocument document) {
            throw new NotImplementedException();
        }

        public bool SwitchToPreviousSection(IRTextSection section, IRDocument document) {
            throw new NotImplementedException();
        }

        public IRTextSection GetPreviousSection(IRTextSection section) {
            throw new NotImplementedException();
        }

        public IRTextSection GetNextSection(IRTextSection section) {
            throw new NotImplementedException();
        }

        public ParsedIRTextSection LoadAndParseSection(IRTextSection section) {
            throw new NotImplementedException();
        }

        public void UnregisterDetachedPanel(DraggablePopup panel) {
            throw new NotImplementedException();
        }

        public bool SaveFunctionTaskOptions(FunctionTaskInfo taskInfo, IFunctionTaskOptions options) {
            throw new NotImplementedException();
        }

        public IFunctionTaskOptions LoadFunctionTaskOptions(FunctionTaskInfo taskInfo) {
            throw new NotImplementedException();
        }

        #region IDisposable Support

        private bool disposed_;

        protected virtual void Dispose(bool disposing) {
            if (!disposed_) {
                mainDocument_ = null;
                diffDocument_ = null;
                sessionState_.EndSession();
                sessionState_.Dispose();
                sessionState_ = null;
                disposed_ = true;
            }
        }

        ~ConsoleSession() {
            Dispose(false);
        }

        public void Dispose() {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public void AddOtherSummary(IRTextSummary summary) {
        }

        public IRTextFunction FindFunctionWithId(int funcNumber, Guid summaryId) {
            return null;
        }

        public void DisplayFloatingPanel(IToolPanel panel) {
        }

        public Task SwitchActiveFunction(IRTextFunction function) {
            return null;
        }

        Task<LoadedDocument> ISession.OpenSessionDocument(string filePath) {
            return null;
        }

        public Task<bool> LoadProfileData(string profileFilePath, string binaryFilePath, ProfileDataProviderOptions options, ProfileLoadProgressHandler progressCallback, CancelableTask cancelableTask) {
            return null;
        }

        public void SetApplicationStatus(string text, string tooltip = "") {
        }

        public void SetApplicationProgress(bool visible, double percentage, string title = null) {
        }

        #endregion
    }
}
