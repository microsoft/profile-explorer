// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using CoreLib;

namespace Client {
    /// <summary>
    ///     Interaction logic for SectionPanelPair.xaml
    /// </summary>
    public partial class SectionPanelPair : ToolPanelControl {
        private bool diffModeEnabled_;

        public SectionPanelPair() {
            InitializeComponent();
            DiffPanel.Visibility = Visibility.Collapsed;
            MainGrid.ColumnDefinitions[1].Width = new GridLength(0, GridUnitType.Pixel);
            MainGrid.ColumnDefinitions[2].Width = new GridLength(0, GridUnitType.Pixel);
            MainPanel.OpenSection += MainPanel_OpenSection;
            MainPanel.EnterDiffMode += MainPanel_EnterDiffMode;
            MainPanel.FunctionSwitched += DiffPanel_FunctionSwitched;
            MainPanel.SectionListScrollChanged += MainPanel_SectionListScrollChanged;
            DiffPanel.OpenSection += MainPanel_OpenSection;
            DiffPanel.EnterDiffMode += MainPanel_EnterDiffMode;
            DiffPanel.FunctionSwitched += DiffPanel_FunctionSwitched;
            DiffPanel.SectionListScrollChanged += MainPanel_SectionListScrollChanged;
        }

        public ICompilerInfoProvider CompilerInfo {
            get => MainPanel.CompilerInfo;
            set {
                MainPanel.CompilerInfo = value;
                DiffPanel.CompilerInfo = value;
            }
        }

        public bool HasAnnotatedSections => MainPanel.HasAnnotatedSections || DiffPanel.HasAnnotatedSections;

        public IRTextSummary MainSummary {
            get => MainPanel.Summary;
            set {
                if (!diffModeEnabled_) {
                    MainPanel.Summary = value;
                }
            }
        }

        public IRTextSummary DiffSummary {
            get => DiffPanel.Summary;
            set {
                if (DiffPanel.Summary != value) {
                    DiffPanel.Summary = value;

                    if (!diffModeEnabled_) {
                        MainGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
                        MainGrid.ColumnDefinitions[1].Width = new GridLength(2, GridUnitType.Pixel);
                        MainGrid.ColumnDefinitions[2].Width = new GridLength(1, GridUnitType.Star);
                        MainPanel.IsDiffModeEnabled = true;
                        DiffPanel.IsDiffModeEnabled = true;

                        //MainPanel.IsFunctionListVisible = false;
                        DiffPanel.Visibility = Visibility.Visible;
                        MainPanel.OtherSummary = value;

                        if (MainPanel.CurrentFunction != null) {
                            SwitchPanelDiffFunction(MainPanel.CurrentFunction, DiffPanel);
                        }

                        diffModeEnabled_ = true;
                    }
                    else if (value == null) {
                        MainGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
                        MainGrid.ColumnDefinitions[1].Width = new GridLength(0, GridUnitType.Pixel);
                        MainGrid.ColumnDefinitions[2].Width = new GridLength(0, GridUnitType.Star);
                        MainPanel.IsDiffModeEnabled = false;
                        DiffPanel.IsDiffModeEnabled = false;
                        MainPanel.IsFunctionListVisible = true;
                        DiffPanel.Visibility = Visibility.Collapsed;

                        // Restore original section list, without diff placeholders/annotations.
                        MainPanel.Sections = MainPanel.CreateSectionsExtension();
                        diffModeEnabled_ = false;
                    }
                }
            }
        }

        public string MainTitle {
            get => MainPanel.DocumentTitle;
            set => MainPanel.DocumentTitle = value;
        }

        public string DiffTitle {
            get => DiffPanel.DocumentTitle;
            set => DiffPanel.DocumentTitle = value;
        }

        public override ToolPanelKind PanelKind => ToolPanelKind.Section;
        public override bool SavesStateToFile => true;

        public override ISessionManager Session {
            get => MainPanel.Session;
            set => MainPanel.Session = value;
        }

        private void MainPanel_SectionListScrollChanged(object sender, double offset) {
            var panel = sender as SectionPanel;
            var otherPanel = panel == MainPanel ? DiffPanel : MainPanel;

            if (otherPanel.Summary != null && diffModeEnabled_) {
                otherPanel.ScrollSectionList(offset);
            }
        }

