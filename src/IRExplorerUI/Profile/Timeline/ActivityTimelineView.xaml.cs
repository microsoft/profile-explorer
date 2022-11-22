using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
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
using IRExplorerUI.Utilities;

namespace IRExplorerUI.Profile;

public enum ThreadActivityAction {
    IncludeThread,
    IncludeSameNameThread,
    ExcludeThread,
    ExcludeSameNameThread,
    FilterToThread,
    FilterToSameNameThread
}

public partial class ActivityTimelineView : UserControl, INotifyPropertyChanged {
    public ActivityTimelineView() {
        InitializeComponent();
        disabledMarginBackColor_ = Brushes.GhostWhite;
        marginBackColor_ = Brushes.Linen;
        DataContext = this;
    }

    public event EventHandler<ThreadActivityAction> ThreadActivityAction;

    public RelayCommand<object> IncludeThreadCommand => 
        new((obj) => ThreadActivityAction?.Invoke(this, Profile.ThreadActivityAction.IncludeThread));
    public RelayCommand<object> IncludeSameNameThreadCommand => 
        new((obj) => ThreadActivityAction?.Invoke(this, Profile.ThreadActivityAction.IncludeSameNameThread));
    public RelayCommand<object> ExcludeThreadCommand => 
        new((obj) => ThreadActivityAction?.Invoke(this, Profile.ThreadActivityAction.ExcludeThread));
    public RelayCommand<object> ExcludeSameNameThreadCommand => 
        new((obj) => ThreadActivityAction?.Invoke(this, Profile.ThreadActivityAction.ExcludeSameNameThread));
    public RelayCommand<object> FilterToThreadCommand => 
        new((obj) => ThreadActivityAction?.Invoke(this, Profile.ThreadActivityAction.FilterToThread));
    public RelayCommand<object> FilterToSameNameThreadCommand =>
        new((obj) => ThreadActivityAction?.Invoke(this, Profile.ThreadActivityAction.FilterToSameNameThread));

    private Brush disabledMarginBackColor_;
    public Brush DisabledMarginBackColor {
        get => disabledMarginBackColor_;
        set => SetField(ref disabledMarginBackColor_, value);
    }
    
    private Brush marginBackColor_;
    public Brush MarginBackColor {
        get => IsThreadIncluded ? marginBackColor_ : disabledMarginBackColor_;
        set => SetField(ref marginBackColor_, value);
    }

    public bool IsThreadIncluded {
        get => ActivityHost.IsThreadIncluded;
        set {
            if (ActivityHost.IsThreadIncluded != value) {
                ActivityHost.IsThreadIncluded = value;
                OnPropertyChanged(nameof(IsThreadIncluded));
                OnPropertyChanged(nameof(MarginBackColor));
            }
        }
    }

    public int ThreadId => ActivityHost.ThreadId;
    public string ThreadName => ActivityHost.ThreadName;

    private void Margin_MouseDown(object sender, MouseButtonEventArgs e) {
        if (e.LeftButton == MouseButtonState.Pressed &&
            e.ClickCount >= 2) {
            ThreadActivityAction?.Invoke(this, Profile.ThreadActivityAction.FilterToThread);
        }
    }

    public event PropertyChangedEventHandler PropertyChanged;

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
