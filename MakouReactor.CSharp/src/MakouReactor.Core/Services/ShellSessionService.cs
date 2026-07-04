namespace MakouReactor.Core.Services;

public interface IShellSessionService
{
    AppSettings Settings { get; }
    IRecentFilesService RecentFiles { get; }
    void Save();
}

public sealed class ShellSessionService : IShellSessionService
{
    private readonly IAppSettingsService _settingsService;

    public ShellSessionService()
        : this(new JsonAppSettingsService())
    {
    }

    public ShellSessionService(IAppSettingsService settingsService)
    {
        ArgumentNullException.ThrowIfNull(settingsService);

        _settingsService = settingsService;
        Settings = _settingsService.Load();
        RecentFiles = new RecentFilesService(Settings, _settingsService);
    }

    public AppSettings Settings { get; }

    public IRecentFilesService RecentFiles { get; }

    public void Save() => _settingsService.Save(Settings);
}
