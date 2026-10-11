using System.Text.Json;

namespace SelectiveVpnRouter.Core;

/// <summary>
/// Local GUI/session preferences (%LocalAppData%\SelectiveVpnRouter\ui.json).
/// Not routing policy — never sent through Service SetConfig.
/// </summary>
public static class UiSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static bool Exists(string? path = null) => File.Exists(ResolvePath(path));

    public static UiSettings LoadOrDefault(string? path = null)
    {
        path = ResolvePath(path);
        if (!File.Exists(path))
        {
            return new UiSettings();
        }

        try
        {
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<UiSettings>(json, JsonOptions) ?? new UiSettings();
        }
        catch (JsonException)
        {
            return new UiSettings();
        }
        catch (IOException)
        {
            return new UiSettings();
        }
    }

    /// <summary>
    /// One-time initial file when ui.json is absent. Does not read legacy MinimizeToTray.
    /// </summary>
    public static UiSettings CreateInitialFromLegacyServiceUi(UiSettings? legacyFromService)
        => new()
        {
            StartMinimized = legacyFromService?.StartMinimized ?? false,
        };

    public static void EnsureInitialFileFromLegacyServiceUi(UiSettings? legacyFromService, string? path = null)
    {
        path = ResolvePath(path);
        if (File.Exists(path))
        {
            return;
        }

        Save(CreateInitialFromLegacyServiceUi(legacyFromService), path);
    }

    public static void Save(UiSettings settings, string? path = null)
    {
        path = ResolvePath(path);
        string? dir = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(dir))
        {
            throw new InvalidOperationException("Ui settings path must include a directory.");
        }

        Directory.CreateDirectory(dir);
        string json = JsonSerializer.Serialize(settings, JsonOptions);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, path, overwrite: true);
    }

    private static string ResolvePath(string? path) => path ?? AppPaths.UiSettingsFile;
}
