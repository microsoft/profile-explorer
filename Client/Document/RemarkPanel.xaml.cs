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
        public PassRemarkExtension(PassRemark remark, bool inCurrentSection) {
            Remark = remark;
            InCurrentSection = inCurrentSection;
        }

        PassRemark Remark { get; set; }
        bool InCurrentSection { get; set; }

        string Description => Remark.Section.Name;
        string Text => Remark.RemarkText;
    }

    public partial class RemarkPanel : UserControl, INotifyPropertyChanged
    {
        static readonly double RemarkListTop = 24;
        static readonly double RemarkPreviewHeight = 200;
        static readonly double RemarkListItemHeight = 20;
        static readonly double MaxRemarkListItems = 10;
        static readonly double MinRemarkListItems = 2;

        public RemarkPanel()
        {
            InitializeComponent();
            DataContext = this;
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
                    UpdateHeight();
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
                RemarkList.ItemsSource = null;

                var remarkTag = element_.GetTag<RemarkTag>();

                if(remarkTag != null)
                {
                    RemarkList.ItemsSource = new CollectionView(remarkTag.Remarks);
                    UpdateHeight();            
                }
            }
        }

        public void NotifyPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        void UpdateHeight()
        {
            Height = RemarkListTop + RemarkListItemHeight *
                        Math.Clamp(RemarkList.Items.Count, MinRemarkListItems, MaxRemarkListItems);
            if (ShowPreview) {
                Height += RemarkPreviewHeight;
            }
        }

        public FunctionIR Function { get; set; }
        public IRTextSection Section { get; set; }
        public ISessionManager Session { get; set; }

        private async void RemarkList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if(e.AddedItems.Count != 1)
            {
                return;
            }

            var item = e.AddedItems[0] as PassRemark;
            var outputText = await Session.GetSectionPassOutputAsync(item.Section.OutputBefore, item.Section);

            TextView.Text = outputText;
            TextView.ScrollToLine(item.RemarkLocation.Line);
            TextView.Select(item.RemarkLocation.Offset, item.RemarkText.Length);
            ShowPreview = true;
        }
    }
}
