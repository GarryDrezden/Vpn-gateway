using System.Text;
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
        string? dir = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(dir))
        {
            throw new ArgumentException("Config path must include a directory.", nameof(path));
        }

        Directory.CreateDirectory(dir);
        string json = JsonSerializer.Serialize(config, JsonOptions);
        string fileName = Path.GetFileName(path);
        string tmpPath = Path.Combine(dir, fileName + ".tmp");
        string backupPath = Path.Combine(dir, Path.GetFileNameWithoutExtension(fileName) + ".bak.json");

        File.WriteAllText(tmpPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        try
        {
            if (File.Exists(path))
            {
                ReplaceExistingConfig(tmpPath, path, backupPath);
            }
            else
            {
                File.Move(tmpPath, path);
            }
        }
        finally
        {
            DeleteIfExists(tmpPath);
        }
    }

    private static void ReplaceExistingConfig(string tmpPath, string destPath, string backupPath)
    {
        try
        {
            NormalizeAttributes(backupPath);
            NormalizeAttributes(destPath);
            File.Replace(tmpPath, destPath, backupPath, ignoreMetadataErrors: true);
        }
        catch (Exception ex) when (IsRecoverableReplaceFailure(ex))
        {
            File.Move(tmpPath, destPath, overwrite: true);
            TryRefreshBackup(destPath, backupPath);
        }
    }

    private static void TryRefreshBackup(string sourcePath, string backupPath)
    {
        try
        {
            NormalizeAttributes(backupPath);
            File.Copy(sourcePath, backupPath, overwrite: true);
        }
        catch (Exception)
        {
            // Backup is best-effort and must not block the primary config save.
        }
    }

    private static bool IsRecoverableReplaceFailure(Exception ex) =>
        ex is IOException or UnauthorizedAccessException;

    private static void NormalizeAttributes(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        File.SetAttributes(path, FileAttributes.Normal);
    }

    private static void DeleteIfExists(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            NormalizeAttributes(path);
            File.Delete(path);
        }
        catch (Exception)
        {
        }
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
