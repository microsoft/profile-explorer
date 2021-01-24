// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Windows;
using System.Windows.Controls;
using IRExplorerUI.Controls;

namespace IRExplorerUI.OptionsPanels {
    public interface IOptionsPanel {
        event EventHandler PanelClosed;
        event EventHandler PanelReset;
        event EventHandler <bool> SettingsChanged;
        event EventHandler<bool> StayOpenChanged;

        void Initialize(FrameworkElement parent);
        void PanelClosing();
        void PanelResetting();
        void PanelResetted();
        SettingsBase Settings { get; set; }
    }

    public class OptionsPanelBase : UserControl, IOptionsPanel {
        private bool initialized_;

        public FrameworkElement Parent { get; set; }
        public event EventHandler PanelClosed;
        public event EventHandler PanelReset;
        public event EventHandler<bool> SettingsChanged;
        public event EventHandler<bool> StayOpenChanged;

        public virtual void Initialize(FrameworkElement parent) {
            Parent = parent;
            initialized_ = true;
        }

        public Point ParentPosition {
            get {
                if(Parent is DraggablePopup popup) {
                    return new Point(popup.HorizontalOffset, popup.VerticalOffset);
                }

                var window = Window.GetWindow(this);
                return window != null ? new Point(window.Left, window.Top) : new Point();
            }
        }

        public void RaisePanelClosed(EventArgs e) {
            PanelClosed?.Invoke(this, e);
        }

        public void RaisePanelReset(EventArgs e) {
            PanelReset?.Invoke(this, e);
        }

        public void RaiseSettingsChanged(bool force = false) {
            SettingsChanged?.Invoke(this, force);
        }

        public void RaiseStayOpenChanged(bool staysOpen) {
            StayOpenChanged?.Invoke(this, staysOpen);
        }

        public virtual void PanelClosing() { }
        public virtual void PanelResetting() { }
        public virtual void PanelResetted() { }

        public virtual void OnSettingsChanged(SettingsBase newSettings) {

        }

        public SettingsBase Settings {
            get => (SettingsBase)DataContext;
            set {
                if (DataContext != value) {
                    DataContext = value;
                    
                    if (value != null && initialized_) {
                        OnSettingsChanged(value);
                    }
                }
            }
        }
    }
}
