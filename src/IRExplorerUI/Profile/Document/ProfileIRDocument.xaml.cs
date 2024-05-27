// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Highlighting;
using IRExplorerCore;
using IRExplorerCore.IR;
using IRExplorerCore.SourceParser;
using IRExplorerUI.Compilers;
using IRExplorerUI.Document;
using IRExplorerUI.Utilities;

namespace IRExplorerUI.Profile.Document;

public class ProfileSourceSyntaxNode {
  private static readonly IconDrawing LoopIcon;
  private static readonly IconDrawing ThenIcon;
  private static readonly IconDrawing ElseIcon;
  private static readonly IconDrawing SwitchCaseIcon;
  private static readonly IconDrawing SwitchIcon;
  public SourceSyntaxNode SyntaxNode { get; set; }
  public int Level { get; set; }
  public ProfileSourceSyntaxNode Parent { get; set; }
  public IRElement StartElement { get; set; }
  public List<IRElement> Elements { get; set; }
  public TimeSpan Weight { get; set; }
  public TimeSpan BodyWeight { get; set; }
  public TimeSpan ConditionWeight { get; set; }
  public PerformanceCounterValueSet Counters { get; set; }
  public bool ShowInDocumentColumns { get; set; }
  public SourceSyntaxNodeKind Kind => SyntaxNode.Kind;
  public TextLocation Start { get; set; }
  public TextLocation End { get; set; }
  public int Length => End.Offset - Start.Offset;

  static ProfileSourceSyntaxNode() {
    LoopIcon = IconDrawing.FromIconResource("LoopIcon");
    ThenIcon = IconDrawing.FromIconResource("ThenArrowIcon");
    ElseIcon = IconDrawing.FromIconResource("ElseArrowIcon");
    SwitchIcon = IconDrawing.FromIconResource("SwitchArrowIcon");
    SwitchCaseIcon = IconDrawing.FromIconResource("SwitchCaseArrowIcon");
  }

  public ProfileSourceSyntaxNode(SourceSyntaxNode syntaxNode) {
    SyntaxNode = syntaxNode;
    Weight = TimeSpan.Zero;
    Start = syntaxNode.Start;
    End = syntaxNode.End;
  }

  public IconDrawing GetIcon() {
    return Kind switch {
      SourceSyntaxNodeKind.Loop       => LoopIcon,
      SourceSyntaxNodeKind.If         => ThenIcon,
      SourceSyntaxNodeKind.Else       => ElseIcon,
      SourceSyntaxNodeKind.ElseIf     => ElseIcon,
      SourceSyntaxNodeKind.Switch     => SwitchIcon,
      SourceSyntaxNodeKind.SwitchCase => SwitchCaseIcon,
      _                               => null
    };
  }

  public string GetTextIcon() {
    return Kind switch {
      SourceSyntaxNodeKind.Loop       => "\u2B6F",
      SourceSyntaxNodeKind.If         => "\u2BA7",
      SourceSyntaxNodeKind.Else       => "\u2BA6",
      SourceSyntaxNodeKind.ElseIf     => "\u2BA7",
      SourceSyntaxNodeKind.Switch     => "\u21C9",
      SourceSyntaxNodeKind.SwitchCase => "\u2BA3",
      _                               => ""
    };
  }

  public string GetKindText() {
    return Kind switch {
      SourceSyntaxNodeKind.Loop       => "Loop",
      SourceSyntaxNodeKind.If         => "If",
      SourceSyntaxNodeKind.Else       => "Else",
      SourceSyntaxNodeKind.ElseIf     => "Else If",
      SourceSyntaxNodeKind.Switch     => "Switch",
      SourceSyntaxNodeKind.SwitchCase => "Switch Case",
      SourceSyntaxNodeKind.Call       => "Call",
      _                               => ""
    };
  }

  public void SetTextStyle(ProfileMenuItem value) {
    if (Kind == SourceSyntaxNodeKind.Loop) {
      value.TextColor = Brushes.DarkGreen;
      value.TextWeight = FontWeights.Bold;
    }
    else if (Kind == SourceSyntaxNodeKind.If ||
             Kind == SourceSyntaxNodeKind.Else ||
             Kind == SourceSyntaxNodeKind.ElseIf) {
      value.TextColor = Brushes.DarkBlue;
      value.TextWeight = FontWeights.SemiBold;
    }
  }

  public string GetTooltip(FunctionProfileData funcProfile) {
    var tooltip = new StringBuilder();
    tooltip.Append($"{GetKindText()} statement");
    tooltip.Append($"\nWeight: {funcProfile.ScaleWeight(Weight).AsPercentageString()}");
    tooltip.Append($" ({Weight.AsMillisecondsString()})");

    if ((SyntaxNode.Kind == SourceSyntaxNodeKind.If ||
         SyntaxNode.Kind == SourceSyntaxNodeKind.Loop) &&
        ConditionWeight != TimeSpan.Zero &&
        BodyWeight != TimeSpan.Zero) {
      tooltip.Append($"\n    Condition: {funcProfile.ScaleWeight(ConditionWeight).AsPercentageString()}");
      tooltip.Append($" ({ConditionWeight.AsMillisecondsString()})");

      tooltip.Append($"\n    Body: {funcProfile.ScaleWeight(BodyWeight).AsPercentageString()}");
      tooltip.Append($" ({BodyWeight.AsMillisecondsString()})");
    }

    return tooltip.ToString();
  }

  public bool IsMarkedNode => SyntaxNode.Kind == SourceSyntaxNodeKind.If ||
                              SyntaxNode.Kind == SourceSyntaxNodeKind.Else ||
                              SyntaxNode.Kind == SourceSyntaxNodeKind.ElseIf ||
                              SyntaxNode.Kind == SourceSyntaxNodeKind.Loop ||
                              SyntaxNode.Kind == SourceSyntaxNodeKind.Switch ||
                              SyntaxNode.Kind == SourceSyntaxNodeKind.SwitchCase;
}

public class ProfileMenuItem : BindableObject {
  private Thickness borderThickness_;
  private Brush borderBrush_;
  private string text_;
  private string prefixText_;
  private double minTextWidth_;
  private string toolTip_;
  private Brush textColor_;
  private Brush backColor_;
  private bool showPercentageBar_;
  private Brush percentageBarBackColor__;
  private double percentageBarBorderThickness_;
  private Brush percentageBarBorderBrush_;
  private FontWeight textWeight_;
  private double textSize_;
  private FontFamily textFont_;

  public ProfileMenuItem(string text, long value = 0, double valueValuePercentage = 0.0) {
    Text = text;
    Value = value;
    ValuePercentage = valueValuePercentage;
    TextWeight = FontWeights.Normal;
    TextColor = Brushes.Black;
  }

  public IRElement Element { get; set; }
  public long Value { get; set; }
  public double ValuePercentage { get; set; }

  public Thickness BorderThickness {
    get => borderThickness_;
    set => SetAndNotify(ref borderThickness_, value);
  }

  public Brush BorderBrush {
    get => borderBrush_;
    set => SetAndNotify(ref borderBrush_, value);
  }

  public string Text {
    get => text_;
    set => SetAndNotify(ref text_, value);
  }

  public string PrefixText {
    get => prefixText_;
    set => SetAndNotify(ref prefixText_, value);
  }

  public double MinTextWidth {
    get => minTextWidth_;
    set => SetAndNotify(ref minTextWidth_, value);
  }

  public string ToolTip {
    get => toolTip_;
    set => SetAndNotify(ref toolTip_, value);
  }

  public Brush TextColor {
    get => textColor_;
    set => SetAndNotify(ref textColor_, value);
  }

  public Brush BackColor {
    get => backColor_;
    set => SetAndNotify(ref backColor_, value);
  }

  public bool ShowPercentageBar {
    get => showPercentageBar_;
    set => SetAndNotify(ref showPercentageBar_, value);
  }

  public Brush PercentageBarBackColor {
    get => percentageBarBackColor__;
    set => SetAndNotify(ref percentageBarBackColor__, value);
  }

  public double PercentageBarBorderThickness {
    get => percentageBarBorderThickness_;
    set => SetAndNotify(ref percentageBarBorderThickness_, value);
  }

  public Brush PercentageBarBorderBrush {
    get => percentageBarBorderBrush_;
    set => SetAndNotify(ref percentageBarBorderBrush_, value);
  }

  public FontWeight TextWeight {
    get => textWeight_;
    set => SetAndNotify(ref textWeight_, value);
  }

