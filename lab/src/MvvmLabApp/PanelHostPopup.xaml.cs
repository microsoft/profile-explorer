using System;
using System.Threading.Tasks;
using System.Windows;

namespace MvvmLabApp;

public partial class PanelHostPopup : Window
{
    private LabPanelBase? _panel;

    private PanelHostPopup()
    {
        InitializeComponent();
    }

    public static PanelHostPopup Create<TPanel, TSettings>(TSettings settings,
                                                           FrameworkElement relativeControl,
                                                           IUISession session,
                                                           Func<TSettings, bool, Task<TSettings>> newSettingsHandler,
                                                           Action panelClosedHandler,
                                                           Point? positionAdjustment = null,
                                                           bool dockLeft = false)
        where TPanel : LabPanelBase, new()
        where TSettings : SettingsBase, new()
    {
        var popup = new PanelHostPopup();
        var panel = new TPanel();
        popup._panel = panel;

        // Wrap handler to satisfy base signature (erases generics)
        async Task<SettingsBase> SaveAdapter(SettingsBase sb, bool close)
        {
            var typed = (TSettings)sb;
            var updated = await newSettingsHandler(typed, close).ConfigureAwait(false);
            return updated;
        }

    panel.Initialize(settings, session, async (sb, close) => await SaveAdapter(sb, close), panelClosedHandler);
        popup.PanelHost.Content = panel;

        // simple positioning near control
        var p = relativeControl.PointToScreen(new Point(0, 0));
        double x = p.X + (dockLeft ? -popup.Width - 10 : relativeControl.ActualWidth + 10);
        double y = p.Y;
        if (positionAdjustment is Point adj)
        {
            x += adj.X; y += adj.Y;
        }
        popup.Left = Math.Max(0, x);
        popup.Top = Math.Max(0, y);

        popup.Closed += (_, _) => panel.NotifyClosed();
        popup.Show();
        return popup;
    }
}
