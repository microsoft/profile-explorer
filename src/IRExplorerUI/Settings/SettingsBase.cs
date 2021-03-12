// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;

namespace IRExplorerUI {
    public class SettingsBase {
        public virtual void Reset() { }
        public virtual void SwitchTheme(ThemeColorSet theme) { }

        public virtual SettingsBase Clone() {
            throw new NotImplementedException();
        }

        public virtual bool HasChanges(SettingsBase other) {
            return !other.Equals(this);
        }
    }
}