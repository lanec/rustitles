using CommunityToolkit.Mvvm.ComponentModel;
using WinTitles.Core.Models;
using WinTitles.Core.Services;

namespace WinTitles.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settingsService;

    public AppSettings Settings => _settingsService.Settings;

    public SettingsViewModel(SettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public async Task SaveAsync()
    {
        await _settingsService.SaveAsync();
    }
}
