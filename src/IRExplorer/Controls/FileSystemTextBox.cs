// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace IRExplorer.Controls {
    class FileSystemTextBox : AutoCompleteBox {
        protected override void OnInitialized(EventArgs e) {
            base.OnInitialized(e);
            extensionFilter_ = "*.*";
            Populating += FileSystemTextBox_Populating;
        }

        private string extensionFilter_;

        public string ExtensionFilter { get => extensionFilter_; set => extensionFilter_ = value; }

        private void FileSystemTextBox_Populating(object sender, PopulatingEventArgs e) {
            var box = sender as FileSystemTextBox;
            string text = box.Text;

            try {
                string dirname = Path.GetDirectoryName(text);

                if (dirname != null && Directory.Exists(dirname)) {
                    var files = Directory.GetFiles(dirname, extensionFilter_, SearchOption.TopDirectoryOnly);
                    var dirs = Directory.GetDirectories(dirname, "*", SearchOption.TopDirectoryOnly);
                    var candidates = new List<string>(files.Length + dirs.Length);

                    foreach (string f in dirs) {
                        candidates.Add(f);
                    }

                    foreach (string f in files) {
                        candidates.Add(f);
                    }

                    box.ItemsSource = candidates;
                    box.PopulateComplete();
                    return;
                }
            }
            catch { }

            box.ItemsSource = null;
            box.PopulateComplete();
        }
    }
}
