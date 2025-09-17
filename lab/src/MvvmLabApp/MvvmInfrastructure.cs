using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace MvvmLabApp;

// 1. Settings abstraction similar to your real project
public abstract class SettingsBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class CounterSettings : SettingsBase
{
    private int _incrementAmount = 1;
    public int IncrementAmount
    {
        get => _incrementAmount;
        set { if (_incrementAmount != value) { _incrementAmount = value; OnPropertyChanged(); } }
    }
}

public class TemperatureSettings : SettingsBase
{
    private bool __convertToFarenheitFromCelcius = true;
    public bool ConvertToFarenheitFromCelcius
    {
        get => __convertToFarenheitFromCelcius;
        set { if (__convertToFarenheitFromCelcius != value) { __convertToFarenheitFromCelcius = value; OnPropertyChanged(); __convertToCelciusFromFarenheit = !value; OnPropertyChanged(nameof(ConvertToCelciusFromFarenheit)); } }
    }

    private bool __convertToCelciusFromFarenheit = false;

    public bool ConvertToCelciusFromFarenheit
    {
        get => __convertToCelciusFromFarenheit;
        set { if (__convertToCelciusFromFarenheit != value) { __convertToCelciusFromFarenheit = value; OnPropertyChanged(); __convertToFarenheitFromCelcius = !value; OnPropertyChanged(nameof(ConvertToFarenheitFromCelcius)); } }
    }
}

// 2. Base ViewModel implementing INotifyPropertyChanged
public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

// 3. Panel base (UserControl) demonstrating inheritance layering
public class LabPanelBase : UserControl
{
    public SettingsBase? Settings { get; private set; }
    public IUISession? Session { get; private set; }

    private Func<SettingsBase, bool, Task<SettingsBase>>? _saveHandler;
    private Action? _closedHandler;

    public void Initialize(SettingsBase settings,
                            IUISession session,
                            Func<SettingsBase, bool, Task<SettingsBase>> saveHandler,
                            Action closedHandler)
    {
        Settings = settings;
        Session = session;
        _saveHandler = saveHandler;
        _closedHandler = closedHandler;
        OnInitialized();
    }

    protected virtual void OnInitialized() { }

    protected Task<SettingsBase>? SaveAsync(bool closeAfter)
        => _saveHandler?.Invoke(Settings!, closeAfter);

    public void NotifyClosed() => _closedHandler?.Invoke();
}

// (Removed generic typed convenience to simplify XAML inheritance mechanics.)
