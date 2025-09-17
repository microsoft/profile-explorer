using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace MvvmLabApp;

public class CounterViewModel : ViewModelBase
{
    private int _value;
    public int Value
    {
        get => _value;
        set { if (_value != value) { _value = value; Raise(); } }
    }
}

public partial class CounterPanel : LabPanelBase
{
    public CounterViewModel VM { get; } = new();
    public new CounterSettings Settings => (CounterSettings)base.Settings!;

    public CounterPanel()
    {
        InitializeComponent();
        DataContext = this; // Expose both VM and Settings via this
    }
    // No extra Initialize: base class handles it

    private void Increment_Click(object sender, RoutedEventArgs e)
    {
    VM.Value += Settings.IncrementAmount;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        await SaveAsync(false)!; // call base delegate
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        NotifyClosed();
        // look up a parent window and close if we are hosted in popup
        Window.GetWindow(this)?.Close();
    }
}
