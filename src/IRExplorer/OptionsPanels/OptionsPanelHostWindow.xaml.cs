using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace IRExplorer.OptionsPanels {
    public partial class OptionsPanelHostWindow : Popup, IOptionsPanel {
        private bool closed_;
        private IOptionsPanel optionsPanel_;

        public OptionsPanelHostWindow(UserControl panel, Point position, double width, double height, UIElement referenceElement) {
            InitializeComponent();

            var screenPosition = Utils.CoordinatesToScreen(position, referenceElement);
            HorizontalOffset = screenPosition.X;
            VerticalOffset = screenPosition.Y;
            Width = width;
            Height = height;

            optionsPanel_ = (IOptionsPanel)panel;
            optionsPanel_.PanelClosed += SettingsPanel_PanelClosed;
            optionsPanel_.PanelReset += SettingsPanel_PanelReset;
            optionsPanel_.SettingsChanged += SettingsPanel_SettingsChanged;
            PanelHost.Content = panel;
        }

        protected override void OnOpened(EventArgs e) {
            base.OnOpened(e);
            optionsPanel_.Initialize();
        }

        protected override void OnClosed(EventArgs e) {
            base.OnClosed(e);

            optionsPanel_.PanelClosed -= SettingsPanel_PanelClosed;
            optionsPanel_.PanelReset -= SettingsPanel_PanelReset;
            optionsPanel_.SettingsChanged -= SettingsPanel_SettingsChanged;

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
            //? TODO: Message box shows under panel!
            //StaysOpen = true; // Prevent popup from closing while showing message box.

            //if (MessageBox.Show("Do you want to reset all settings?", "IR Explorer",
            //    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) {
            //    return;
            //}

            //StaysOpen = false;
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
