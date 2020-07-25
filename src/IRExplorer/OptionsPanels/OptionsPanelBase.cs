﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Windows;
using System.Windows.Controls;

namespace IRExplorer.OptionsPanels {
    public interface IOptionsPanel {
        event EventHandler PanelClosed;
        event EventHandler PanelReset;
        event EventHandler SettingsChanged;
        event EventHandler<bool> StayOpenChanged;

        void Initialize(FrameworkElement parent);
        void PanelClosing();
        void PanelResetting();
        void PanelResetted();
        object Settings { get; set; }
    }

    public class OptionsPanelBase : UserControl, IOptionsPanel {
        private bool initialized_;

        public FrameworkElement Parent { get; set; }
        public event EventHandler PanelClosed;
        public event EventHandler PanelReset;
        public event EventHandler SettingsChanged;
        public event EventHandler<bool> StayOpenChanged;

        public virtual void Initialize(FrameworkElement parent) {
            Parent = parent;
            initialized_ = true;
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

        public void RaiseStayOpenChanged(bool staysOpen) {
            StayOpenChanged?.Invoke(this, staysOpen);
        }

        public virtual void PanelClosing() { }
        public virtual void PanelResetting() { }
        public virtual void PanelResetted() { }

        public virtual void OnSettingsChanged(object newSettings) {

        }

        public object Settings {
            get => DataContext;
            set {
                if (DataContext != value) {
                    DataContext = value; //? TODO: Should first set to null and remove all Settings = null

                    if (value != null && initialized_) {
                        OnSettingsChanged(value);
                    }
                }
            }
        }
    }
}