  public double TextSize {
    get => textSize_;
    set => SetAndNotify(ref textSize_, value);
  }

  public FontFamily TextFont {
    get => textFont_;
    set => SetAndNotify(ref textFont_, value);
  }
}

public partial class ProfileIRDocument : UserControl, INotifyPropertyChanged {
  private List<(IRElement, TimeSpan)> profileElements_;
  private int profileElementIndex_;
  private SourceLineProcessingResult sourceProcessingResult_;
  private SourceLineProfileResult sourceProfileResult_;
  private bool columnsVisible_;
  private bool ignoreNextCaretEvent_;
  private bool disableCaretEvent_;
  private bool selectedLines_;
  private ReadOnlyMemory<char> originalSourceText_;
  private ReadOnlyMemory<char> sourceText_;
  private bool hasProfileInfo_;
  private Brush selectedLineBrush_;
  private TextViewSettingsBase settings_;
  private ProfileDocumentMarker profileMarker_;
  private bool isPreviewDocument_;
  private bool isSourceFileDocument_;
  private bool suspendColumnVisibilityHandler_;
  private ProfileSampleFilter profileFilter_;
  private CancelableTaskInstance loadTask_;
  private SourceCodeLanguage sourceLanguage_;
  private ProfileHistoryManager historyManager_;
  private RangeColorizer assemblyColorizer_;
  private SourceStackFrame inlinee_;
  private bool ignoreNextRowSelectedEvent_;

  public ProfileIRDocument() {
    InitializeComponent();
    UpdateDocumentStyle();
    SetupEvents();
    ShowPerformanceCounterColumns = true;
    ShowPerformanceMetricColumns = true;
    DataContext = this;
    loadTask_ = new CancelableTaskInstance(false);
    profileFilter_ = new ProfileSampleFilter();
    historyManager_ = new ProfileHistoryManager(() =>
                                                  new ProfileFunctionState(TextView.Section, TextView.Function,
                                                                           TextView.SectionText, profileFilter_),
                                                () => {
                                                  FunctionHistoryChanged?.Invoke(this, EventArgs.Empty);
                                                });
  }

  private void SetupEvents() {
    TextView.CaretChanged += TextView_CaretChanged;
    TextView.TextArea.TextView.ScrollOffsetChanged += TextViewOnScrollOffsetChanged;
    TextView.TextArea.SelectionChanged += TextAreaOnSelectionChanged;
    ProfileColumns.ScrollChanged += ProfileColumns_ScrollChanged;
    ProfileColumns.RowSelected += ProfileColumns_RowSelected;
    TextView.TextRegionFolded += TextViewOnTextRegionFolded;
    TextView.TextRegionUnfolded += TextViewOnTextRegionUnfolded;
    TextView.PreviewMouseDown += TextView_PreviewMouseDown;
    TextView.PreviewMouseUp += TextView_PreviewMouseUp;
    PreviewKeyDown += TextView_PreviewKeyDown;
    TextView.FunctionCallOpen += TextViewOnFunctionCallOpen;
  }

  private async void TextView_PreviewKeyDown(object sender, KeyEventArgs e) {
    if (e.Key == Key.Back) {
      if (Utils.IsKeyboardModifierActive()) {
        await LoadNextSection();
      }
      else {
        await LoadPreviousSection();
      }
    }
    else if (e.Key == Key.C && Utils.IsControlModifierActive()) {
      // Override Ctrl+C to copy instruction details instead of just text,
      // but not if Shift/Alt key is also pressed, copy plain text then.
      if (!Utils.IsAltModifierActive() &&
          !Utils.IsShiftModifierActive() &&
          !TextView.HandleOverlayKeyPress(e)) { // Send to overlays first.
        await CopySelectedLinesAsHtml();
        e.Handled = true;
      }
    }
    else if (e.Key == Key.H && Utils.IsControlModifierActive()) {
      JumpToHottestProfiledElement();
    }
    else if (e.Key == Key.F2) {
      if (Utils.IsShiftModifierActive()) {
        JumpToProfiledElement(1);
      }
      else {
        JumpToProfiledElement(-1);
      }
    }
  }

  private async void TextView_PreviewMouseDown(object sender, MouseButtonEventArgs e) {
    // Handle the back/forward mouse buttons
    // to navigate through the function history.
    if (e.ChangedButton == MouseButton.XButton1) {
      e.Handled = true;
      await LoadPreviousSection();
    }
    else if (e.ChangedButton == MouseButton.XButton2) {
      e.Handled = true;
      await LoadNextSection();
    }
    else if (e.ChangedButton == MouseButton.Left) {
      // Disable selecting the assembly lines associated with the source line
      // during a selection of multiple lines.
      disableCaretEvent_ = true;
      selectedLines_ = false;
    }
  }

  private async void TextView_PreviewMouseUp(object sender, MouseButtonEventArgs e) {
    if (e.ChangedButton == MouseButton.Left) {
      if (!selectedLines_) {
        // No multi-line selection was done,
        // select assembly lines associated with the source line.
        HighlightElementsOnSelectedLine();
      }

      disableCaretEvent_ = false;
      selectedLines_ = false;
    }
  }

  public async Task LoadPreviousSection() {
    var state = historyManager_.PopPreviousState();

    if (state != null) {
      await LoadPreviousSectionState(state);
    }
  }

  public async Task LoadNextSection() {
    var state = historyManager_.PopNextState();

    if (state != null) {
      await LoadPreviousSectionState(state);
    }
  }

  private async Task LoadPreviousSectionState(ProfileFunctionState state) {
    await LoadAssembly(state.ParsedSection, state.ProfileFilter);
  }

  private async void TextViewOnFunctionCallOpen(object sender, IRTextSection targetSection) {
    var targetFunc = targetSection.ParentFunction;
    ProfileSampleFilter targetFilter = null;

    if (profileFilter_ is {IncludesAll: false}) {
      targetFilter = profileFilter_.CloneForCallTarget(targetFunc);
    }

    historyManager_.ClearNextStates(); // Reset forward history.
    var parsedSection = await Session.LoadAndParseSection(targetFunc.Sections[0]);

    if (parsedSection != null) {
      await LoadAssembly(parsedSection, targetFilter);
    }
  }

  public event EventHandler<string> TitlePrefixChanged;
  public event EventHandler<string> TitleSuffixChanged;
  public event EventHandler<string> DescriptionPrefixChanged;
  public event EventHandler<string> DescriptionSuffixChanged;
  public event EventHandler<ParsedIRTextSection> LoadedFunctionChanged;
  public event EventHandler<int> LineSelected;
  public event EventHandler FunctionHistoryChanged;
  public event PropertyChangedEventHandler PropertyChanged;
  public ISession Session { get; set; }
  public IRTextSection Section => TextView.Section;

  public ProfileSampleFilter ProfileFilter {
    get => profileFilter_;
    set {
      if (value == null) {
        profileFilter_ = new ProfileSampleFilter();
      }
      else {
        profileFilter_ = value.Clone(); // Clone to detect changes later.
      }

      UpdateProfileFilterUI();
      ProfilingUtils.SyncInstancesMenuWithFilter(InstancesMenu, profileFilter_);
      ProfilingUtils.SyncThreadsMenuWithFilter(ThreadsMenu, profileFilter_);
    }
  }

  public bool HasProfileInfo {
    get => hasProfileInfo_;
    set => SetField(ref hasProfileInfo_, value);
  }

  public bool UseCompactProfilingColumns { get; set; }
  public bool ShowPerformanceCounterColumns { get; set; }
  public bool ShowPerformanceMetricColumns { get; set; }

  public bool UseSmallerFontSize {
    get => ProfileColumns.UseSmallerFontSize;
    set => ProfileColumns.UseSmallerFontSize = value;
  }

  public bool ColumnsVisible {
    get => columnsVisible_;
    set => SetField(ref columnsVisible_, value);
  }

  public bool IsPreviewDocument {
    get => isPreviewDocument_;
    set => SetField(ref isPreviewDocument_, value);
  }

  public bool IsSourceFileDocument {
    get => isSourceFileDocument_;
    set => SetField(ref isSourceFileDocument_, value);
  }

  public Brush SelectedLineBrush {
    get => selectedLineBrush_;
    set => SetField(ref selectedLineBrush_, value);
  }

  public bool HasPreviousFunctions => historyManager_.HasPreviousStates;
  public bool HasNextFunctions => historyManager_.HasNextStates;

  protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null) {
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
  }

  protected bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null) {
    if (EqualityComparer<T>.Default.Equals(field, value)) return false;
    field = value;
    OnPropertyChanged(propertyName);
    return true;
  }

  public async Task<bool> LoadAssembly(ParsedIRTextSection parsedSection,
                                       ProfileSampleFilter profileFilter = null) {
    using var task = await loadTask_.CancelPreviousAndCreateTaskAsync();
    ResetInstance();
    IsSourceFileDocument = false;

    if (TextView.IsLoaded) {
      historyManager_.SaveCurrentState();
      TextView.UnloadDocument();
    }

    await TextView.LoadSection(parsedSection);

    // Apply profile filter if needed.
    ProfileFilter = profileFilter;
    bool success = true;

    if (!parsedSection.LoadFailed) {
      if (profileFilter is {IncludesAll: false}) {
        success = await LoadAssemblyProfileInstance(parsedSection);
      }
      else {
        success = await LoadAssemblyProfile(parsedSection);
      }
    }
    else {
      success = false;
    }

    if (!success) {
      await HideProfile();
      return false;
    }

    LoadedFunctionChanged?.Invoke(this, parsedSection);
    return true;
  }

  private async Task HideProfile() {
    HasProfileInfo = false;
    ColumnsVisible = false;
    ResetProfilingMenus();
  }

  private void ResetProfilingMenus() {
    DocumentUtils.RemoveNonDefaultMenuItems(ProfileElementsMenu);
    DocumentUtils.RemoveNonDefaultMenuItems(InstancesMenu);
    DocumentUtils.RemoveNonDefaultMenuItems(ThreadsMenu);
    DocumentUtils.RemoveNonDefaultMenuItems(InlineesMenu);
  }

  private async Task<bool> LoadAssemblyProfile(ParsedIRTextSection parsedSection,
                                               bool reloadFilterMenus = true) {
    var funcProfile = Session.ProfileData?.GetFunctionProfile(parsedSection.ParentFunction);

    if (funcProfile == null) {
      return false;
    }

    await MarkAssemblyProfile(parsedSection, funcProfile);
    CreateProfileElementMenus(funcProfile);

    if (reloadFilterMenus) {
      CreateProfileFilterMenus(parsedSection.Section, funcProfile);
    }

    return true;
  }

  private void CreateProfileElementMenus(FunctionProfileData funcProfile) {
    if (TextView.ProfileProcessingResult != null) {
      if (!isSourceFileDocument_) {
        var inlineeList = profileMarker_.GenerateInlineeList(TextView.ProfileProcessingResult);
        ProfilingUtils.CreateInlineesMenu(InlineesMenu, Section, inlineeList,
                                          funcProfile, InlineeMenuItem_OnClick, settings_, Session);
      }

      CreateProfileElementMenu(funcProfile, TextView.ProfileProcessingResult);
    }
  }

  private async Task<bool> LoadAssemblyProfileInstance(ParsedIRTextSection parsedSection,
                                                       bool reloadFilterMenus = true) {
    UpdateProfileFilterUI();
    var instanceProfile = await ComputeInstanceProfile();
    var funcProfile = instanceProfile.GetFunctionProfile(parsedSection.ParentFunction);

    if (funcProfile == null) {
      return false;
    }

    await MarkAssemblyProfile(parsedSection, funcProfile);
    CreateProfileElementMenus(funcProfile);

    if (reloadFilterMenus) {
      CreateProfileFilterMenus(parsedSection.Section, funcProfile);
    }

    return true;
  }

  private async Task<ProfileData> ComputeInstanceProfile() {
    return await LongRunningAction.Start(
      async () => await Task.Run(() => Session.ProfileData.
                                   ComputeProfile(Session.ProfileData, profileFilter_, false)),
      TimeSpan.FromMilliseconds(500),
      "Filtering function instance", this, Session);
  }

  private async Task<bool> LoadSourceFileProfileInstance(IRTextSection section,
                                                         bool reloadFilterMenus = true) {
    UpdateProfileFilterUI();
    var instanceProfile = await ComputeInstanceProfile();
    var funcProfile = instanceProfile.GetFunctionProfile(section.ParentFunction);

    if (funcProfile == null) {
      return false;
    }

    if (!(await MarkSourceFileProfile(section, funcProfile))) {
      return false;
    }

    CreateProfileElementMenus(funcProfile);

    if (reloadFilterMenus) {
      CreateProfileFilterMenus(section, funcProfile);
    }

    return true;
  }

  private async Task MarkAssemblyProfile(ParsedIRTextSection parsedSection, FunctionProfileData funcProfile) {
    ResetInstanceProfiling();
    profileMarker_ = new ProfileDocumentMarker(funcProfile, Session.ProfileData,
                                               settings_.ProfileMarkerSettings,
                                               settings_.ColumnSettings, Session.CompilerInfo);
    await profileMarker_.Mark(TextView, parsedSection.Function,
                              parsedSection.ParentFunction);

    if (settings_.ProfileMarkerSettings.JumpToHottestElement) {
      JumpToHottestProfiledElement(true);
    }

    UpdateProfileFilterUI();
    UpdateProfileDescription(funcProfile);
    await UpdateProfilingColumns();
  }

  private void UpdateProfileFilterUI() {
    OnPropertyChanged(nameof(HasProfileInstanceFilter));
    OnPropertyChanged(nameof(HasProfileThreadFilter));
  }

  private void UpdateProfileDescription(FunctionProfileData funcProfile) {
    DescriptionPrefixChanged?.Invoke(this, ProfilingUtils.
                                       CreateProfileFunctionDescription(funcProfile, settings_.ProfileMarkerSettings,
                                                                        Session));
    TitlePrefixChanged?.Invoke(this, ProfilingUtils.
                                 CreateProfileFilterTitle(profileFilter_, Session));
    DescriptionSuffixChanged?.Invoke(this, ProfilingUtils.
                                       CreateProfileFilterDescription(profileFilter_, Session));
  }

  public bool HasProfileInstanceFilter => profileFilter_ is {HasInstanceFilter: true};
  public bool HasProfileThreadFilter => profileFilter_ is {HasThreadFilter: true};

  string MakeSyntaxNodePreviewText(string text, int maxLength) {
    if (string.IsNullOrEmpty(text)) {
      return "";
    }

    // Extract until either a new line or the max length is reached.
    int i = 0;

    while (i < text.Length && text[i] != '\n') {
      i++;
    }

    i = Math.Min(i, maxLength);

    if (i == 0) return null;

    if (i <= text.Length) {
      return text.Substring(0, i).Trim();
    }
    else {
      return $"{text.Substring(0, i)}...".Trim();
    }
  }

  private List<ProfileSourceSyntaxNode>
    PrepareSourceSyntaxTree(string sourceText, SourceLineProcessingResult sourceProcessingResult,
                            SourceLineProfileResult sourceProfileResult,
                            SourceCodeLanguage sourceLanguage) {
    var parser = new SourceCodeParser(sourceLanguage);
    var tree = parser.Parse(sourceText);
    if (tree == null) return null;

    var funcTreeNoe = tree.FindFunctionNode(sourceProcessingResult.FirstLineIndex);

    if (funcTreeNoe == null) {
      Trace.WriteLine($"Couldn't find function in the syntax tree at line {sourceProcessingResult.FirstLineIndex}");
      return null;
    }

    // Trace.WriteLine("-------------------------------------------");
    // Trace.WriteLine("Source Syntax Tree:");
    // Trace.WriteLine($"{funcTreeNoe.Print()}");

    var profileNodes = new List<ProfileSourceSyntaxNode>();
    var profileNodeMap = new Dictionary<SourceSyntaxNode, ProfileSourceSyntaxNode>();

    funcTreeNoe.WalkNodes((node, depth) => {
      IRElement startElement = null;
      var weight = TimeSpan.Zero;
      var counters = new PerformanceCounterValueSet();
      List<IRElement> elements = new();

      // Collect the elements for the source lines that are part of the node
      // and accumulate the weight of the source lines.
      int startLine = node.Start.Line;
      int endLine = node.End.Line;

      // For if statements, the syntax node covers the line range
      // of any any other nested if/else statements, but here consider
      // only the lines in the "then" part of the if statement.
      if (node.Kind == SourceSyntaxNodeKind.If) {
        var elseNode = node.GetChildOfKind(SourceSyntaxNodeKind.Else);

        if (elseNode != null) {
          var thenNode = node.GetChildOfKind(SourceSyntaxNodeKind.Compound);

          if (thenNode != null) {
            endLine = thenNode.End.Line;
            node.End = thenNode.End;
          }
          else {
            endLine = elseNode.Start.Line;
            node.End = elseNode.Start;
          }
        }
      }

      // Accumulate profile values in range.
      for (int line = startLine; line <= endLine; line++) {
        int mappedLine = MapFromOriginalSourceLineNumber(line);

        if (sourceProfileResult.LineToElementMap.TryGetValue(mappedLine, out var element)) {
          startElement ??= element;
          elements.Add(element);

          if (sourceProcessingResult.SourceLineWeight.TryGetValue(line, out var w)) {
            weight += w;
          }

          if (sourceProcessingResult.SourceLineCounters.TryGetValue(line, out var c)) {
            counters.Add(c);
          }
        }
      }

      // Create a profile node for the syntax node,
      // except for nodes that are not interesting.
      ProfileSourceSyntaxNode profileNode = null;

      if (startElement != null &&
          node.Kind != SourceSyntaxNodeKind.Compound &&
          node.Kind != SourceSyntaxNodeKind.Condition &&
          node.Kind != SourceSyntaxNodeKind.Other) {
        profileNode = new ProfileSourceSyntaxNode(node) {
          Weight = weight,
          Counters = counters,
          StartElement = startElement,
          Elements = elements
        };

        // Connect node to the parent (because some nodes are skipped,
        // parent may not be the direct parent from the syntax tree).
        var parentNode = node.ParentNode;

        while (parentNode != null) {
          if (profileNodeMap.TryGetValue(parentNode, out var nodeParent)) {
            profileNode.Parent = nodeParent;
            profileNode.Level = nodeParent.Level + 1;
            break;
          }

          parentNode = parentNode.ParentNode;
        }

        profileNodes.Add(profileNode);
        profileNodeMap[node] = profileNode;
      }

      // Distinguish between the body and the whole statement
      // by recording the weight of the body part too.
      if (profileNode != null &&
          node.ParentNode != null &&
          profileNodeMap.TryGetValue(node.ParentNode, out var parent)) {
        if (node.ParentNode.Kind == SourceSyntaxNodeKind.If ||
            node.ParentNode.Kind == SourceSyntaxNodeKind.ElseIf) {
          if (node.Kind == SourceSyntaxNodeKind.Condition) {
            parent.ConditionWeight = weight;
            parent.BodyWeight = parent.Weight - weight;
          }
        }

        if (node.ParentNode.Kind == SourceSyntaxNodeKind.Else) {
          // Replace an else { if } pair with an IfElse statement.
          if (node.Kind == SourceSyntaxNodeKind.If) {
            node.Kind = SourceSyntaxNodeKind.ElseIf;
            node.Start = node.ParentNode.Start; // Include the "else" text.
            profileNode.Level = parent.Level;
            profileNodeMap.Remove(node.ParentNode);
            profileNodes.Remove(parent);
          }
        }
        else if (node.ParentNode.Kind == SourceSyntaxNodeKind.Else) {
          if (node.Kind == SourceSyntaxNodeKind.Else) {
            parent.BodyWeight = weight;
          }
        }
        else if (node.ParentNode.Kind == SourceSyntaxNodeKind.Loop) {
          if (node.Kind == SourceSyntaxNodeKind.Condition) {
            parent.ConditionWeight = weight;
          }
          else if (node.Kind == SourceSyntaxNodeKind.Compound) {
            parent.BodyWeight = weight;
          }
        }
      }

      return true;
    });

    return profileNodes;
  }

  private int MapFromOriginalSourceLineNumber(int line) {
    // Map from original line to adjusted line with assembly.
    if (sourceProfileResult_ != null) {
      if (sourceProfileResult_.OriginalLineToLineMap.TryGetValue(line, out int mappedLine)) {
        return mappedLine;
      }

      return -1; // Line is assembly.
    }

    return line;
  }

  private int MapToOriginalSourceLineNumber(int line) {
    // Map from original line to adjusted line with assembly.
    if (sourceProfileResult_ != null) {
      if (sourceProfileResult_.LineToOriginalLineMap.TryGetValue(line, out int mappedLine)) {
        return mappedLine;
      }

      return -1; // Line is assembly.
    }

    return line;
  }

  public async Task<bool> LoadSourceFile(SourceFileDebugInfo sourceInfo,
                                         IRTextSection section,
                                         ProfileSampleFilter profileFilter = null,
                                         SourceStackFrame inlinee = null) {
    try {
      using var task = await loadTask_.CancelPreviousAndCreateTaskAsync();
      ResetInstance();
      IsSourceFileDocument = true;

      string text = await File.ReadAllTextAsync(sourceInfo.FilePath);
      SetSourceText(text, sourceInfo.FilePath);

      // Apply profile filter if needed.
      ProfileFilter = profileFilter;
      inlinee_ = inlinee;
      ignoreNextCaretEvent_ = true;
      bool success = true;

      if (profileFilter is {IncludesAll: false}) {
        success = await LoadSourceFileProfileInstance(section);
      }
      else {
        success = await LoadSourceFileProfile(section);
      }

      if (!success) {
        await HideProfile();
        return true; // Only profile part failed, keep text.
      }

      //? TODO: Is panel is not visible, scroll doesn't do anything,
      //? should be executed again when panel is activated.
      if (!settings_.ProfileMarkerSettings.JumpToHottestElement) {
        (int firstSourceLineIndex, int lastSourceLineIndex) =
          await DocumentUtils.FindFunctionSourceLineRange(section.ParentFunction, TextView);

        if (firstSourceLineIndex != 0) {
          SelectLine(firstSourceLineIndex);
        }
      }

      return true;
    }
    catch (Exception ex) {
      Trace.TraceError($"Failed to load source file {sourceInfo.FilePath}: {ex.Message}");
      Trace.TraceError(ex.StackTrace);
      Trace.Flush();
      return false;
    }
  }

  public async Task HandleMissingSourceFile(string failureText) {
    string text = "Failed to load source file.";

    if (!string.IsNullOrEmpty(failureText)) {
      text += $"\n{failureText}";
    }

    SetSourceText(text, "");
    await HideProfile();
  }

  private async Task<bool> LoadSourceFileProfile(IRTextSection section, bool reloadFilterMenus = true) {
    var funcProfile = Session.ProfileData?.GetFunctionProfile(section.ParentFunction);

    if (funcProfile == null) {
      return false;
    }

    if (!(await MarkSourceFileProfile(section, funcProfile))) {
      return false;
    }

    if (reloadFilterMenus) {
      CreateProfileFilterMenus(section, funcProfile);
    }

    return true;
  }

  private void CreateProfileFilterMenus(IRTextSection section, FunctionProfileData funcProfile) {
    ProfilingUtils.CreateInstancesMenu(InstancesMenu, section, funcProfile,
                                       InstanceMenuItem_OnClick,
                                       InstanceMenuItem_OnRightClick,
                                       settings_, Session);
    ProfilingUtils.CreateThreadsMenu(ThreadsMenu, section, funcProfile,
                                     ThreadMenuItem_OnClick, settings_, Session);
    ProfilingUtils.SyncInstancesMenuWithFilter(InstancesMenu, profileFilter_);
    ProfilingUtils.SyncThreadsMenuWithFilter(ThreadsMenu, profileFilter_);
  }

  private async Task<bool> MarkSourceFileProfile(IRTextSection section, FunctionProfileData funcProfile) {
    ResetInstanceProfiling();
    profileMarker_ = new ProfileDocumentMarker(funcProfile, Session.ProfileData,
                                               settings_.ProfileMarkerSettings,
                                               settings_.ColumnSettings,
                                               Session.CompilerInfo);
    // Accumulate the instruction weight for each source line.
    var sourceLineProfileResult = await Task.Run(async () => {
      var debugInfo = await Session.GetDebugInfoProvider(section.ParentFunction).ConfigureAwait(false);
      return funcProfile.ProcessSourceLines(debugInfo, Session.CompilerInfo.IR, inlinee_);
    });

    // Clear markers when switching between ASM/source modes.
    if (TextView.IsLoaded) {
      TextView.ClearInstructionMarkers();
    }

    // Insert assembly lines corresponding to each source code line,
    // grouped as a code folding that can be hidden.
    bool showAssemblyLines = ((SourceFileSettings)settings_).ShowInlineAssembly;
    bool showSourceStatements = ((SourceFileSettings)settings_).ShowSourceStatements;
    ParsedIRTextSection parsedSection = null;

    if (showAssemblyLines) {
      // In case of changing the instance, start again from
      // the original source code text when inserting the assembly lines.
      TextView.Text = originalSourceText_.ToString();
      sourceText_ = originalSourceText_;
      parsedSection = await Session.LoadAndParseSection(section);
    }

    // Create a dummy FunctionIR that has fake tuples representing each
    // source line, with the profiling data attached to the tuples.
    var processingResult = await profileMarker_.
      PrepareSourceLineProfile(funcProfile, TextView,
                               sourceLineProfileResult, parsedSection);

    if (processingResult == null) {
      return false;
    }

    sourceProcessingResult_ = sourceLineProfileResult;
    sourceProfileResult_ = processingResult;

    // Parse the source code to get the syntax tree nodes.
    List<ProfileSourceSyntaxNode> syntaxNodes = null;

    if (showSourceStatements) {
      syntaxNodes = await Task.Run(() =>
                                     PrepareSourceSyntaxTree(sourceText_.ToString(), sourceLineProfileResult,
                                                             processingResult, sourceLanguage_));
    }

    // Replace the text after the assembly lines were inserted.
    if (showAssemblyLines) {
      sourceText_ = TextView.Text.AsMemory();
    }

    // Load the dummy section with the source lines.
    var dummyParsedSection = new ParsedIRTextSection(section, sourceText_, processingResult.Function);
    await TextView.LoadSection(dummyParsedSection, true);
    CreateProfileElementMenus(funcProfile);

    // Annotate the source lines with the profiling data based on the code statements.
    if (syntaxNodes != null) {
      await MarkSourceFileStructure(syntaxNodes, processingResult,
                                    originalSourceText_, section.ParentFunction);
    }

    TextView.SuspendUpdate();
    await profileMarker_.MarkSourceLines(TextView, processingResult);

    // Annotate call sites next to source lines by parsing the actual section
    // and mapping back the call sites to the dummy elements representing the source lines.

    if (parsedSection != null) {
      profileMarker_.MarkCallSites(TextView, parsedSection.Function,
                                   section.ParentFunction, processingResult);
    }

    TextView.ResumeUpdate();

    if (syntaxNodes != null) {
      PatchSourceStructureRows(syntaxNodes, funcProfile);
    }

    if (settings_.ProfileMarkerSettings.JumpToHottestElement) {
      JumpToHottestProfiledElement(true);
    }

    UpdateProfileFilterUI();
    UpdateProfileDescription(funcProfile);
    await UpdateProfilingColumns();

    if (showAssemblyLines) {
      SetupSourceAssembly(processingResult);
    }

    return true;
  }

  private async Task MarkSourceFileStructure(List<ProfileSourceSyntaxNode> nodes,
                                             SourceLineProfileResult sourceProfileResult,
                                             ReadOnlyMemory<char> sourceText, IRTextFunction function) {
    var profileItems = new List<ProfileMenuItem>();
    var valueTemplate = (DataTemplate)Application.Current.FindResource("ProfileMenuItemValueTemplate");
    var markerSettings = settings_.ProfileMarkerSettings;
    var funcProfile = Session.ProfileData.GetFunctionProfile(function);
    double maxWidth = 0;

    //? TODO Extract out the menu outline part
    OutlineMenu.Items.Clear();

    foreach (var node in nodes) {
      MarkSourceSyntaxNode(node, sourceProfileResult, funcProfile, markerSettings);

      // Build outline menu.
      double weightPercentage = funcProfile.ScaleWeight(node.Weight);

      if (!markerSettings.IsVisibleValue(weightPercentage)) {
        continue;
      }

      // Append | chars for alignment based on node level.
      string nesting = "";

      for (int i = 0; i < node.Level - 1; i++) {
        nesting += " \u250A   ";
      }

      int CountDigits(int number) {
        int count = 1;

        while (number >= 10) {
          count++;
          number /= 10;
        }

        return count;
      }

      // Append node start line number.
      string line = node.Kind != SourceSyntaxNodeKind.Function ? node.Start.Line.ToString() : "";
      line = line.PadRight(CountDigits(TextView.LineCount));

      string nodeText = null;

      if (node.SyntaxNode.Kind == SourceSyntaxNodeKind.Function) {
        nodeText = function.FormatFunctionName(Session);
      }
      else {
        nodeText = node.SyntaxNode.GetText(sourceText);
      }

      string preview = MakeSyntaxNodePreviewText(nodeText, 50);
      string title = $"{line} {nesting}{node.GetTextIcon()} {preview}";
      string tooltip = nodeText;
      string nodeTitle = $"({markerSettings.FormatWeightValue(node.Weight)})";

      var value = new ProfileMenuItem(nodeTitle, node.Weight.Ticks, weightPercentage) {
        PrefixText = title,
        ToolTip = tooltip,
        ShowPercentageBar = markerSettings.ShowPercentageBar(weightPercentage),
        TextWeight = node.Kind != SourceSyntaxNodeKind.Function ?
          markerSettings.PickTextWeight(weightPercentage) : FontWeights.Normal,
        PercentageBarBackColor = markerSettings.PercentageBarBackColor.AsBrush(),
      };

      node.SetTextStyle(value);

      var item = new MenuItem {
        Tag = node,
        Header = value,
        IsEnabled = node.Kind != SourceSyntaxNodeKind.Function,
        HeaderTemplate = valueTemplate,
        Style = (Style)Application.Current.FindResource("SubMenuItemHeaderStyle")
      };

      item.Click += (sender, args) => {
        if (sender is MenuItem menuItem &&
            menuItem.Tag is ProfileSourceSyntaxNode syntaxNode) {
          TextView.SelectLine(syntaxNode.StartElement.TextLocation.Line + 1);
        }
      };

      item.MouseEnter += (sender, args) => {
        if (sender is MenuItem menuItem &&
            menuItem.Tag is ProfileSourceSyntaxNode syntaxNode) {
          SelectSyntaxNodeLineRange(syntaxNode);
        }
      };

      item.MouseLeave += (sender, args) => {
        TextView.ClearSelectedElements();
      };

      profileItems.Add(value);
      OutlineMenu.Items.Add(item);
      double width = Utils.MeasureString(title, settings_.FontName, settings_.FontSize).Width;
      maxWidth = Math.Max(width, maxWidth);
    }

    foreach (var value in profileItems) {
      value.MinTextWidth = maxWidth;
    }
  }

  private void MarkSourceSyntaxNode(ProfileSourceSyntaxNode node,
                                    SourceLineProfileResult sourceProfileResult, FunctionProfileData funcProfile,
                                    ProfileDocumentMarkerSettings markerSettings) {
    if (!node.IsMarkedNode ||
        node.StartElement == null) return;

    // Show statement time in the columns, but only if
    // it doesn't replace a large enough value already associated with the line.
    int existingIndex = sourceProfileResult.Result.SampledElements.FindIndex((item) => item.Item1 == node.StartElement);
    bool replaceOnlyInsignificant = ((SourceFileSettings)settings_).ReplaceInsignificantSourceStatements;
    bool failedReplaceAttempt = false;
    bool mark = true;

    if (existingIndex != -1) {
      var existingWeight = sourceProfileResult.Result.SampledElements[existingIndex].Item2;
      double existingWeightPercentage = funcProfile.ScaleWeight(existingWeight);

      mark = !replaceOnlyInsignificant || // Always replace.
             !markerSettings.IsVisibleValue(existingWeightPercentage, 2.0);
      failedReplaceAttempt = !mark;

      if (mark) {
        sourceProfileResult.Result.SampledElements.RemoveAt(existingIndex);
        sourceProfileResult.Result.CounterElements.RemoveAll((item) => item.Item1 == node.StartElement);
      }
    }

    if (mark) {
      node.ShowInDocumentColumns = true;
      sourceProfileResult.Result.SampledElements.Add((node.StartElement, node.Weight));

      if (node.Counters is {Count: > 0}) {
        sourceProfileResult.Result.CounterElements.Add((node.StartElement, node.Counters));
      }
    }

    bool showStatementOverlays = ((SourceFileSettings)settings_).ShowSourceStatementsOnMargin;

    if (showStatementOverlays || (failedReplaceAttempt && replaceOnlyInsignificant)) {
      CreateFileStructureOverlay(node, funcProfile, markerSettings);
    }
  }

  private void CreateFileStructureOverlay(ProfileSourceSyntaxNode node,
                                          FunctionProfileData funcProfile,
                                          ProfileDocumentMarkerSettings markerSettings) {
    double weightPercentage = funcProfile.ScaleWeight(node.Weight);
    var color = App.Settings.DocumentSettings.BackgroundColor;

    if (node.StartElement.ParentBlock != null &&
        !node.StartElement.ParentBlock.HasEvenIndexInFunction) {
      color = App.Settings.DocumentSettings.AlternateBackgroundColor;
    }

    string label =
      $"{node.GetKindText()}: {weightPercentage.AsPercentageString()} ({node.Weight.AsMillisecondsString()})";
    string overalyTooltip = node.GetTooltip(funcProfile);
    var overlay = TextView.RegisterIconElementOverlay(node.StartElement, node.GetIcon(), 16, 16,
                                                      label, overalyTooltip, true);
    overlay.Tag = node;
    overlay.Background = color.AsBrush();
    overlay.IsLabelPinned = false;
    overlay.AllowLabelEditing = false;
    overlay.UseLabelBackground = true;
    overlay.ShowBackgroundOnMouseOverOnly = true;
    overlay.ShowBorderOnMouseOverOnly = true;
    overlay.AlignmentX = HorizontalAlignment.Left;
    overlay.MarginY = 2;
    (overlay.TextColor, overlay.TextWeight) = markerSettings.PickBlockOverlayStyle(weightPercentage);

    //? TODO: Click - proper selection, easy to Ctrl+C
    overlay.OnHover += (s, e) => {
      if (node.Elements != null) {
        SelectSyntaxNodeLineRange(node);
      }
    };

    overlay.OnHoverEnd += (sender, args) => {
      TextView.ClearSelectedElements();
    };

    if (node.StartElement is InstructionIR instr) {
      // Place before the call opcode.
      int lineOffset = instr.OpcodeLocation.Offset - instr.TextLocation.Offset;
      overlay.MarginX = Utils.MeasureString(lineOffset, Utils.GetTextTypeface(TextView),
                                            TextView.FontSize).Width - 20;
    }
  }

  private void PatchSourceStructureRows(List<ProfileSourceSyntaxNode> nodes, FunctionProfileData funcProfile) {
    // Override icons and tooltips for each row that corresponds
    // to a source code statement like for/if.
    var sourceColumnData = TextView.ProfileColumnData;

    if (sourceColumnData.GetColumn(ProfileDocumentMarker.TimePercentageColumnDefinition) is var timeColumn) {
      foreach (var node in nodes) {
        if (node.StartElement == null || !node.IsMarkedNode ||
            !node.ShowInDocumentColumns) {
          continue;
        }

        var row = sourceColumnData.GetValues(node.StartElement);
        if (row == null) continue;

        foreach (var pair in row.ColumnValues) {
          bool showIcon = Equals(pair.Key, timeColumn);
          var cell = pair.Value;

          if (showIcon) {
            // Override default icon.
            cell.Icon = node.GetIcon()?.Icon;
          }

          cell.CanShowIcon = false; // Disable auto-icon.
          cell.ToolTip = node.GetTooltip(funcProfile);
          cell.CanShowPercentageBar = false;
          cell.CanShowBackgroundColor = false;
          row.Tag = node;
        }
      }
    }

    // Select statement line range when hovering over the column cell.
    ProfileColumns.RowHoverStart += (sender, value) => {
      if (value.Tag is ProfileSourceSyntaxNode node) {
        SelectSyntaxNodeLineRange(node);
      }
    };

    ProfileColumns.RowHoverStop += (sender, value) => {
      TextView.ClearSelectedElements();
    };
  }

  private void SelectSyntaxNodeLineRange(ProfileSourceSyntaxNode node) {
    var selectionColor = ColorBrushes.GetTransparentBrush(settings_.SelectedValueColor, 0.5);
    TextView.SelectElementsInLineRange(node.Start.Line, node.End.Line,
                                       MapFromOriginalSourceLineNumber);
  }

  private void SetupSourceAssembly(SourceLineProfileResult processingResult) {
    // Replace the default line number left margin with one
    // that doesn't number the assembly lines, to keep same line numbers
    // with the original source file.
    var lineNumbers = new SourceLineNumberMargin(TextView, processingResult);
    TextView.SetupCustomLineNumbers(lineNumbers);

    // Create the block foldings for each source line and its assembly section.
    bool defaultClosed = !((SourceFileSettings)settings_).AutoExpandInlineAssembly;
    SetupSourceAssemblyFolding(defaultClosed);

    // Change the text color of the assembly section to be the same
    // (the source syntax highlighting may mark some ASM opcodes for ex).
    var asmFont = new Typeface(TextView.FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    assemblyColorizer_ = new RangeColorizer(processingResult.AssemblyRanges,
                                            ((SourceFileSettings)settings_).AssemblyTextColor.AsBrush(),
                                            ((SourceFileSettings)settings_).AssemblyBackColor.AsBrush(), asmFont);
    TextView.RegisterTextTransformer(assemblyColorizer_);
  }

  private void SetupSourceAssemblyFolding(bool defaultClosed) {
    FoldingElementGenerator.TextBrush = Brushes.Transparent;
    var foldingStrategy = new RangeFoldingStrategy(sourceProfileResult_.AssemblyRanges, defaultClosed);
    var foldings = TextView.SetupCustomBlockFolding(foldingStrategy);

    // Sync the initial folding status in the columns with the document.
    ProfileColumns.SetupFoldedTextRegions(foldings);
  }

  private async Task ApplyProfileFilter() {
    using var task = await loadTask_.CancelPreviousAndCreateTaskAsync();

    if (isSourceFileDocument_) {
      if (profileFilter_ is {IncludesAll: false}) {
        await LoadSourceFileProfileInstance(TextView.Section, false);
      }
      else {
        await LoadSourceFileProfile(TextView.Section, false);
      }
    }
    else {
      var parsedSection = new ParsedIRTextSection(TextView.Section,
                                                  TextView.SectionText,
                                                  TextView.Function);

      if (profileFilter_ is {IncludesAll: false}) {
        await LoadAssemblyProfileInstance(parsedSection, false);
      }
      else {
        await LoadAssemblyProfile(parsedSection, false);
      }
    }
  }

  public async Task UpdateProfilingColumns() {
    var sourceColumnData = TextView.ProfileColumnData;
    ColumnsVisible = sourceColumnData is {HasData: true};

    if (ColumnsVisible) {
      if (UseCompactProfilingColumns) {
        // Use compact mode that shows only the time column.
        if (sourceColumnData.GetColumn(ProfileDocumentMarker.TimeColumnDefinition) is var timeColumn) {
          timeColumn.Style.ShowIcon = OptionalColumnStyle.PartVisibility.Never;
        }

        if (sourceColumnData.GetColumn(ProfileDocumentMarker.TimePercentageColumnDefinition) is var timePercColumn) {
          timePercColumn.IsVisible = false;
        }
      }

      // Hide perf counter columns.
      if (!ShowPerformanceCounterColumns ||
          !ShowPerformanceMetricColumns) {
        foreach (var column in sourceColumnData.Columns) {
          if ((!ShowPerformanceCounterColumns && column.IsPerformanceCounter) ||
              (!ShowPerformanceMetricColumns && column.IsPerformanceMetric)) {
            column.IsVisible = false;
          }
        }
      }

      ProfileColumns.Reset();
      ProfileColumns.Settings = settings_;
      ProfileColumns.ColumnSettings = settings_.ColumnSettings;

      await ProfileColumns.Display(sourceColumnData, TextView);
      profileMarker_.UpdateColumnStyles(sourceColumnData, TextView.Function, TextView);
      ProfileColumns.UpdateColumnWidths();

      profileElements_ = TextView.ProfileProcessingResult.SampledElements;
      UpdateHighlighting();

      // Add the columns to the View menu.
      // While doing that, disable handling the MenuItem Checked event,
      // it gets triggered when the MenuItems are temporarily removed.
      suspendColumnVisibilityHandler_ = true;
      ProfileColumns.BuildColumnsVisibilityMenu(sourceColumnData, ProfileViewMenu, async () => {
        await UpdateProfilingColumns();
      });
      suspendColumnVisibilityHandler_ = false;
    }
    else {
      ProfileColumns.Reset();
    }

    HasProfileInfo = true;
  }

  private void CreateProfileElementMenu(FunctionProfileData funcProfile,
                                        FunctionProcessingResult result) {
    var list = new List<ProfileMenuItem>(result.SampledElements.Count);
    double maxWidth = 0;

    ProfileElementsMenu.Items.Clear();
    var valueTemplate = (DataTemplate)Application.Current.FindResource("ProfileMenuItemValueTemplate");
    var markerSettings = settings_.ProfileMarkerSettings;
    int order = 0;
    int index = 0;

    foreach (var (element, weight) in result.SampledElements) {
      // For source files, don't include assembly line elements.
      if (isSourceFileDocument_ && !IsSourceLine(element.TextLocation.Line + 1)) {
        index++;
        continue;
      }

      double weightPercentage = funcProfile.ScaleWeight(weight);

      if (!markerSettings.IsVisibleValue(order++, weightPercentage)) {
        break;
      }

      string text = $"({markerSettings.FormatWeightValue(weight)})";
      string prefixText;

      if (isSourceFileDocument_) {
        prefixText = element.GetText(TextView.SectionText).ToString();
        prefixText = prefixText.Trim().TrimToLength(80);
      }
      else {
        prefixText = DocumentUtils.GenerateElementPreviewText(element, TextView.SectionText, 50);
      }

      int line = MapToOriginalSourceLineNumber(element.TextLocation.Line + 1);
      var value = new ProfileMenuItem(text, weight.Ticks, weightPercentage) {
        Element = element,
        PrefixText = prefixText,
        ToolTip = $"Line {line}",
        ShowPercentageBar = markerSettings.ShowPercentageBar(weightPercentage),
        TextWeight = markerSettings.PickTextWeight(weightPercentage),
        PercentageBarBackColor = markerSettings.PercentageBarBackColor.AsBrush(),
      };

      var item = new MenuItem {
        Header = value,
        Tag = index,
        HeaderTemplate = valueTemplate
      };

      item.Click += (sender, args) => {
        var menuItem = (MenuItem)sender;
        JumpToProfiledElementAt((int)menuItem.Tag);
      };

      ProfileElementsMenu.Items.Add(item);

      // Make sure percentage rects are aligned.
      Utils.UpdateMaxMenuItemWidth(prefixText, ref maxWidth, ProfileElementsMenu);
      list.Add(value);
      index++;
    }

    foreach (var value in list) {
      value.MinTextWidth = maxWidth;
    }
  }

  public void SetSourceText(string text, string filePath) {
    disableCaretEvent_ = true; // Changing the text triggers the caret event twice.
    IHighlightingDefinition highlightingDef = null;

    switch (Utils.GetFileExtension(filePath).ToLowerInvariant()) {
      case ".c":
      case ".cpp":
      case ".cxx":
      case ".c++":
      case ".cc":
      case ".cp":
      case ".h":
      case ".hh":
      case ".hpp":
      case ".hxx":
      case ".inl":
      case ".ixx": {
        highlightingDef = HighlightingManager.Instance.GetDefinition("C++");
        sourceLanguage_ = SourceCodeLanguage.Cpp;
        break;
      }
      case ".cs": {
        highlightingDef = HighlightingManager.Instance.GetDefinition("C#");
        sourceLanguage_ = SourceCodeLanguage.CSharp;
        break;
      }
      case ".rs": {
        //? TODO: Rust syntax highlighting
        highlightingDef = HighlightingManager.Instance.GetDefinition("C++");
        sourceLanguage_ = SourceCodeLanguage.Rust;
        break;
      }
    }

    TextView.Text = text;
    TextView.SyntaxHighlighting = highlightingDef;
    sourceText_ = text.AsMemory();
    originalSourceText_ = sourceText_;
    disableCaretEvent_ = false;
  }

  private void ProfileColumns_ScrollChanged(object sender, ScrollChangedEventArgs e) {
    if (Math.Abs(e.VerticalChange) < double.Epsilon) {
      return;
    }

    TextView.ScrollToVerticalOffset(e.VerticalOffset);
  }

  private void UpdateDocumentStyle() {
    var settings = App.Settings.DocumentSettings;
    TextView.Background = ColorBrushes.GetBrush(settings.BackgroundColor);
    TextView.Foreground = ColorBrushes.GetBrush(settings.TextColor);
    TextView.FontFamily = new FontFamily(settings.FontName);
    TextView.FontSize = settings.FontSize;
  }

  private void JumpToProfiledElementExecuted(object sender, ExecutedRoutedEventArgs e) {
    if (!HasProfileElements()) {
      return;
    }

    profileElementIndex_ = 0;
    JumpToProfiledElement(profileElements_[profileElementIndex_].Item1);
  }

  private bool HasProfileElements() {
    return ColumnsVisible && profileElements_ != null && profileElements_.Count > 0;
  }

  private bool HasProfileElement(int offset) {
    return ColumnsVisible && profileElements_ != null &&
           profileElementIndex_ + offset >= 0 &&
           profileElementIndex_ + offset < profileElements_.Count;
  }

  private void JumpToNextProfiledElementExecuted(object sender, ExecutedRoutedEventArgs e) {
    JumpToProfiledElement(-1);
  }

  private void JumpToPreviousProfiledElementExecuted(object sender, ExecutedRoutedEventArgs e) {
    JumpToProfiledElement(1);
  }

  public void JumpToHottestProfiledElement(bool onLoad = false) {
    Dispatcher.BeginInvoke(() => {
      if (onLoad) {
        // Don't select the associated document instructions
        // when jumping during the source file load.
        ignoreNextCaretEvent_ = true;
      }

      JumpToProfiledElement(0);
    }, DispatcherPriority.ContextIdle);
  }

  private void JumpToProfiledElement(int offset) {
    if (!HasProfileElement(offset)) {
      return;
    }

    profileElementIndex_ += offset;
    JumpToProfiledElement(profileElements_[profileElementIndex_].Item1);
  }

  private void JumpToProfiledElement(IRElement element) {
    TextView.SetCaretAtElement(element);
    double offset = TextView.TextArea.TextView.VerticalOffset;
    SyncColumnsVerticalScrollOffset(offset);
  }

  private void JumpToProfiledElementAt(int index) {
    profileElementIndex_ = index;
    JumpToProfiledElement(0);
  }

  private void JumpToNextProfiledElementCanExecute(object sender, CanExecuteRoutedEventArgs e) {
    e.CanExecute = HasProfileElement(-1);
  }

  private void JumpToPreviousProfiledElementCanExecute(object sender, CanExecuteRoutedEventArgs e) {
    e.CanExecute = HasProfileElement(1);
  }

  private void TextView_CaretChanged(object sender, int e) {
    if (!TextView.IsLoaded) {
      return; // Event still triggered when unloading document, ignore.
    }

    if (columnsVisible_) {
      ignoreNextRowSelectedEvent_ = true;
      var line = TextView.Document.GetLineByOffset(TextView.TextArea.Caret.Offset);
      ProfileColumns.SelectRow(line.LineNumber - 1);
    }

    if (ignoreNextCaretEvent_) {
      ignoreNextCaretEvent_ = false;
      return;
    }

    if (disableCaretEvent_) {
      return;
    }

    HighlightElementsOnSelectedLine();
  }

  private void HighlightElementsOnSelectedLine() {
    var line = TextView.Document.GetLineByOffset(TextView.CaretOffset);

    if (line != null) {
      // With assembly lines, source line numbers are shifted.
      if (sourceProfileResult_ != null) {
        if (sourceProfileResult_.LineToOriginalLineMap.
          TryGetValue(line.LineNumber, out int originalLineNumber)) {
          LineSelected?.Invoke(this, originalLineNumber);
        }

        return; // One of the assembly lines, ignore.
      }

      LineSelected?.Invoke(this, line.LineNumber);
    }
  }

  public void SelectLine(int line) {
    ignoreNextCaretEvent_ = true;
    int mappedLine = MapFromOriginalSourceLineNumber(line);

    if (mappedLine != -1) {
      TextView.SelectLine(mappedLine);
    }
  }

  bool IsSourceLine(int line) {
    return sourceProfileResult_ == null ||
           sourceProfileResult_.LineToOriginalLineMap.ContainsKey(line);
  }

  public async Task Reset() {
    using var task = await loadTask_.CancelPreviousAndCreateTaskAsync();
    ResetProfilingMenus();
    ResetInstance();
    ProfileFilter = new ProfileSampleFilter();
    historyManager_.Reset();
    originalSourceText_ = null;
  }

  private void ResetInstance() {
    ResetInstanceProfiling();
    sourceText_ = null;
    inlinee_ = null;
  }

  private void ResetInstanceProfiling() {
    ProfileColumns.Reset();
    profileElements_ = null;
    sourceProcessingResult_ = null;
    sourceProfileResult_ = null;

    if (assemblyColorizer_ != null) {
      TextView.UnregisterTextTransformer(assemblyColorizer_);
      TextView.UninstallBlockFolding();
      assemblyColorizer_ = null;
    }
  }

  private void TextViewOnScrollOffsetChanged(object? sender, EventArgs e) {
    // Sync scrolling with the optional columns.
    double offset = TextView.TextArea.TextView.VerticalOffset;
    SyncColumnsVerticalScrollOffset(offset);
  }

  private void SyncColumnsVerticalScrollOffset(double offset) {
    // Sync scrolling with the optional columns.
    if (columnsVisible_) {
      ProfileColumns.ScrollToVerticalOffset(offset);
    }
  }

  private void UpdateHighlighting() {
    TextView.TextArea.TextView.Redraw();
  }

  private void ToolBar_Loaded(object sender, RoutedEventArgs e) {
    Utils.PatchToolbarStyle(sender as ToolBar);
  }

  public async Task ReloadSettings() {
    Initialize(settings_);
    await UpdateProfilingColumns();
  }

  public void Initialize(TextViewSettingsBase settings) {
    settings_ = settings;
    ProfileViewMenu.DataContext = settings_.ColumnSettings;
    TextView.Initialize(settings, Session);
    TextView.FontFamily = new FontFamily(settings_.FontName);

    if (settings_ is SourceFileSettings sourceSettings &&
        !sourceSettings.ShowInlineAssembly) {
      TextView.UninstallCustomLineNumbers(true);
    }

    if (UseSmallerFontSize) {
      TextView.FontSize = settings_.FontSize - 1;
    }
    else {
      TextView.FontSize = settings_.FontSize;
    }
  }

  private async void ViewMenuItem_OnCheckedChanged(object sender, RoutedEventArgs e) {
    if (!suspendColumnVisibilityHandler_) {
      await UpdateProfilingColumns();
    }
  }

  private async void ExportSourceExecuted(object sender, ExecutedRoutedEventArgs e) {
    await DocumentExporting.ExportToExcelFile(TextView,
                                              isSourceFileDocument_ ?
                                                DocumentExporting.ExportSourceAsExcelFile :
                                                DocumentExporting.ExportFunctionAsExcelFile);
  }

  private async void ExportSourceHtmlExecuted(object sender, ExecutedRoutedEventArgs e) {
    if (isSourceFileDocument_) {
      await DocumentExporting.ExportSourceToHtmlFile(TextView, MapToOriginalSourceLineNumber,
                                                     MapFromOriginalSourceLineNumber);
    }
    else {
      await DocumentExporting.ExportToHtmlFile(TextView, DocumentExporting.ExportFunctionAsHtmlFile);
    }
  }

  private async void ExportSourceMarkdownExecuted(object sender, ExecutedRoutedEventArgs e) {
    if (isSourceFileDocument_) {
      await DocumentExporting.ExportSourceToMarkdownFile(TextView, MapToOriginalSourceLineNumber,
                                                         MapFromOriginalSourceLineNumber);
    }
    else {
      await DocumentExporting.ExportToMarkdownFile(TextView, DocumentExporting.ExportFunctionAsMarkdownFile);
    }
  }

  private async void CopySelectedLinesAsHtmlExecuted(object sender, ExecutedRoutedEventArgs e) {
    await CopySelectedLinesAsHtml();
  }

  public async Task CopySelectedLinesAsHtml() {
    if (isSourceFileDocument_) {
      await DocumentExporting.CopySelectedSourceLinesAsHtml(TextView, MapToOriginalSourceLineNumber,
                                                            MapFromOriginalSourceLineNumber);
    }
    else {
      await DocumentExporting.CopySelectedLinesAsHtml(TextView);
    }
  }

  public async Task CopyAllLinesAsHtml() {
    if (isSourceFileDocument_) {
      await DocumentExporting.CopyAllSourceLinesAsHtml(TextView, MapToOriginalSourceLineNumber,
                                                       MapFromOriginalSourceLineNumber);
    }
    else {
      await DocumentExporting.CopyAllLinesAsHtml(TextView);
    }
  }

  private async void InstanceMenuItem_OnClick(object sender, RoutedEventArgs e) {
    ProfilingUtils.HandleInstanceMenuItemChanged(sender as MenuItem, InstancesMenu, profileFilter_);
    await ApplyProfileFilter();
  }

  private void InstanceMenuItem_OnRightClick(object sender, MouseButtonEventArgs e) {
    if (sender is MenuItem menuItem) {
      if (menuItem.Tag is ProfileCallTreeNode node) {
        Session.SelectProfileFunctionInPanel(node, ToolPanelKind.FlameGraph);
        Session.SelectProfileFunctionInPanel(node, ToolPanelKind.CallTree);
        e.Handled = true;
      }
    }
  }

  private async void ThreadMenuItem_OnClick(object sender, RoutedEventArgs e) {
    ProfilingUtils.HandleThreadMenuItemChanged(sender as MenuItem, ThreadsMenu, profileFilter_);
    await ApplyProfileFilter();
  }

  private void ProfileColumns_RowSelected(object sender, int line) {
    if (ignoreNextRowSelectedEvent_) {
      ignoreNextRowSelectedEvent_ = false;
      return;
    }

    TextView.SelectLine(line + 1);
  }

  private void TextAreaOnSelectionChanged(object sender, EventArgs e) {
    if (Section == null) {
      return; // Nothing loaded, ignore.
    }

    // For source files, compute the sum of the selected lines time.
    int startLine = TextView.TextArea.Selection.StartPosition.Line;
    int endLine = TextView.TextArea.Selection.EndPosition.Line;
    var funcProfile = Session.ProfileData?.GetFunctionProfile(Section.ParentFunction);

    if (sourceProcessingResult_ == null) {
      if (funcProfile == null ||
          !ProfilingUtils.ComputeAssemblyWeightInRange(startLine, endLine,
                                                       TextView.Function, funcProfile,
                                                       out var weightSum, out int count)) {
        Session.SetApplicationStatus("");
        return;
      }

      double weightPercentage = funcProfile.ScaleWeight(weightSum);
      string text = $"Selected {count}: {weightPercentage.AsPercentageString()} ({weightSum.AsMillisecondsString()})";
      Session.SetApplicationStatus(text, "Sum of time for the selected instructions");
      selectedLines_ = true;
    }
    else {
      if (funcProfile == null ||
          !ProfilingUtils.ComputeSourceWeightInRange(startLine, endLine,
                                                     sourceProcessingResult_, sourceProfileResult_,
                                                     out var weightSum, out int count)) {
        Session.SetApplicationStatus("");
        return;
      }

      double weightPercentage = funcProfile.ScaleWeight(weightSum);
      string text = $"Selected {count}: {weightPercentage.AsPercentageString()} ({weightSum.AsMillisecondsString()})";
      Session.SetApplicationStatus(text, "Sum of time for the selected source lines");
      selectedLines_ = true;
    }
  }

  private void TextViewOnTextRegionUnfolded(object sender, FoldingSection e) {
    ProfileColumns.HandleTextRegionUnfolded(e);
  }

  private void TextViewOnTextRegionFolded(object sender, FoldingSection e) {
    ProfileColumns.HandleTextRegionFolded(e);
  }

  public async Task SwitchProfileInstanceAsync(ProfileSampleFilter instanceFilter) {
    ProfileFilter = instanceFilter;
    await ApplyProfileFilter();
  }

  private async void InlineeMenuItem_OnClick(object sender, RoutedEventArgs e) {
    var inlinee = ((MenuItem)sender)?.Tag as InlineeListItem;

    if (inlinee != null && inlinee.ElementWeights is {Count: > 0}) {
      // Sort by weight and bring the hottest element into view.
      var elements = inlinee.SortedElements;
      TextView.SelectElements(elements);
      TextView.BringElementIntoView(elements[0]);
    }
  }

  private void CopySelectedTextExecuted(object sender, ExecutedRoutedEventArgs e) {
    TextView.Copy();
  }

  public RelayCommand<object> CopyDocumentCommand => new RelayCommand<object>(async obj => {
    await CopyAllLinesAsHtml();
  });

  public void ExpandBlockFoldings() {
    SetupSourceAssemblyFolding(false);
  }

  public void CollapseBlockFoldings() {
    SetupSourceAssemblyFolding(true);
  }
}
