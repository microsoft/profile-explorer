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
using System.Windows.Shapes;

namespace IRExplorer.OptionsPanels {
    public partial class OptionsPanelHostWindow : Window, IOptionsPanel {
        private bool closed_;
        private IOptionsPanel optionsPanel_;

        public OptionsPanelHostWindow(UserControl panel, Point position, double width, double height, UIElement referenceElement) {
            InitializeComponent();
            Loaded += OptionsPanelHostWindow_Loaded;
            Unloaded += OptionsPanelHostWindow_Unloaded;

            var screenPosition = Utils.CoordinatesToScreen(position, referenceElement);
            Left = screenPosition.X;
            Top = screenPosition.Y;
            Width = width; ;
            Height = height;

            optionsPanel_ = (IOptionsPanel)panel;
            optionsPanel_.PanelClosed += SettingsPanel_PanelClosed;
            optionsPanel_.PanelReset += SettingsPanel_PanelReset;
            optionsPanel_.SettingsChanged += SettingsPanel_SettingsChanged;
            PanelHost.Content = panel;
        }

        private void OptionsPanelHostWindow_Unloaded(object sender, RoutedEventArgs e) {
            optionsPanel_.PanelClosed -= SettingsPanel_PanelClosed;
            optionsPanel_.PanelReset -= SettingsPanel_PanelReset;
            optionsPanel_.SettingsChanged -= SettingsPanel_SettingsChanged;
        }

        private void OptionsPanelHostWindow_Loaded(object sender, RoutedEventArgs e) {
            optionsPanel_.Initialize();
            Activate();
        }

        protected override void OnDeactivated(EventArgs e) {
            base.OnDeactivated(e);

            if (!closed_) {
                closed_ = true;
                PanelClosed?.Invoke(this, e);
            }
        }

         public void Initialize() {

        }

        private void SettingsPanel_SettingsChanged(object sender, EventArgs e) {
            SettingsChanged?.Invoke(this, e);
        }

        private void SettingsPanel_PanelReset(object sender, EventArgs e) {
            PanelReset?.Invoke(this, e);
        }

        private void SettingsPanel_PanelClosed(object sender, EventArgs e) {
            closed_ = true;
            PanelClosed?.Invoke(this, e);
        }

        public object Settings {
            get => optionsPanel_.Settings;
            set => optionsPanel_.Settings = value;
        }

        public event EventHandler PanelClosed;
        public event EventHandler PanelReset;
        public event EventHandler SettingsChanged;

        public void PanelClosing() { }
        public void PanelResetting() { }
        public void PanelResetted() { }

        private void ResetButton_Click(object sender, RoutedEventArgs e) {
            optionsPanel_.PanelResetting();
            PanelReset?.Invoke(this, e);
            optionsPanel_.PanelResetted();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) {
            closed_ = true;
            optionsPanel_.PanelClosing();
            PanelClosed?.Invoke(this, e);
        }
    }
}
