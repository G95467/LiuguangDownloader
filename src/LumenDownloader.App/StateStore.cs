using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using LumenDownloader.Engine;

namespace LumenDownloader.App;

public sealed class PersistedState
{
    public int Version { get; set; } = 1;
    public AppSettings Settings { get; set; } = new();
    public List<DownloadRecord> Tasks { get; set; } = new();
}

public static class AppPaths
{
    public static string DataRoot { get; } = ResolveDataRoot();
    public static string StateFile => Path.Combine(DataRoot, "state.json");
    public static string WebViewData => Path.Combine(DataRoot, "WebView2");
    public static string LogFile => Path.Combine(DataRoot, "logs", "app.log");

    private static string ResolveDataRoot()
    {
        var overridePath = Environment.GetEnvironmentVariable("LUMEN_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return Path.GetFullPath(overridePath);
        }

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(local, "LiuguangDownloader");
    }
}

public sealed class StateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly object _logLock = new();

    public StateStore()
    {
        Directory.CreateDirectory(AppPaths.DataRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(AppPaths.LogFile)!);
        Directory.CreateDirectory(AppPaths.WebViewData);
    }

    public PersistedState Load()
    {
        try
        {
            if (!File.Exists(AppPaths.StateFile))
            {
                return new PersistedState();
            }

            var json = File.ReadAllText(AppPaths.StateFile);
            var state = JsonSerializer.Deserialize<PersistedState>(json, JsonOptions);
            if (state is null)
            {
                return new PersistedState();
            }

            state.Settings ??= new AppSettings();
            state.Tasks ??= new List<DownloadRecord>();
            return state;
        }
        catch (Exception ex)
        {
            Log("ERROR", "无法读取任务状态: " + ex.Message);
            TryBackupCorruptState();
            return new PersistedState();
        }
    }

    public void Save(PersistedState state)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataRoot);
            var json = JsonSerializer.Serialize(state, JsonOptions);
            var temp = AppPaths.StateFile + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, AppPaths.StateFile, overwrite: true);
        }
        catch (Exception ex)
        {
            Log("ERROR", "无法保存任务状态: " + ex.Message);
        }
    }

    public void Log(string level, string message)
    {
        try
        {
            lock (_logLock)
            {
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";
                File.AppendAllText(AppPaths.LogFile, line);
                var info = new FileInfo(AppPaths.LogFile);
                if (info.Exists && info.Length > 1_500_000)
                {
                    var archive = Path.Combine(Path.GetDirectoryName(AppPaths.LogFile)!, "app.previous.log");
                    File.Move(AppPaths.LogFile, archive, overwrite: true);
                }
            }
        }
        catch
        {
            // Logging must never take the app down.
        }
    }

    private static void TryBackupCorruptState()
    {
        try
        {
            if (File.Exists(AppPaths.StateFile))
            {
                File.Copy(
                    AppPaths.StateFile,
                    AppPaths.StateFile + $".corrupt-{DateTime.Now:yyyyMMddHHmmss}",
                    overwrite: true);
            }
        }
        catch
        {
            // Ignore backup failures.
        }
    }
}
