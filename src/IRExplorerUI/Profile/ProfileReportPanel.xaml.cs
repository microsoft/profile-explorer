using System;
using System.Collections.Generic;
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
using DocumentFormat.OpenXml.Office2010.ExcelAc;
using IRExplorerCore;

namespace IRExplorerUI.Profile {
    public partial class ProfileReportPanel : ToolPanelControl {
        ProfileDataReport report_;
        ISession session_;

        public ProfileReportPanel(ISession session) {
            InitializeComponent();
            session_ = session;
        }

        public void ShowReport(ProfileDataReport report) {
            report_ = report;
            DataContext = report;

            var modules = report.Modules;
            modules.Sort((a, b) => a.ImageFileInfo.ImageName.CompareTo(b.ImageFileInfo.ImageName));
            ModuleList.ItemsSource = new ListCollectionView(modules);

            if (report.RunningProcesses != null) {
                // Process list is already sorted by weight.
                ProcessList.ItemsSource = new ListCollectionView(report.RunningProcesses);
            }
        }

        public static void ShowReport(ProfileDataReport report, ISession session) {
            var panel = new ProfileReportPanel(session);
            panel.TitleSuffix = $"Profile report";

            var window = new Window();
            window.Content = panel;
            window.Title = "Profile report";
            window.WindowStyle = WindowStyle.ToolWindow;
            window.ResizeMode = ResizeMode.CanResizeWithGrip;
            window.Width = 800;
            window.Height = 600;
            window.Owner = App.Current.MainWindow;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            panel.ShowReport(report);
            window.Show();
        }
    }
}
