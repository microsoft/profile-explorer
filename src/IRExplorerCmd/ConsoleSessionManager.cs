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

namespace IRExplorerCmd {
    public class ConsoleCompilerInfoProvider : ICompilerInfoProvider {
        public ConsoleCompilerInfoProvider(string irName, ICompilerIRInfo irInfo) {
            CompilerIRName = irName;
            IR = irInfo;
        }

        public string CompilerIRName { get; set; }
        public ICompilerIRInfo IR { get; set; }

        public INameProvider NameProvider => throw new NotImplementedException();
        public ISectionStyleProvider SectionStyleProvider => throw new NotImplementedException();
        public IRRemarkProvider RemarkProvider => throw new NotImplementedException();

        public bool AnalyzeLoadedFunction(FunctionIR function) {
            throw new NotImplementedException();
        }

        public IDiffOutputFilter CreateDiffOutputFilter() {
            throw new NotImplementedException();
        }

        public IBlockFoldingStrategy CreateFoldingStrategy(FunctionIR function) {
            throw new NotImplementedException();
        }

        public List<QueryDefinition> BuiltinQueries => new List<QueryDefinition>() { };
        public List<FunctionTaskDefinition> BuiltinFunctionTasks => throw new NotImplementedException();
    }

    public class ConsoleSessionManager : ISession {
        ICompilerInfoProvider compilerInfo_;
        SessionStateManager sessionState_;
        LoadedDocument mainDocument_;
        LoadedDocument diffDocument_;

        public ConsoleSessionManager(ICompilerInfoProvider compilerInfo) {
            compilerInfo_ = compilerInfo;
            sessionState_ = new SessionStateManager("", SessionKind.Default);
        }

        public bool LoadMainDocument(string path) {
            mainDocument_ = LoadDocument(path);
            return mainDocument_ != null;
        }

        public bool LoadDiffDocument(string path) {
            diffDocument_ = LoadDocument(path);
            return diffDocument_ != null;
        }

        private LoadedDocument LoadDocument(string path) {
            var result = new LoadedDocument(path, Guid.NewGuid());
            result.Loader = new DocumentSectionLoader(path, CompilerInfo.IR);
            result.Summary = result.Loader.LoadDocument(null);
            sessionState_.RegisterLoadedDocument(result);
            return result;
        }

        public ICompilerInfoProvider CompilerInfo => compilerInfo_;
        public bool IsInDiffMode => false;
        public bool IsInTwoDocumentsDiffMode => diffDocument_ != null;
        public IRTextSummary MainDocumentSummary => mainDocument_?.Summary;
        public IRTextSummary DiffDocumentSummary => diffDocument_?.Summary;

        public Task<string> GetSectionTextAsync(IRTextSection section, IRDocument targetDiffDocument = null) {
            var docInfo = sessionState_.FindLoadedDocument(section);
            return Task.Run(() => docInfo.Loader.GetSectionText(section));
        }

        public IRDocument CurrentDocument => throw new NotImplementedException();
        public IRTextSection CurrentDocumentSection => throw new NotImplementedException();
        public List<IRDocument> OpenDocuments => throw new NotImplementedException();
        public SessionStateManager SessionState => throw new NotImplementedException();

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

        public Task<string> GetSectionPassOutputAsync(IRPassOutput output, IRTextSection section) {
            throw new NotImplementedException();
        }

        public object LoadDocumentState(IRTextSection section) {
            throw new NotImplementedException();
        }

        public object LoadPanelState(IToolPanel panel, IRTextSection section) {
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

        public void SavePanelState(object stateObject, IToolPanel panel, IRTextSection section) {
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

        public bool SwitchToNextSection(IRTextSection section, IRDocument document) {
            throw new NotImplementedException();
        }

        public bool SwitchToPreviousSection(IRTextSection section, IRDocument document) {
            throw new NotImplementedException();
        }

        public void UnregisterDetachedPanel(DraggablePopup panel) {
            throw new NotImplementedException();
        }

        public void LoadDocumentQuery(QueryDefinition query, IRDocument document) {
            throw new NotImplementedException();
        }
    }
}
