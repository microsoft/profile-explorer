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
            ModuleList.ItemsSource = new ListCollectionView(report.Modules);
        }

        public static void ShowReport(ProfileDataReport report, ISession session) {
            var panel = new ProfileReportPanel(session);
            panel.TitleSuffix = $"Profile report";
            session.DisplayFloatingPanel(panel);
            panel.ShowReport(report);
        }
    }
}
