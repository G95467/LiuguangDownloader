using System.IO;

namespace LumenDownloader.App;

public sealed class AppSettings
{
    public const string DefaultUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

    public string DownloadDirectory { get; set; } = GetDefaultDownloadDirectory();
    public int Connections { get; set; } = 16;
    public int MaxConcurrent { get; set; } = 3;
    public bool WatchClipboard { get; set; } = true;
    public bool CompletionSound { get; set; } = true;
    public bool AllowInvalidCertificate { get; set; }
    public string UserAgent { get; set; } = DefaultUserAgent;

    public static string GetDefaultDownloadDirectory()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(profile))
        {
            profile = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        return Path.Combine(profile, "Downloads", "流光下载");
    }

    public AppSettings Clone() => (AppSettings)MemberwiseClone();

    public void Normalize()
    {
        DownloadDirectory = string.IsNullOrWhiteSpace(DownloadDirectory)
            ? GetDefaultDownloadDirectory()
            : DownloadDirectory.Trim().Trim('"');

        try
        {
            DownloadDirectory = Path.GetFullPath(Environment.ExpandEnvironmentVariables(DownloadDirectory));
        }
        catch
        {
            DownloadDirectory = GetDefaultDownloadDirectory();
        }

        Connections = Math.Clamp(Connections, 1, 32);
        MaxConcurrent = Math.Clamp(MaxConcurrent, 1, 8);
        UserAgent = string.IsNullOrWhiteSpace(UserAgent) ? DefaultUserAgent : UserAgent.Trim();
        if (UserAgent.Length > 700)
        {
            UserAgent = DefaultUserAgent;
        }

        try
        {
            Directory.CreateDirectory(DownloadDirectory);
        }
        catch
        {
            DownloadDirectory = GetDefaultDownloadDirectory();
            try
            {
                Directory.CreateDirectory(DownloadDirectory);
            }
            catch
            {
                // The manager will surface a meaningful error when a download is added.
            }
        }
    }
}
