// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
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
                return Info.Kind switch
                {
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
        [ProtoMember(4)]
        public int FilterSelectedIndex;
        [ProtoMember(3)]
        public bool HasPinnedContent;

        [ProtoMember(2)]
        public bool IsFindAll;

        public IRElement Element {
            get => elementRef_;
            set => elementRef_ = value;
        }
    }

    public partial class ReferencesPanel : ToolPanelControl {
        private static readonly FontFamily PreviewFont = new FontFamily("Consolas");
        private IRDocument document_;

        private IRElement element_;
        private bool filterEnabled_;
        private ReferenceKind filterKind_;
        private bool focusedOnce_;
        private bool ignoreNextElement_;
        private bool isFindAll_;

        private IRPreviewToolTip previewTooltip_;
        private List<ReferenceInfo> referenceList_;
        private ListCollectionView referenceListView_;
        private ReferenceSummary referenceSummary_;
        private IRTextSection section_;

        public ReferencesPanel() {
            InitializeComponent();
        }

        public IRElement Element {
            get => element_;
            set {
                if (ignoreNextElement_) {
                    ignoreNextElement_ = false;
                    return;
                }

                if (!(value is OperandIR)) {
                    return;
                }

                if (element_ != value) {
                    if (value != null && HasPinnedContent) {
                        return;
                    }

                    element_ = value;

                    if (FindSSAUses(element_, false).Count == 0) {
                        FindAllReferences(element_, false);
                    }
                }
            }
        }

        private (TextBlock, string) CreatePreviewTextBlock(OperandIR operand, Reference reference) {
            string text = FindPreviewText(reference);
            string symbolName = ReferenceFinder.GetSymbolName(operand);
            int index = 0;
            var textBlock = new TextBlock();
            textBlock.FontFamily = PreviewFont;
            textBlock.Foreground = Brushes.Black;
            textBlock.Margin = new Thickness(0, 2, 0, 0);

            while (index < text.Length) {
                int symbolIndex = text.IndexOf(symbolName, index, StringComparison.InvariantCulture);

                if (symbolIndex == -1) {
                    break;
                }

                if (index < symbolIndex) {
                    textBlock.Inlines.Add(text.Substring(index, symbolIndex - index));
                }

                textBlock.Inlines.Add(new Run(symbolName) {
                    FontWeight = FontWeights.Bold
                });

                index = symbolIndex + symbolName.Length;
            }

            if (index < text.Length) {
                textBlock.Inlines.Add(text.Substring(index, text.Length - index));
            }

            return (textBlock, text);
        }

        private string FindPreviewText(Reference reference) {
            var instr = reference.Element.ParentInstruction;
            string text = instr.GetText(document_.Text).ToString();
            int start = 0;
            int length = text.Length;

            if (instr.Destinations.Count > 0) {
                var firstDest = instr.Destinations[0];
                start = firstDest.TextLocation.Offset - instr.TextLocation.Offset;
                start = Math.Max(0, start); //? TODO: Workaround for offset not being right
            }

            if (instr.Sources.Count > 0) {
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
            if (!filterEnabled_) {
                return true;
            }

            var refInfo = value as ReferenceInfo;
            return refInfo.Info.Kind == filterKind_;
        }

        public List<Reference> FindAllReferences(IRElement element, bool pinElement = true) {
            if (!(element is OperandIR operand)) {
                ResetReferenceListView();
                return new List<Reference>();
            }

            var refFinder = new ReferenceFinder(document_.Function);
            var operandRefs = refFinder.FindAllReferences(element);
            UpdateReferenceListView(operand, operandRefs);

            // Select the overview if viewing SSA uses.
            if (FilterComboBox.SelectedIndex == 1) {
                FilterComboBox.SelectedIndex = 0;
            }

            FixedToolbar.IsPinned = pinElement;
            element_ = element;
            isFindAll_ = true;
            return operandRefs;
        }

        public List<Reference> FindSSAUses(IRElement element, bool pinElement = true) {
            if (!(element is OperandIR operand)) {
                ResetReferenceListView();
                return new List<Reference>();
            }

            var operandRefs = ReferenceFinder.FindSSAUses(operand);
            UpdateReferenceListView(operand, operandRefs);

            // Select the SSA uses filter.
            FilterComboBox.SelectedIndex = 1;
            FixedToolbar.IsPinned = pinElement;
            element_ = element;
            isFindAll_ = false;
            return operandRefs;
        }

        private void UpdateReferenceListView(OperandIR operand, List<Reference> operandRefs) {
            referenceList_ = new List<ReferenceInfo>(operandRefs.Count);
            referenceSummary_ = new ReferenceSummary();
            int index = 1;

            // Sort based on the ref. element text offset.
            operandRefs.Sort((a, b) => a.Element.TextLocation.Offset - b.Element.TextLocation.Offset);

            foreach (var reference in operandRefs) {
                (var preview, string previewText) = CreatePreviewTextBlock(operand, reference);
                referenceList_.Add(new ReferenceInfo(index, reference, preview, previewText));
                index++;

                switch (reference.Kind) {
                    case ReferenceKind.Address:
                        referenceSummary_.AddressCount++;
                        break;
                    case ReferenceKind.Load:
                        referenceSummary_.LoadCount++;
                        break;
                    case ReferenceKind.Store:
                        referenceSummary_.StoreCount++;
                        break;
                    case ReferenceKind.SSA:
                        referenceSummary_.SSACount++;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            referenceListView_ = new ListCollectionView(referenceList_);
            referenceListView_.Filter = FilterReferenceList;
            ReferenceList.ItemsSource = referenceListView_;
            FilterComboBox.DataContext = referenceSummary_;

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
            FilterComboBox.DataContext = null;
            SymbolName.Text = "";
        }

        public void InitializeFromDocument(IRDocument document) {
            if (document_ != document) {
                document_ = document;
                Element = null;
            }
        }

        private void ComboBox_Loaded(object sender, RoutedEventArgs e) {
            if (sender is ComboBox control) {
                Utils.PatchComboBoxStyle(control);
            }
        }

        private void FilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (referenceListView_ == null) {
                return;
            }

            var item = e.AddedItems[0] as ComboBoxItem;
            string kindString = item.Tag as string;

            if (kindString == "All") {
                filterEnabled_ = false;
            }
            else {
                filterEnabled_ = true;

                filterKind_ = kindString switch
                {
                    "SSA" => ReferenceKind.SSA,
                    "Load" => ReferenceKind.Load,
                    "Store" => ReferenceKind.Store,
                    "Address" => ReferenceKind.Address,
                    _ => filterKind_
                };
            }

            referenceListView_.Refresh();
        }

        private void JumpToReferenceExecuted(object sender, ExecutedRoutedEventArgs e) {
            var refInfo = e.Parameter as ReferenceInfo;
            JumpToReference(refInfo);
        }

        private void MarkReferenceExecuted(object sender, ExecutedRoutedEventArgs e) {
            if (ReferenceList.SelectedItem is ReferenceInfo refInfo) {
                var color = ((ColorEventArgs)e.Parameter).SelectedColor;
                document_.MarkElement(refInfo.Info.Element, color);
            }
        }

        private void UnmarkReferenceExecuted(object sender, ExecutedRoutedEventArgs e) {
            var refInfo = ReferenceList.SelectedItem as ReferenceInfo;

            if (refInfo != null) {
                document_.ClearMarkedElement(refInfo.Info.Element);
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
            document_.SelectElement(refInfo.Info.Element);
            document_.BringElementIntoView(refInfo.Info.Element);
        }

        private void ListViewItem_MouseEnter(object sender, MouseEventArgs e) {
            HideToolTip();
            var listItem = sender as ListViewItem;
            var refInfo = listItem.DataContext as ReferenceInfo;
            previewTooltip_ = new IRPreviewToolTip(600, 100, document_, refInfo.Info.Element);
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

        public override void OnActivatePanel() {
            // Hack to prevent DropDown in toolbar to get focus 
            // the first time the panel is made visible.
            if (!focusedOnce_) {
                ReferenceList.Focus();
                focusedOnce_ = true;
            }
        }

        public override void OnDocumentSectionLoaded(IRTextSection section, IRDocument document) {
            if (section == section_) {
                return;
            }

            InitializeFromDocument(document);
            var data = Session.LoadPanelState(this, section);
            var state = StateSerializer.Deserialize<ReferencePanelState>(data, document.Function);

            if (state != null) {
                if (state.IsFindAll) {
                    FindAllReferences(state.Element);
                }
                else {
                    FindSSAUses(state.Element);
                }

                HasPinnedContent = state.HasPinnedContent;
                FilterComboBox.SelectedIndex = state.FilterSelectedIndex;
            }
            else {
                ResetReferenceListView();
            }

            IsPanelEnabled = document_ != null;
            section_ = section;
        }

        public override void OnDocumentSectionUnloaded(IRTextSection section, IRDocument document) {
            if (element_ == null) {
                return;
            }

            var state = new ReferencePanelState();
            state.IsFindAll = isFindAll_;
            state.Element = Element;
            state.HasPinnedContent = HasPinnedContent;
            state.FilterSelectedIndex = FilterComboBox.SelectedIndex;
            var data = StateSerializer.Serialize(state, document.Function);
            Session.SavePanelState(data, this, section);
            ResetReferenceListView();
        }

        public override void OnSessionEnd() {
            base.OnSessionEnd();
            ResetReferenceListView();
        }

        public override void OnElementSelected(IRElementEventArgs e) {
            Element = e.Element;
        }

        public override void ClonePanel(IToolPanel sourcePanel) {
            var sourceRefPanel = sourcePanel as ReferencesPanel;
            document_ = sourceRefPanel.document_;
            IsPanelEnabled = document_ != null;
        }

        #endregion
    }
}
