// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation.Peers;

namespace Client {
    public class CustomAutomationPeer : FrameworkElementAutomationPeer {
        public CustomAutomationPeer(FrameworkElement owner) : base(owner) { }

        protected override string GetNameCore() {
            return "CustomAutomationPeer";
        }

        protected override AutomationControlType GetAutomationControlTypeCore() {
            return AutomationControlType.Window;
        }

        protected override List<AutomationPeer> GetChildrenCore() {
            return new List<AutomationPeer>();
        }
    }
}
