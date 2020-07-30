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
using IRExplorer.OptionsPanels;

namespace IRExplorer.Sharing {
    public partial class SessionSharingPanel : OptionsPanelBase {
        public const double DefaultHeight = 90;
        public const double MinimumHeight = 90;
        public const double DefaultWidth = 300;
        public const double MinimumWidth = 100;

        public string SharingLink { get; set; }

        public SessionSharingPanel(string sharingLink) {
            InitializeComponent();
            SharingLink = sharingLink;
            DataContext = this;
        }

        public override void Initialize(FrameworkElement parent) {
            base.Initialize(parent);
            SharingLinkTextBox.SelectAll();
            SharingLinkTextBox.Focus();
            Keyboard.Focus(SharingLinkTextBox);
        }

    }
}
