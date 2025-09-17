using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace MvvmLabApp;

public class TemperatureViewModel : ViewModelBase
{
    private double _input;
    public double Input
    {
        get => _input;
        set { if (_input != value) { _input = value; Raise(); } }
    }

    private double _output;
    public double Output
    {
        get => _output;
        set { if (_output != value) { _output = value; Raise(); } }
    }
}

public partial class TemperaturePanel : LabPanelBase
{
    public TemperatureViewModel VM { get; } = new();
    public new TemperatureSettings Settings => (TemperatureSettings)base.Settings!;

    public TemperaturePanel()
    {
        InitializeComponent();
        DataContext = this; // Expose both VM and Settings via this
    }
    // No extra Initialize: base class handles it

    private void Convert_Click(object sender, RoutedEventArgs e)
    {
        if (Settings.ConvertToCelciusFromFarenheit)
        {
            VM.Output = (VM.Input - 32) * 5.0 / 9.0;
        }
        else
        {
            VM.Output = (VM.Input * 9.0 / 5.0) + 32;
        }
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
