using System;
using System.Windows.Controls;

namespace IRExplorer.OptionsPanels {
    public interface IOptionsPanel {
        event EventHandler PanelClosed;
        event EventHandler PanelReset;
        event EventHandler SettingsChanged;

        void Initialize();
        void PanelClosing();
        void PanelResetting();
        void PanelResetted();
        object Settings { get; set; }
    }

    public class OptionsPanelBase : UserControl, IOptionsPanel {
        public event EventHandler PanelClosed;
        public event EventHandler PanelReset;
        public event EventHandler SettingsChanged;

        public virtual void Initialize() {

        }

        public void RaisePanelClosed(EventArgs e) {
            PanelClosed?.Invoke(this, e);
        }

        public void RaisePanelReset(EventArgs e) {
            PanelReset?.Invoke(this, e);
        }

        public void RaiseSettingsChanged(EventArgs e) {
            SettingsChanged?.Invoke(this, e);
        }

        public virtual void PanelClosing() { }
        public virtual void PanelResetting() { }
        public virtual void PanelResetted() { }

        public object Settings {
            get => DataContext;
            set => DataContext = value; //? TODO: Should first set to null and remove all Settings = null
        }
    }
}
