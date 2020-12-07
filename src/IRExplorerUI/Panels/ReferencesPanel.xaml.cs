// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.ComponentModel;
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

    public class ReferenceInfo {
        public ReferenceInfo(int index, Reference info, TextBlock preview, string previewText) {
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
        private static readonly FontFamily PreviewFont = new FontFamily("Consolas");
        private string documentText_;

        private IRElement element_;
        private ReferenceKind filterKind_;
        private bool ignoreNextElement_;
        private bool isFindAll_;

        private IRPreviewToolTip previewTooltip_;
        private List<ReferenceInfo> referenceList_;
        private ListCollectionView referenceListView_;
        private ReferenceSummary referenceSummary_;
        private IRTextSection section_;

        public ReferencesPanel() {
            InitializeComponent();
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
                    referenceListView_.Refresh();
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
                    return; // Only operands can have references.
                }

                if (element_ != value) {
                    if (value != null && HasPinnedContent) {
                        return; // Keep pinned element.
                    }

                    element_ = value;

                    //? TODO: There should be an option to pick default behavior for SSA values
                    //?  - if SSA def, show only SSA uses
                    //?  - or always show all refs
                    if (!FindAllReferences(element_, showSSAUses: true, false)) {
                        FindAllReferences(element_, showSSAUses: false, false);
                    }
                }
            }
        }

        private (TextBlock, string) CreatePreviewTextBlock(OperandIR operand, Reference reference) {
            // Mark every instance of the symbol name in the preview text (usually an instr).
            string text = FindPreviewText(reference);
            string symbolName = ReferenceFinder.GetSymbolName(operand);
            int index = 0;
            var textBlock = new TextBlock();
            textBlock.FontFamily = PreviewFont;
            textBlock.Foreground = Brushes.Black;
            textBlock.Margin = new Thickness(0, 2, 0, 0);

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
                    FontWeight = FontWeights.Bold
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
            var refInfo = value as ReferenceInfo;
            return filterKind_.HasFlag(refInfo.Info.Kind);
        }

        public bool FindAllReferences(IRElement element, bool showSSAUses, bool pinElement = true) {
            if (!(element is OperandIR operand)) {
                ResetReferenceListView();
                return false;
            }

            var refFinder = new ReferenceFinder(Document.Function);
            var operandRefs = refFinder.FindAllReferences(element, includeSSAUses: true);
            UpdateReferenceListView(operand, operandRefs);

            // Enabled the filters.
            if (showSSAUses) {
                FilterKind |= ReferenceKind.SSA;
            }
            else {
                FilterKind |= ReferenceKind.Address | ReferenceKind.Load | ReferenceKind.Store;
            }

            FixedToolbar.IsPinned = pinElement;
            element_ = element;
            isFindAll_ = true;
            return operandRefs.Count > 0;
        }

        private void UpdateReferenceListView(OperandIR operand, List<Reference> operandRefs) {
            referenceList_ = new List<ReferenceInfo>(operandRefs.Count);
            var summary = new ReferenceSummary();
            int index = 1;

            // Sort based on the ref. element text offset.
            operandRefs.Sort((a, b) => a.Element.TextLocation.Offset - b.Element.TextLocation.Offset);

            foreach (var reference in operandRefs) {
                (var preview, string previewText) = CreatePreviewTextBlock(operand, reference);
                referenceList_.Add(new ReferenceInfo(index, reference, preview, previewText));
                index++;

                switch (reference.Kind) {
                    case ReferenceKind.Address:
                        summary.AddressCount++;
                        break;
                    case ReferenceKind.Load:
                        summary.LoadCount++;
                        break;
                    case ReferenceKind.Store:
                        summary.StoreCount++;
                        break;
                    case ReferenceKind.SSA:
                        summary.SSACount++;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

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
            var refInfo = e.Parameter as ReferenceInfo;
            JumpToReference(refInfo);
        }

        private void MarkReferenceExecuted(object sender, ExecutedRoutedEventArgs e) {
            if (ReferenceList.SelectedItem is ReferenceInfo refInfo) {
                var color = ((SelectedColorEventArgs)e.Parameter).SelectedColor;
                Document.MarkElement(refInfo.Info.Element, color);
            }
        }

        private void UnmarkReferenceExecuted(object sender, ExecutedRoutedEventArgs e) {
            var refInfo = ReferenceList.SelectedItem as ReferenceInfo;

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
            var refInfo = ((ListViewItem)sender).DataContext as ReferenceInfo;
            JumpToReference(refInfo);
        }

        private void JumpToReference(ReferenceInfo refInfo) {
            ignoreNextElement_ = true;
            Document.SelectElement(refInfo.Info.Element);
            Document.BringElementIntoView(refInfo.Info.Element);
        }

        private void ListViewItem_MouseEnter(object sender, MouseEventArgs e) {
            HideToolTip();
            var listItem = sender as ListViewItem;
            var refInfo = listItem.DataContext as ReferenceInfo;
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
            MessageBox.Show("TODO");
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
    }
}
