// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Windows.Media;
using Core;

namespace Client {
    public class MarkedSectionName {
        public string Text { get; set; }
        public TextSearchKind SearchKind { get; set; }
        public Brush TextColor { get; set; }

        public MarkedSectionName() { }

        public MarkedSectionName(string text, TextSearchKind searchKind) {
            Text = text;
            SearchKind = searchKind;
        }
    }

    public interface IStyleProvider {
        bool IsMarkedSection(IRTextSection section, out MarkedSectionName result);
    }
}
