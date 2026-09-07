using System.Text.Json;
using System.Text.Json.Serialization;

namespace SelectiveVpnRouter.Core;

public static class AppPaths
{
    public static string ProgramData => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "SelectiveVpnRouter");

    public static string LocalAppData => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SelectiveVpnRouter");

    public static string ConfigFile => Path.Combine(ProgramData, "config.json");
    public static string ConfigBackupFile => Path.Combine(ProgramData, "config.bak.json");
    public static string CrashStateFile => Path.Combine(ProgramData, "runtime", "crash-state.json");
    public static string LogDirectory => Path.Combine(ProgramData, "logs");
    public static string RuntimeDirectory => Path.Combine(ProgramData, "runtime");
    public static string UiSettingsFile => Path.Combine(LocalAppData, "ui.json");
    public static string PipeName => "SelectiveVpnRouter";
}

public static class ConfigSerializer
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static AppConfiguration LoadOrDefault(string path)
    {
        if (!File.Exists(path))
        {
            return new AppConfiguration();
        }

        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<AppConfiguration>(json, JsonOptions) ?? new AppConfiguration();
    }

    public static void Save(string path, AppConfiguration config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path))
        {
            File.Copy(path, AppPaths.ConfigBackupFile, overwrite: true);
        }

        string json = JsonSerializer.Serialize(config, JsonOptions);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, path, overwrite: true);
    }

    public static CrashState LoadCrashState(string path)
    {
        if (!File.Exists(path))
        {
            return new CrashState();
        }

        try
        {
            return JsonSerializer.Deserialize<CrashState>(File.ReadAllText(path), JsonOptions) ?? new CrashState();
        }
        catch (Exception)
        {
            return new CrashState();
        }
    }

    public static void SaveCrashState(string path, CrashState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(state, JsonOptions));
    }

    public static void ClearCrashState(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