        private void DiffPanel_FunctionSwitched(object sender, IRTextFunction func) {
            var panel = sender as SectionPanel;
            var otherPanel = panel == MainPanel ? DiffPanel : MainPanel;

            if (otherPanel.Summary != null && diffModeEnabled_) {
                SwitchPanelDiffFunction(func, otherPanel);
            }
        }

        private void SwitchPanelDiffFunction(IRTextFunction func, SectionPanel otherPanel) {
            var otherFunc = otherPanel.Summary.FindFunction(func.Name);

            if (otherFunc != null && otherPanel.SelectFunction(otherFunc)) {
                ComputePanelSectionDiff();
            }
        }

        private void MainPanel_EnterDiffMode(object sender, DiffModeEventArgs e) {
            if (e.IsWithOtherDocument) {
                var panel = sender as SectionPanel;
                var otherPanel = panel == MainPanel ? DiffPanel : MainPanel;
                var otherSection = FindDiffDocumentSection(e.Left.Section);

                if (panel == MainPanel) {
                    e.Left.OpenKind = OpenSectionKind.ReplaceLeft;
                    e.Right = new OpenSectionEventArgs(otherSection, OpenSectionKind.ReplaceRight);
                }
                else {
                    e.Right = e.Left;
                    e.Right.OpenKind = OpenSectionKind.ReplaceRight;
                    e.Left = new OpenSectionEventArgs(otherSection, OpenSectionKind.ReplaceLeft);
                }
            }

            EnterDiffMode?.Invoke(sender, e);
        }

        private void MainPanel_OpenSection(object sender, OpenSectionEventArgs e) {
            if (e.OpenKind == OpenSectionKind.ReplaceCurrent && diffModeEnabled_) {
                if (sender == MainPanel) {
                    e.OpenKind = OpenSectionKind.ReplaceLeft;
                }
                else if (sender == DiffPanel) {
                    e.OpenKind = OpenSectionKind.ReplaceRight;
                }
            }

            OpenSection?.Invoke(sender, e);
        }

        private async void ComputePanelSectionDiff() {
            var baseExtList = MainPanel.CreateSectionsExtension();
            var diffExtList = DiffPanel.CreateSectionsExtension();
            var (baseList, diffList) = ComputeSectionNameListDiff(baseExtList, diffExtList);
            MainPanel.Sections = baseList;
            DiffPanel.Sections = diffList;

            //? TODO: Cache the diffs
            var results = await ComputeSectionIRDiffs(baseList, diffList);
            HasDiffResult firstDiffResult = null;

            foreach (var result in results) {
                if (result.HasDiffs) {
                    var baseSection = MainPanel.GetSectionExtension(result.LeftSection);
                    var diffSection = DiffPanel.GetSectionExtension(result.RightSection);
                    baseSection.SectionDiffKind = DiffKind.Modification;
                    diffSection.SectionDiffKind = DiffKind.Modification;
                    firstDiffResult ??= result;
                }
            }

            // Scroll to the first diff.
            if (firstDiffResult != null) {
                MainPanel.SelectSection(firstDiffResult.LeftSection);
                DiffPanel.SelectSection(firstDiffResult.RightSection);
            }
        }

        public void SetSectionAnnotationState(IRTextSection section, bool hasAnnotations) {
            SelectSectionPanel(section).SetSectionAnnotationState(section, hasAnnotations);
        }

        public void SelectSection(IRTextSection section, bool focus = true) {
            SelectSectionPanel(section).SelectSection(section, focus);
        }

        public void SelectSectionFunction(IRTextSection section) {
            MainPanel.SelectFunction(section.ParentFunction);
            DiffPanel.SelectFunction(section.ParentFunction);
        }

        public void SwitchToSection(IRTextSection section, IRDocumentHost targetDocument = null) {
            SelectSectionPanel(section).SwitchToSection(section, targetDocument);
        }

        private SectionPanel SelectSectionPanel(IRTextSection section) {
            var summary = section.ParentFunction.ParentSummary;

            if (MainPanel.Summary == summary) {
                return MainPanel;
            }
            else if (DiffPanel.Summary == summary) {
                return DiffPanel;
            }

            return null;
        }

