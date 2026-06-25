using System.Windows.Controls;
using KuikuiGameAssistant.Models;
using KuikuiGameAssistant.Services;
using KuikuiGameAssistant.ViewModels;

namespace KuikuiGameAssistant.Views;

public partial class DashboardPage : System.Windows.Controls.UserControl
{
    private readonly TelemetryService _telemetry;
    private readonly DashboardViewModel _viewModel = new();

    public DashboardPage(TelemetryService telemetry)
    {
        InitializeComponent();
        _telemetry = telemetry;
        DataContext = _viewModel;

        _telemetry.SnapshotUpdated += Telemetry_SnapshotUpdated;
        _viewModel.Apply(_telemetry.Latest);
    }

    public void RefreshNow() => _telemetry.RefreshNow();

    private void Telemetry_SnapshotUpdated(object? sender, RealtimeSnapshot e)
    {
        Dispatcher.Invoke(() => _viewModel.Apply(e));
    }
}
