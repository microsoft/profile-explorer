// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection.Metadata;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using IRExplorerCore;
using IRExplorerCore.Analysis;
using IRExplorerCore.IR;
using IRExplorerUI.OptionsPanels;
using ProtoBuf;

namespace IRExplorerUI {
    public static class ReferenceCommand {
        public static readonly RoutedUICommand JumpToReference =
            new RoutedUICommand("Untitled", "JumpToReference", typeof(ReferencesPanel));
        public static readonly RoutedUICommand CopyToClipboard =
            new RoutedUICommand("Untitled", "CopyToClipboard", typeof(ReferencesPanel));
        public static readonly RoutedUICommand MarkReference =
            new RoutedUICommand("Untitled", "MarkReference", typeof(ReferencesPanel));
        public static readonly RoutedUICommand UnmarkReference =
            new RoutedUICommand("Untitled", "UnmarkReference", typeof(ReferencesPanel));
    }

    public class ReferenceSummary {
        public int SSACount { get; set; }
        public int LoadCount { get; set; }
        public int StoreCount { get; set; }
        public int AddressCount { get; set; }
    }

    public class ReferenceEx {
        public ReferenceEx(int index, Reference info, TextBlock preview, string previewText) {
            Index = index;
            Info = info;
            Preview = preview;
            PreviewText = previewText;
        }

        public Reference Info { get; set; }
        public int Index { get; set; }
        public int Line => Info.Element.TextLocation.Line;
        public string Block => Utils.MakeBlockDescription(Info.Element.ParentBlock);

        public string Kind {
            get {
                return Info.Kind switch {
                    ReferenceKind.Address => "Address",
                    ReferenceKind.Load => "Load",
                    ReferenceKind.Store => "Store",
                    ReferenceKind.SSA => "SSA use",
                    _ => ""
                };
            }
        }

        public TextBlock Preview { get; }
        public string PreviewText { get; }
        public Brush TextColor { get; set; }
    }

    [ProtoContract]
    public class ReferencePanelState {
        [ProtoMember(1)]
        private IRElementReference elementRef_;
        [ProtoMember(2)]
        public bool IsFindAll;
        [ProtoMember(3)]
        public bool HasPinnedContent;
        [ProtoMember(4)]
        public ReferenceKind FilterKind;

        public IRElement Element {
            get => elementRef_;
            set => elementRef_ = value;
        }
    }

    public partial class ReferencesPanel : ToolPanelControl, INotifyPropertyChanged {
        private string documentText_;
        private IRElement element_;
        private ReferenceKind filterKind_;
        private bool ignoreNextElement_;
        private bool isFindAll_;

        private IRPreviewToolTip previewTooltip_;
        private List<ReferenceEx> referenceList_;
        private ListCollectionView referenceListView_;
        private ReferenceSummary referenceSummary_;
        private IRTextSection section_;
        private ReferenceSettings settings_;
        private OptionsPanelHostWindow optionsPanel_;
        private bool optionsPanelVisible_;

        public ReferencesPanel() {
            InitializeComponent();
            settings_ = App.Settings.ReferenceSettings;
            DataContext = this;
        }

        public ReferenceKind FilterKind {
            get => filterKind_;
            set {
                if (filterKind_ != value) {
                    filterKind_ = value;
                    OnPropertyChange(nameof(FilterKind));
                    OnPropertyChange(nameof(ShowLoad));
                    OnPropertyChange(nameof(ShowStore));
                    OnPropertyChange(nameof(ShowAddress));
                    OnPropertyChange(nameof(ShowSSA));
                    referenceListView_?.Refresh();
                }
            }
        }

        public ReferenceSummary ReferenceSummary {
            get => referenceSummary_;
            set {
                if (referenceSummary_ != value) {
                    referenceSummary_ = value;
                    OnPropertyChange(nameof(LoadCount));
                    OnPropertyChange(nameof(StoreCount));
                    OnPropertyChange(nameof(AddressCount));
                    OnPropertyChange(nameof(SSACount));
                }
            }
        }

