namespace KuikuiGameAssistant.Models;

public sealed record HardwareSection(string Title, string Icon, IReadOnlyList<HardwareItem> Items);
