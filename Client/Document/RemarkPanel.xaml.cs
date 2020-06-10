using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Core;
using Core.IR;

namespace Client.Document
{
    public class PassRemarkExtension {
        public PassRemarkExtension(PassRemark remark, string sectionName, bool inCurrentSection) {
            Remark = remark;
            InCurrentSection = inCurrentSection;
            SectionName = sectionName;
        }

        public PassRemark Remark { get; set; }
        public bool InCurrentSection { get; set; }
        public string SectionName { get; set; }

        public bool IsOptimization => Remark.Kind == RemarkKind.Optimization;
        public bool IsAnalysis => Remark.Kind == RemarkKind.Analysis;
        public string Description => SectionName;
        public string Text => $"({Remark.Section.Number}) {Remark.RemarkText}";
    }

    public partial class RemarkPanel : Window, INotifyPropertyChanged
    {
        static readonly double RemarkListTop = 48;
        static readonly double RemarkPreviewWidth = 700;
        static readonly double RemarkPreviewHeight = 200;
        static readonly double RemarkListItemHeight = 20;
        static readonly double MaxRemarkListItems = 10;
        static readonly double MinRemarkListItems = 3;

        RemarkFilterState remarkFilter_;
        public RemarkFilterState RemarkFilter
        {
            get => remarkFilter_;
            set => remarkFilter_ = value;
        }

        public RemarkPanel()
        {
            InitializeComponent();
            DataContext = this;

            remarkFilter_ = new RemarkFilterState();
            remarkFilter_.PropertyChanged += RemarkFilter__PropertyChanged;
            ToolbarPanel.DataContext = remarkFilter_;
        }

        private void RemarkFilter__PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            UpdateRemarkList();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        bool showPreview_;
        public bool ShowPreview
        {
            get => showPreview_;
            set
            {
                if(showPreview_ != value)
                {
                    showPreview_ = value;
                    NotifyPropertyChanged(nameof(ShowPreview));
                    UpdateSize();
                }
            }
        }

        IRElement element_;
        public IRElement Element
        {
            get => element_;
            set
            {
                if(element_ == value)
                {
                    return;
                }

                element_ = value;
                UpdateRemarkList();
            }
        }

        void UpdateRemarkList()
        {
            RemarkList.ItemsSource = null;
            var remarkTag = element_.GetTag<RemarkTag>();

            if (remarkTag != null)
            {
                var list = new List<PassRemarkExtension>();

                foreach (var remark in remarkTag.Remarks)
                {
                    if (remarkFilter_.IsAcceptedRemark(remark, Section))
                    {
                        var sectionName = Session.CompilerInfo.NameProvider.GetSectionName(remark.Section);
                        sectionName = $"({remark.Section.Number}) {sectionName}";
                        list.Add(new PassRemarkExtension(remark, sectionName, remark.Section == Section));
                    }
                }

                RemarkList.ItemsSource = list;
            }
        }

        public void NotifyPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        void UpdateSize()
        {
            Width = RemarkPreviewWidth;
            Height = RemarkListTop + RemarkListItemHeight *
                        Math.Clamp(RemarkList.Items.Count, MinRemarkListItems, MaxRemarkListItems);
            if (ShowPreview) {
                Height += RemarkPreviewHeight;
            }
        }

        public FunctionIR Function { get; set; }
        public IRTextSection Section { get; set; }
        public ISessionManager Session { get; set; }

        public void Initialize()
        {
            RemarkList.UnselectAll();
            SectionLabel.Content = "";
            ShowPreview = false;
            UpdateSize();
        }

        private async void RemarkList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

            if(e.AddedItems.Count != 1)
            {
                return;
            }

            var itemEx = e.AddedItems[0] as PassRemarkExtension;
            var item = itemEx.Remark;
            var outputText = await Session.GetSectionPassOutputAsync(item.Section.OutputBefore, item.Section);

            TextView.Text = outputText;
            TextView.ScrollToLine(item.RemarkLocation.Line);
            TextView.Select(item.RemarkLocation.Offset, item.RemarkText.Length);
            SectionLabel.Content = itemEx.Description;
            ShowPreview = true;
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {

        }
    }
}
