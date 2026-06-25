using System.Windows.Controls;
using KuikuiGameAssistant.Services;
using KuikuiGameAssistant.ViewModels;

namespace KuikuiGameAssistant.Views;

public partial class HardwarePage : System.Windows.Controls.UserControl
{
    private readonly HardwareInfoService _hardwareInfo;
    private readonly HardwarePageViewModel _viewModel = new();
    private bool _hasLoaded;

    public HardwarePage(HardwareInfoService hardwareInfo)
    {
        InitializeComponent();
        _hardwareInfo = hardwareInfo;
        DataContext = _viewModel;
    }

    public async void Refresh()
    {
        if (_viewModel.IsLoading)
        {
            return;
        }

        _viewModel.IsLoading = true;
        _viewModel.StatusText = _hasLoaded ? "正在刷新硬件信息..." : "正在读取硬件信息...";

        try
        {
            var sections = await _hardwareInfo.GetHardwareSectionsAsync();
            _viewModel.Sections.Clear();
            foreach (var section in sections)
            {
                _viewModel.Sections.Add(section);
            }

            _hasLoaded = true;
            _viewModel.StatusText = $"更新于 {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            _viewModel.StatusText = $"读取失败：{ex.Message}";
        }
        finally
        {
            _viewModel.IsLoading = false;
        }
    }
}
