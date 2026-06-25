using System.Collections.ObjectModel;
using KuikuiGameAssistant.Models;

namespace KuikuiGameAssistant.ViewModels;

public sealed class HardwarePageViewModel : ObservableObject
{
    private bool _isLoading;
    private string _statusText = "等待刷新";

    public ObservableCollection<HardwareSection> Sections { get; } = new();

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }
}
