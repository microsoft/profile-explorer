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
using IRExplorerUI.Document;
using System.Windows.Controls.Primitives;
using System.ComponentModel;

namespace IRExplorerUI.Panels {
    /// <summary>
    /// Interaction logic for SearchPanel.xaml
    /// </summary>
    public partial class DocumentSearchPanel : DraggablePopup {
        class DocumentSearchInfo : INotifyPropertyChanged {
            private bool panelDetached_;

            public bool SearchAllFunctions { get; set; }
            public bool SearchPassOutput { get; set; }
            public int FunctionCount { get; set; }
            public int SectionCount { get; set; }
            public int InstanceCount { get; set; }
            public long Duration { get; set; }

            public bool IsPanelDetached {
                get => panelDetached_;
                set {
                    if (panelDetached_ != value) {
                        panelDetached_ = value;
                        NotifyPropertyChanged(nameof(IsPanelDetached));
                    }
                }
            }

            public event PropertyChangedEventHandler PropertyChanged;

            public void NotifyPropertyChanged(string propertyName) {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        private ISessionManager session_;
        private LoadedDocument document_;
        private CancelableTask searchTask_;
        private SearchInfo searchInfo_;
        private DocumentSearchInfo data_;

        public DocumentSearchPanel(Point position, double width, double height,
                                   UIElement referenceElement, ISessionManager session, LoadedDocument document) {
            InitializeComponent();
            Initialize(position, width, height, referenceElement);
            PanelResizeGrip.ResizedControl = this;

            data_ = new DocumentSearchInfo() {
                SearchAllFunctions = true,
                SearchPassOutput = true
            };

            DataContext = data_;
            session_ = session;
            document_ = document;
            SetupSearchPanel();
            SetupResultsPanel();
        }

        private void SetupResultsPanel() {
            ResultsPanel.HideToolbarTray = true;
            ResultsPanel.HideSearchedText = true;
            ResultsPanel.OpenSection += ResultsPanel_OpenSection;
        }

        private void SetupSearchPanel() {
            searchInfo_ = new SearchInfo() {
                ShowSearchAllButton = false,
                ShowNavigationnSection = false
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
                ResultsPanel.ClearSearchResults();
                ResultsPanel.OptionalText = "";
                return;
            }

            var results = await UpdateSearchPanel(searchedText);

            if (results != null) {
                // Update UI if search was not cancelled.
                ResultsPanel.Session = session_;
                ResultsPanel.UpdateSearchResults(results, new SearchInfo());

                // Update result details.
                var functions = new HashSet<IRTextFunction>();
                int sectionCount = 0;

                foreach (var result in results) {
                    if (result.Results.Count > 0) {
                        sectionCount++;
                        functions.Add(result.Section.ParentFunction);
                    }
                }

                ResultsPanel.OptionalText = $"Functions: {functions.Count}        Sections: {sectionCount}";
            }
        }

        private async Task<List<SectionSearchResult>> UpdateSearchPanel(string searchedText) {
            // Create a task that can be used later to cancel the search
            // if another letter is being pressed.
            var searchTask = new CancelableTask();

            lock (this) {
                if (searchTask_ != null) {
                    searchTask_.Cancel();
                }

                searchTask_ = searchTask;
            }

            var docInfo = document_;
            var searcherOptions = new SectionTextSearcherOptions() {
                SearchBeforeOutput = data_.SearchPassOutput,
                KeepSectionText = false, // Reduces memory usage for large files.
                UseRawSectionText = true // Speeds up reading large sections.
            };

            var searcher = new SectionTextSearcher(docInfo.Loader, searcherOptions);
            var list = new List<IRTextSection>();

            foreach (var func in docInfo.Summary.Functions) {
                list.AddRange(func.Sections);
            }

            // Start the search on another thread.,
            var results = await searcher.SearchAsync(searchedText, searchInfo_.SearchKind, list, searchTask_);

            if (searchTask.IsCanceled) {
                return null;
            }

            searchTask.Completed();

            lock (this) {
                if (searchTask_ == searchTask) {
                    searchTask_ = null;
                }
            }

            return results;
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

        private void DetachPanel() {
            if (IsDetached) {
                return;
            }

            DetachPopup();
            StaysOpen = true;
            data_.IsPanelDetached = true;
        }

        public override bool ShouldStartDragging() {
            if (SearchPanelHost.IsMouseOver) {
                DetachPanel();
                return true;
            }

            return false;
        }

        private void PinPanelButton_Click(object sender, RoutedEventArgs e) {
            DetachPanel();
        }

        private void ClosePanelButton_Click(object sender, RoutedEventArgs e) {
            ClosePopup();
        }
    }
}
