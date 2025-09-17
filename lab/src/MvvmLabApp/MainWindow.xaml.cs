using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace MvvmLabApp;

public partial class MainWindow : Window
{
    public ObservableCollection<PanelRegistration> Panels { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        Panels.Add(new PanelRegistration("Counter Panel", () => PanelHostPopup.Create<CounterPanel, CounterSettings>(new CounterSettings(), this, new SessionStub(), async (s, saved) => s, () => { })));
        Panels.Add(new PanelRegistration("Temperature Panel", () => PanelHostPopup.Create<TemperaturePanel, TemperatureSettings>(new TemperatureSettings(), this, new SessionStub(), async (s, saved) => s, () => { })));

        PanelList.ItemsSource = Panels;
        OpenPopupButton.Click += (_, _) =>
        {
            if (PanelList.SelectedItem is PanelRegistration reg)
            {
                reg.CreatePopup();
            }
        };
        ShowCounterInlineButton.Click += (_, _) =>
        {
            var panel = new CounterPanel();
            var settings = new CounterSettings();
            panel.Initialize(settings, new SessionStub(), async (s, saved) => s, () => { });
            InlineHost.Content = panel; // embedding control inline vs popup
        };
    }
}

public record PanelRegistration(string Title, System.Func<PanelHostPopup> CreatePopup);

// Simplified session abstraction used by factory
public interface IUISession { }
public class SessionStub : IUISession { }
