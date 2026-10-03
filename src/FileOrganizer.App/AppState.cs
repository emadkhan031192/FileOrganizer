using FileOrganizer.Core;

namespace FileOrganizer.App;

/// <summary>Shared application state: config + history + the single file-operation service instance.</summary>
public sealed class AppState
{
    public ConfigService ConfigService { get; } = new();
    public AppConfig Config { get; }
    public HistoryService History { get; }
    public FileOperationService Ops { get; }

    public AppState()
    {
        Config = ConfigService.Load();
        History = new HistoryService();
        Ops = new FileOperationService(History);
    }

    public void Save() => ConfigService.Save(Config);
}
