using IRExplorerCore;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using IRExplorer.Document;

namespace IRExplorer.Panels {
    /// <summary>
    /// Interaction logic for SearchPanel.xaml
    /// </summary>
    public partial class DocumentSearchPanel : UserControl {
        class DocumentSearchInfo {
            public int FunctionCount { get; set; }
            public int SectionCount { get; set; }
            public int InstanceCount { get; set; }
            public long Duration { get; set; }
            public ListCollectionView SectionList { get; set; }
        }

        private ISessionManager session_;
        private LoadedDocument document_;
        private CancelableTaskInfo searchTask_;
        private SearchInfo searchInfo_;

        public DocumentSearchPanel(ISessionManager session, LoadedDocument document) {
            InitializeComponent();
            session_ = session;
            document_ = document;
            SetupSearchPanel();
            SetupResultsPanel();
        }

        private void SetupResultsPanel() {
            ResultsPanel.OpenSection += ResultsPanel_OpenSection;
        }

        private void SetupSearchPanel() {
            searchInfo_ = new SearchInfo() {
                ShowSearchAllButton = false,
                ShowShowNavigationnSection = false
            };

            SearchPanel.SearchChanged += SearchPanel_SearchChanged;
            SearchPanel.Show(searchInfo_);
        }

        private async void ResultsPanel_OpenSection(object sender, OpenSectionEventArgs e) {
            await session_.SwitchDocumentSection(e, null);
        }

        private async void SearchPanel_SearchChanged(object sender, SearchInfo e) {
            var searchedText = e.SearchedText;

            if (searchedText.Length < 2) {
                return;
            }

            var searchTask = new CancelableTaskInfo();

            lock (this) {
                if (searchTask_ != null) {
                    searchTask_.Cancel();
                }

                searchTask_ = searchTask;
            }

            var docInfo = document_;
            var searcherOptions = new SectionTextSearcherOptions() {
                SearchBeforeOutput = true,
                ///KeepSectionText = false,
                UseRawSectionText = true
            };

            var searcher = new SectionTextSearcher(docInfo.Loader, searcherOptions);
            var start = Stopwatch.StartNew();

            var list = new List<IRTextSection>();

            foreach (var func in docInfo.Summary.Functions) {
                list.AddRange(func.Sections);
            }

            
            var results = await searcher.SearchAsync(searchedText, TextSearchKind.Default, list, searchTask_);

            if (searchTask.IsCanceled) {
                return;
            }

            int sections = 0;
            int functions = 0;
            int instances = 0;
            var sectionList = new List<string>();
            
            foreach (var result in results) {
                if (result.Results.Count > 0) {
                    sectionList.Add(result.Section.Name);
                    sections++;
                }

                instances += result.Results.Count;
            }

            DataContext = new DocumentSearchInfo() {
                FunctionCount = functions,
                SectionCount = sections,
                InstanceCount = instances,
                SectionList = new ListCollectionView(sectionList),
                Duration = start.ElapsedMilliseconds
            };

            ResultsPanel.Session = session_;
            ResultsPanel.UpdateSearchResults(results, new SearchInfo());

            searchTask.Completed();

            lock (this) {
                if (searchTask_ == searchTask) {
                    searchTask_ = null;
                }
            }
        }

        private void Button_Click(object sender, RoutedEventArgs e) {
            var docInfo = document_;
            var list = new List<IRTextSection>();

            foreach (var func in docInfo.Summary.Functions) {
                list.AddRange(func.Sections);
            }

            int total = 0;
            var start = Stopwatch.StartNew();

            foreach (var section in list) {
                var text = docInfo.Loader.GetRawSectionText(section);
                var hash = CompressionUtils.CreateSHA256(text);
                total += hash.Length;
            }

            MessageBox.Show($"Done in {start.ElapsedMilliseconds}");
        }

    }
}