        public int LoadCount => referenceSummary_ != null ? referenceSummary_.LoadCount : 0;
        public int StoreCount => referenceSummary_ != null ? referenceSummary_.StoreCount : 0;
        public int AddressCount => referenceSummary_ != null ? referenceSummary_.AddressCount : 0;
        public int SSACount => referenceSummary_ != null ? referenceSummary_.SSACount : 0;

        public bool ShowLoad {
            get => filterKind_.HasFlag(ReferenceKind.Load);
            set => SetFilterFlag(ReferenceKind.Load, value);
        }

        public bool ShowStore {
            get => filterKind_.HasFlag(ReferenceKind.Store);
            set => SetFilterFlag(ReferenceKind.Store, value);
        }

        public bool ShowAddress {
            get => filterKind_.HasFlag(ReferenceKind.Address);
            set => SetFilterFlag(ReferenceKind.Address, value);
        }

        public bool ShowSSA {
            get => filterKind_.HasFlag(ReferenceKind.SSA);
            set => SetFilterFlag(ReferenceKind.SSA, value);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public void OnPropertyChange(string propertyname) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyname));
        }

        public IRElement Element {
            get => element_;
            set {
                if (ignoreNextElement_) {
                    ignoreNextElement_ = false;
                    return;
                }

                if (!(value is OperandIR)) {
                    ResetReferenceListView();
                    return; // Only operands can have references.
                }

                if (element_ != value) {
                    if (HasPinnedContent) {
                        return; // Keep pinned element.
                    }

                    element_ = value;
                    FindAllReferences(element_, false, false);
                }
            }
        }

        private (TextBlock, string) CreatePreviewTextBlock(OperandIR operand, Reference reference) {
            // Mark every instance of the symbol name in the preview text (usually an instr).
            string text = FindPreviewText(reference);
            string symbolName = ReferenceFinder.GetSymbolName(operand);
            
            var textBlock = new TextBlock();
            textBlock.FontFamily = App.StyleResources.DocumentFont;
            textBlock.Foreground = App.StyleResources.ForegroundBrush;
            textBlock.Margin = new Thickness(0, 2, 0, 0);
            int index = 0;

            while (index < text.Length) {
                int symbolIndex = text.IndexOf(symbolName, index, StringComparison.Ordinal);

                if (symbolIndex == -1) {
                    break;
                }

                // Append any text before the symbol name.
                if (index < symbolIndex) {
                    textBlock.Inlines.Add(text.Substring(index, symbolIndex - index));
                }

                textBlock.Inlines.Add(new Run(symbolName) {
                    FontWeight = FontWeights.Bold,
                    Background = App.StyleResources.HighlightBackgroundBrush
                });

                index = symbolIndex + symbolName.Length;
            }

            // Append remaining text at the end.
            if (index < text.Length) {
                textBlock.Inlines.Add(text.Substring(index, text.Length - index));
            }

            return (textBlock, text);
        }

        private string FindPreviewText(Reference reference) {
            var instr = reference.Element.ParentInstruction;
            string text = "";

            if (instr != null) {
                text = instr.GetText(documentText_).ToString();
            }
            else {
                if (reference.Element is OperandIR op) {
                    // This is usually a parameter.
                    text = op.GetText(documentText_).ToString();
                }
                else {
                    return "";
                }
            }

            int start = 0;
            int length = text.Length;

            if (instr != null && instr.Destinations.Count > 0) {
                var firstDest = instr.Destinations[0];
                start = firstDest.TextLocation.Offset - instr.TextLocation.Offset;
                start = Math.Max(0, start); //? TODO: Workaround for offset not being right
            }

            if (instr != null && instr.Sources.Count > 0) {
                var lastSource = instr.Sources.FindLast(s => s.TextLocation.Offset != 0);

                if (lastSource != null) {
                    length = lastSource.TextLocation.Offset -
                             instr.TextLocation.Offset +
                             lastSource.TextLength;

                    if (length <= 0) {
                        length = text.Length;
                    }

                    length = Math.Min(text.Length, length); //? TODO: Workaround for offset not being right
                }
            }

            if (start != 0 || length > 0) {
                int actualLength = Math.Min(length - start, text.Length - start);

                if (actualLength > 0) {
                    text = text.Substring(start, actualLength);
                }
            }

            return text.RemoveNewLines();
        }

        private bool FilterReferenceList(object value) {
            var refInfo = value as ReferenceEx;
            return filterKind_.HasFlag(refInfo.Info.Kind);
        }

        public bool FindAllReferences(IRElement element, bool showOnlySSAUses, bool pinElement = true) {
            if (!(element is OperandIR operand)) {
                ResetReferenceListView();
                return false;
            }

            var refFinder = new ReferenceFinder(Document.Function);
            var operandRefs = refFinder.FindAllReferences(element, includeSSAUses: true);
            var summary = UpdateReferenceListView(operand, operandRefs);

            // Enabled the filters.
            if (showOnlySSAUses || (summary.SSACount > 0 && settings_.ShowOnlySSA)) {
                FilterKind = ReferenceKind.SSA;
            }
            else {
                FilterKind |= ReferenceKind.Address | ReferenceKind.Load | ReferenceKind.Store;
            }

            FixedToolbar.IsPinned = pinElement;
            element_ = element;
            isFindAll_ = true;
            return operandRefs.Count > 0;
        }

        private ReferenceSummary UpdateReferenceListView(OperandIR operand, List<Reference> operandRefs) {
            var summary = BuildReferenceList(operand, operandRefs);

            referenceListView_ = new ListCollectionView(referenceList_);
            referenceListView_.Filter = FilterReferenceList;
            ReferenceList.ItemsSource = referenceListView_;
            ReferenceSummary = summary;

            if (operand != null) {
                SymbolName.Text = ReferenceFinder.GetSymbolName(operand);
            }
            else {
                SymbolName.Text = "";
            }

            return summary;
        }

        private ReferenceSummary BuildReferenceList(OperandIR operand, List<Reference> operandRefs) {
            referenceList_ = new List<ReferenceEx>(operandRefs.Count);
            var summary = new ReferenceSummary();
            int index = 1;

            // Sort based on the ref. element text offset.
            operandRefs.Sort((a, b) => a.Element.TextLocation.Offset - b.Element.TextLocation.Offset);

            foreach (var reference in operandRefs) {
                (var preview, string previewText) = CreatePreviewTextBlock(operand, reference);
                var referenceEx = new ReferenceEx(index, reference, preview, previewText);

                switch (reference.Kind)
                {
                    case ReferenceKind.Address:
                        referenceEx.TextColor = ColorBrushes.GetBrush(settings_.AddressTextColor);
                        summary.AddressCount++;
                        break;
                    case ReferenceKind.Load:
                        referenceEx.TextColor = ColorBrushes.GetBrush(settings_.LoadTextColor);
                        summary.LoadCount++;
                        break;
                    case ReferenceKind.Store:
                        referenceEx.TextColor = ColorBrushes.GetBrush(settings_.StoreTextColor);
                        summary.StoreCount++;
                        break;
                    case ReferenceKind.SSA:
                        referenceEx.TextColor = ColorBrushes.GetBrush(settings_.SSATextColor);
                        summary.SSACount++;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                referenceList_.Add(referenceEx);
                index++;
            }

            return summary;
        }

        private void ResetReferenceListView() {
            section_ = null;
            element_ = null;
            HasPinnedContent = false;
            ReferenceList.ItemsSource = null;
            ReferenceSummary = null;
            SymbolName.Text = "";
        }

        public void InitializeFromDocument(IRDocument document) {
            if (Document != document ||
                section_ != document.Section) {
                Document = document;
                documentText_ = document.Text; // Cache text.
                section_ = document.Section;
                Element = null;
            }
        }

        private void ComboBox_Loaded(object sender, RoutedEventArgs e) {
            if (sender is ComboBox control) {
                Utils.PatchComboBoxStyle(control);
            }
        }

        private void JumpToReferenceExecuted(object sender, ExecutedRoutedEventArgs e) {
            var refInfo = e.Parameter as ReferenceEx;
            JumpToReference(refInfo);
        }

        private void MarkReferenceExecuted(object sender, ExecutedRoutedEventArgs e) {
            if (ReferenceList.SelectedItem is ReferenceEx refInfo) {
                var color = ((SelectedColorEventArgs)e.Parameter).SelectedColor;
                Document.MarkElement(refInfo.Info.Element, color);
            }
        }

        private void UnmarkReferenceExecuted(object sender, ExecutedRoutedEventArgs e) {
            var refInfo = ReferenceList.SelectedItem as ReferenceEx;

            if (refInfo != null) {
                Document.ClearMarkedElement(refInfo.Info.Element);
            }
        }

        private void CopyToClipboardExecuted(object sender, ExecutedRoutedEventArgs e) {
            if (referenceList_ == null) {
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"References for symbol: {SymbolName.Text}");
            sb.AppendLine($"  SSA uses: {referenceSummary_.SSACount}");
            sb.AppendLine($"  Load uses: {referenceSummary_.LoadCount}");
            sb.AppendLine($"  Store uses: {referenceSummary_.StoreCount}");
            sb.AppendLine($"  Address uses: {referenceSummary_.AddressCount}");

            foreach (var refInfo in referenceList_) {
                sb.AppendLine($"# {refInfo.Index}: {refInfo.Kind}, block {refInfo.Block}");
                sb.AppendLine($"    {refInfo.PreviewText}");
            }

            Clipboard.SetText(sb.ToString());
        }

        private void ListViewItem_MouseDoubleClick(object sender, MouseButtonEventArgs e) {
            var refInfo = ((ListViewItem)sender).DataContext as ReferenceEx;
            JumpToReference(refInfo);
        }

        private void JumpToReference(ReferenceEx refEx) {
            ignoreNextElement_ = true;
            Document.SelectElement(refEx.Info.Element);
            Document.BringElementIntoView(refEx.Info.Element);
        }

        private void ListViewItem_MouseEnter(object sender, MouseEventArgs e) {
            HideToolTip();

            if(!settings_.ShowPreviewPopup) {
                return;
            }

            var listItem = sender as ListViewItem;
            var refInfo = listItem.DataContext as ReferenceEx;
            previewTooltip_ = new IRPreviewToolTip(600, 100, Document, refInfo.Info.Element, documentText_);
            listItem.ToolTip = previewTooltip_;
        }

        private void HideToolTip() {
            if (previewTooltip_ != null) {
                previewTooltip_.Hide();
                previewTooltip_ = null;
            }
        }

        private void ToolBar_Loaded(object sender, RoutedEventArgs e) {
            Utils.PatchToolbarStyle(sender as ToolBar);
        }

        private void PanelToolbarTray_SettingsClicked(object sender, EventArgs e) {
            if (optionsPanelVisible_) {
                CloseOptionsPanel();
            }
            else {
                ShowOptionsPanel();
            }
        }

        private void PanelToolbarTray_PinnedChanged(object sender, PinEventArgs e) {
            HasPinnedContent = e.IsPinned;
        }

        private void PanelToolbarTray_DuplicateClicked(object sender, DuplicateEventArgs e) {
            Session.DuplicatePanel(this, e.Kind);
        }

        private void FixedToolbar_BindMenuOpen(object sender, BindMenuItemsArgs e) {
            Session.PopulateBindMenu(this, e);
        }

        private void FixedToolbar_BindMenuItemSelected(object sender, BindMenuItem e) {
            Session.BindToDocument(this, e);
        }

        #region IToolPanel

        public override ToolPanelKind PanelKind => ToolPanelKind.References;
        public override HandledEventKind HandledEvents => HandledEventKind.ElementSelection;

        public override bool HasPinnedContent {
            get => FixedToolbar.IsPinned;
            set => FixedToolbar.IsPinned = value;
        }

        public override void OnDocumentSectionLoaded(IRTextSection section, IRDocument document) {
            if (section == section_) {
                return;
            }

            InitializeFromDocument(document);
            var data = Session.LoadPanelState(this, section, document);
            var state = StateSerializer.Deserialize<ReferencePanelState>(data, document.Function);

            if (state != null) {
                FindAllReferences(state.Element, !state.IsFindAll);
                HasPinnedContent = state.HasPinnedContent;
                FilterKind = state.FilterKind;
            }
            else {
                ResetReferenceListView();
            }

            IsPanelEnabled = document != null;
        }

        public override void OnDocumentSectionUnloaded(IRTextSection section, IRDocument document) {
            if (element_ == null) {
                return;
            }

            var state = new ReferencePanelState();
            state.IsFindAll = isFindAll_;
            state.Element = Element;
            state.HasPinnedContent = HasPinnedContent;
            state.FilterKind = FilterKind;
            var data = StateSerializer.Serialize(state, document.Function);
            var back = StateSerializer.Deserialize<ReferencePanelState>(data, document.Function);
            Session.SavePanelState(data, this, section, Document);

            var data2 = Session.LoadPanelState(this, section, Document);
            var back2 = StateSerializer.Deserialize<ReferencePanelState>(data, document.Function);

            ResetReferenceListView();
            Document = null;
        }

        public override void OnSessionEnd() {
            ResetReferenceListView();
            base.OnSessionEnd();
        }

        public override void OnElementSelected(IRElementEventArgs e) {
            Element = e.Element;
        }

        public override void ClonePanel(IToolPanel sourcePanel) {
            var sourceRefPanel = sourcePanel as ReferencesPanel;
            Document = sourceRefPanel.Document;
            documentText_ = sourceRefPanel.documentText_;
            IsPanelEnabled = Document != null;
        }

        #endregion

        private bool SetFilterFlag(ReferenceKind flag, bool value) {
            if (value && !FilterKind.HasFlag(flag)) {
                FilterKind |= flag;
                return true;
            }
            else if (!value && FilterKind.HasFlag(flag)) {
                FilterKind &= ~flag;
                return true;
            }

            return false;
        }

        private void HandleNewSettings(ReferenceSettings newSettings, bool commit, bool force = false) {
            if (commit) {
                App.Settings.ReferenceSettings = newSettings;
                App.SaveApplicationSettings();
            }

            if (newSettings.Equals(settings_) && !force) {
                return;
            }

            App.Settings.ReferenceSettings = newSettings;
            settings_ = newSettings;
            FindAllReferences(element_, !isFindAll_);
        }

        public override void OnThemeChanged() {
            HandleNewSettings(App.Settings.ReferenceSettings, false, true);
        }
        
        private void OptionsPanel_PanelClosed(object sender, EventArgs e) {
            CloseOptionsPanel();
        }

        private void OptionsPanel_PanelReset(object sender, EventArgs e) {
            optionsPanel_.ResetSettings();
            LoadNewSettings(true);
        }
        
        protected virtual void ShowOptionsPanel() {
            if (optionsPanelVisible_) {
                return;
            }

            var width = Math.Max(ReferencesOptionsPanel.MinimumWidth,
                    Math.Min(ReferenceList.ActualWidth, ReferencesOptionsPanel.DefaultWidth));
            var height = Math.Max(ReferencesOptionsPanel.MinimumHeight,
                Math.Min(ReferenceList.ActualHeight, ReferencesOptionsPanel.DefaultHeight));
            var position = ReferenceList.PointToScreen(new Point(ReferenceList.ActualWidth - width, 0));
            optionsPanel_ = new OptionsPanelHostWindow(new ReferencesOptionsPanel(),
                                                       position, width, height, this);

            optionsPanel_.Settings = settings_;
            optionsPanel_.PanelClosed += OptionsPanel_PanelClosed;
            optionsPanel_.PanelReset += OptionsPanel_PanelReset;
            optionsPanel_.SettingsChanged += OptionsPanel_SettingsChanged;
            optionsPanel_.IsOpen = true;
            optionsPanelVisible_ = true;
        }

        private void OptionsPanel_SettingsChanged(object sender, bool force) {
            if (optionsPanelVisible_) {
                LoadNewSettings(false);
            }
        }

        protected virtual void CloseOptionsPanel() {
            if (!optionsPanelVisible_) {
                return;
            }

            LoadNewSettings(true);
            optionsPanel_.IsOpen = false;
            optionsPanel_.PanelClosed -= OptionsPanel_PanelClosed;
            optionsPanel_.PanelReset -= OptionsPanel_PanelReset;
            optionsPanel_.SettingsChanged -= OptionsPanel_SettingsChanged;
            optionsPanelVisible_ = false;
            optionsPanel_ = null;
        }

        private void LoadNewSettings(bool commit) {
            var newSettings = optionsPanel_.GetSettingsSnapshot<ReferenceSettings>();
            HandleNewSettings(newSettings, commit);
        }
    }
}
