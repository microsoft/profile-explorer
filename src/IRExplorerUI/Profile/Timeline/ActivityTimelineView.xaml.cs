using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
    ExcludeOtherThreads
}

public partial class ActivityTimelineView : UserControl {
    public ActivityTimelineView() {
        InitializeComponent();
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
    public RelayCommand<object> ExcludeOtherThreadsCommand => 
        new((obj) => ThreadActivityAction?.Invoke(this, Profile.ThreadActivityAction.ExcludeOtherThreads));

    private void Margin_MouseDown(object sender, MouseButtonEventArgs e) {
        if (e.LeftButton == MouseButtonState.Pressed &&
            e.ClickCount >= 2) {
            ThreadActivityAction?.Invoke(this, Profile.ThreadActivityAction.ExcludeOtherThreads);
        }
    }
}
