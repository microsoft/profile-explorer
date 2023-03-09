using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using IRExplorerCore;
using IRExplorerCore.IR;
using IRExplorerUI.Controls;
using IRExplorerUI.Profile;
using Microsoft.Diagnostics.Tracing.Stacks;

namespace IRExplorerUI.Profile;

public partial class CallTreeNodePopup : DraggablePopup, INotifyPropertyChanged {
    public static readonly TimeSpan PopupHoverDuration = TimeSpan.FromMilliseconds(50);
    public static readonly TimeSpan PopupHoverLongDuration = TimeSpan.FromMilliseconds(1000);
    private const int MaxPreviewNameLength = 80;
    internal const double DefaultTextSize = 12;
    private static readonly Typeface DefaultTextFont = new Typeface("Segoe UI");
    
    public ISession Session { get; set; }
    public ProfileCallTreeNode CallTreeNode { get; set; }
    public ProfileCallTreeNodeEx Node => PanelHost.Node;
    public event PropertyChangedEventHandler PropertyChanged;

    public CallTreeNodePopup(ProfileCallTreeNode node, IFunctionProfileInfoProvider funcInfoProvider,
                             Point position, UIElement referenceElement, ISession session, bool canExpand = true) {

        InitializeComponent();
        Initialize(position, referenceElement);
        PanelResizeGrip.ResizedControl = this;

        Session = session;
        CanExpand = canExpand;
        PanelHost.ShowInstanceNavigation = false;
        PanelHost.Initialize(session, funcInfoProvider);
        UpdateNode(node);
        DataContext = this;
    }
    
    public void UpdateNode(ProfileCallTreeNode node) {
        if (node == CallTreeNode) {
            return;
        }

        CallTreeNode = node;
        PanelHost.Show(CallTreeNode);
        OnPropertyChanged(nameof(Node));
    }

    private bool showResizeGrip_;
    public bool ShowResizeGrip {
        get => showResizeGrip_;
        set => SetField(ref showResizeGrip_, value);
    }

    private bool canExpand_;
    public bool CanExpand {
        get => canExpand_;
        set => SetField(ref canExpand_, value);
    }

    private bool showBacktraceView_;
    public bool ShowBacktraceView {
        get => showBacktraceView_;
        set => SetField(ref showBacktraceView_, value);
    }
        
    private string backtraceText_;
    public string BacktraceText {
        get => backtraceText_;
        set => SetField(ref backtraceText_, value);
    }
    
    public static (string, double) CreateBacktraceText(ProfileCallTreeNode node, int maxLevel,
                                                       FunctionNameFormatter nameFormatter) {
        var sb = new StringBuilder();
        double maxTextWidth = 0;

        while (node != null && maxLevel-- > 0) {
            var funcName = node.FormatFunctionName(nameFormatter, MaxPreviewNameLength);
            var textSize = Utils.MeasureString(funcName, DefaultTextFont, DefaultTextSize);
            maxTextWidth = Math.Max(maxTextWidth, textSize.Width);
            sb.AppendLine(funcName);
            node = node.Caller;
        }

        if (node != null && node.HasCallers) {
            sb.AppendLine("...");
        }

        return (sb.ToString().Trim(), maxTextWidth);
    }
    
    public override bool ShouldStartDragging(MouseButtonEventArgs e) {
        if (e.LeftButton == MouseButtonState.Pressed && ToolbarPanel.IsMouseOver) {
            if (!IsDetached) {
                DetachPopup();
            }

            return true;
        }

        return false;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) {
        Session.UnregisterDetachedPanel(this);
        ClosePopup();
    }

    private async void ExpandButton_OnClick(object sender, RoutedEventArgs e) {
        DetachPopup();
        await PanelHost.ShowDetailsAsync();
        Height = 400;
        ShowResizeGrip = true;
    }

    protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null) {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null) {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}