        public IRTextSection FindDiffDocumentSection(IRTextSection section) {
            if (!diffModeEnabled_) {
                return null;
            }

            var panel = SelectSectionPanel(section);
            var otherPanel = panel == MainPanel ? DiffPanel : MainPanel;
            int sectionIndex = panel.Sections.FindIndex(item => item.Section == section);
            var otherSection = otherPanel.Sections[sectionIndex];

            if (!otherSection.IsPlaceholderDiff) {
                return otherSection.Section;
            }

            while (sectionIndex > 0) {
                sectionIndex--;
                otherSection = otherPanel.Sections[sectionIndex];

                if (!otherSection.IsPlaceholderDiff) {
                    return otherSection.Section;
                }
            }

            return null;
        }

        public event EventHandler<OpenSectionEventArgs> OpenSection;
        public event EventHandler<DiffModeEventArgs> EnterDiffMode;

        private (List<IRTextSectionExtension>, List<IRTextSectionExtension>) ComputeSectionNameListDiff(
            List<IRTextSectionExtension> baseList, List<IRTextSectionExtension> diffList) {
            var m = new int[baseList.Count + 1, diffList.Count + 1];
            int x;
            int y;

            for (x = 1; x <= baseList.Count; x++) {
                for (y = 1; y <= diffList.Count; y++) {
                    var baseSection = baseList[x - 1];
                    var diffSection = diffList[y - 1];

                    if (baseSection.Name == diffSection.Name) {
                        m[x, y] = m[x - 1, y - 1] + 1;
                    }
                    else {
                        m[x, y] = Math.Max(m[x, y - 1], m[x - 1, y]);
                    }
                }
            }

            var newBaseList = new List<IRTextSectionExtension>(baseList.Count);
            var newDiffList = new List<IRTextSectionExtension>(diffList.Count);
            x = baseList.Count;
            y = diffList.Count;

            while (!(x == 0 && y == 0)) {
                if (x > 0 && y > 0) {
                    var baseSection = baseList[x - 1];
                    var diffSection = diffList[y - 1];

                    if (baseSection.Name == diffSection.Name) {
                        // Found in both diff and base.
                        newBaseList.Add(baseSection);
                        newDiffList.Add(diffSection);
                        x--;
                        y--;
                        continue;
                    }
                }

                if (y > 0 && (x == 0 || m[x, y - 1] >= m[x - 1, y])) {
                    // Found in diff, missing in base.
                    var diffSection = diffList[y - 1];

                    newDiffList.Add(
                        new IRTextSectionExtension(diffSection.Section, DiffKind.Insertion,
                                                   diffSection.Name));

                    newBaseList.Add(new IRTextSectionExtension(null, DiffKind.Placeholder, ""));
                    y--;
                }
                else if (x > 0 && (y == 0 || m[x, y - 1] < m[x - 1, y])) {
                    // Not found in diff, removed from base.
                    var baseSection = baseList[x - 1];

                    newBaseList.Add(
                        new IRTextSectionExtension(baseSection.Section, DiffKind.Deletion, baseSection.Name));

                    newDiffList.Add(new IRTextSectionExtension(null, DiffKind.Placeholder, ""));
                    x--;
                }
            }

            newBaseList.Reverse();
            newDiffList.Reverse();
            return (newBaseList, newDiffList);
        }

        private async Task<List<HasDiffResult>> ComputeSectionIRDiffs(
            List<IRTextSectionExtension> baseSections, List<IRTextSectionExtension> diffSections) {
            var comparedSections = new List<Tuple<IRTextSection, IRTextSection>>();

            for (int i = 0; i < baseSections.Count; i++) {
                if (baseSections[i].SectionDiffKind == DiffKind.None &&
                    diffSections[i].SectionDiffKind == DiffKind.None) {
                    comparedSections.Add(new Tuple<IRTextSection, IRTextSection>(
                                             baseSections[i].Section, diffSections[i].Section));
                }
            }

            //? TODO: Pass the LoadedDocument to the panel, not Summary.
            var baseLoader = Session.SessionState.FindDocument(MainPanel.Summary).Loader;
            var diffLoader = Session.SessionState.FindDocument(DiffPanel.Summary).Loader;
            var results = await DocumentDiff.ComputeSectionDiffs(comparedSections, baseLoader, diffLoader);
            return results;
        }

        public override void OnSessionStart() {
            MainPanel.OnSessionStart();
        }

        public override void OnSessionEnd() {
            DiffSummary = null;
            MainSummary = null;
        }
    }
}
