using System.Text.Json;
using System.Text.Json.Serialization;

namespace FileOrganizer.Core;

/// <summary>
/// Loads/saves <see cref="AppConfig"/> as local JSON. No account, no cloud, no network (§14/§16).
/// Default location: %AppData%\FileOrganizer\config.json on Windows,
/// ~/.config/FileOrganizer/config.json elsewhere. The path is injectable for tests.
/// </summary>
public sealed class ConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string ConfigPath { get; }
    public string ConfigDirectory => Path.GetDirectoryName(ConfigPath)!;

    public ConfigService(string? configPath = null)
    {
        ConfigPath = configPath ?? DefaultConfigPath();
    }

    public static string DefaultConfigPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(appData)) // minimal Linux containers etc.
            appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return Path.Combine(appData, "FileOrganizer", "config.json");
    }

    /// <summary>Loads the config, creating and saving a default one on first run.</summary>
    public AppConfig Load()
    {
        if (!File.Exists(ConfigPath))
        {
            var fresh = AppConfig.CreateDefault();
            Save(fresh);
            return fresh;
        }

        try
        {
            var json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? AppConfig.CreateDefault();
        }
        catch (JsonException)
        {
            // Never lose the user's file: keep a copy, fall back to defaults.
            File.Copy(ConfigPath, ConfigPath + ".broken-" + DateTime.Now.ToString("yyyyMMddHHmmss"), overwrite: true);
            var fresh = AppConfig.CreateDefault();
            Save(fresh);
            return fresh;
        }
    }

    public void Save(AppConfig config)
    {
        Directory.CreateDirectory(ConfigDirectory);
        var tmp = ConfigPath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(config, JsonOptions));
        File.Move(tmp, ConfigPath, overwrite: true); // atomic-ish replace; never leaves a half-written config
    }

    /// <summary>Export Settings (§16): writes the config JSON to a user-chosen file.</summary>
    public void Export(AppConfig config, string destinationPath) =>
        File.WriteAllText(destinationPath, JsonSerializer.Serialize(config, JsonOptions));

    /// <summary>Import Settings (§16). Throws on invalid JSON so the UI can report it.</summary>
    public AppConfig Import(string sourcePath)
    {
        var json = File.ReadAllText(sourcePath);
        return JsonSerializer.Deserialize<AppConfig>(json, JsonOptions)
               ?? throw new InvalidDataException("That file does not contain a File Organizer configuration.");
    }

    public void RememberFolder(AppConfig config, string folder)
    {
        config.RecentFolders.Remove(folder);
        config.RecentFolders.Insert(0, folder);
        if (config.RecentFolders.Count > 15)
            config.RecentFolders.RemoveRange(15, config.RecentFolders.Count - 15);
    }
}
