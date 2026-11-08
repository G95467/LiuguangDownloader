using LumenDownloader.Engine;

namespace LumenDownloader.App;

public sealed class UiState
{
    public List<UiTask> Tasks { get; set; } = new();
    public UiSettings Settings { get; set; } = new();
    public UiStats Stats { get; set; } = new();
    public long SessionBytes { get; set; }
    public string Version { get; set; } = "1.0.0";
    public string DefaultDownloadDirectory { get; set; } = string.Empty;
}

public sealed class UiTask
{
    public string Id { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string SaveDirectory { get; set; } = string.Empty;
    public string? FinalPath { get; set; }
    public string? ResolvedUrl { get; set; }
    public string? MimeType { get; set; }
    public string? Error { get; set; }
    public string Status { get; set; } = DownloadStatus.Queued;

    public long TotalBytes { get; set; }
    public long DownloadedBytes { get; set; }
    public int RequestedConnections { get; set; }
    public int ActiveConnections { get; set; }
    public int TotalSegments { get; set; }
    public int CompletedSegments { get; set; }
    public bool SupportsRanges { get; set; }
    public bool IsResumable { get; set; }

    public double SpeedBytesPerSecond { get; set; }
    public double? EtaSeconds { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public sealed class UiSettings
{
    public string DownloadDirectory { get; set; } = string.Empty;
    public int Connections { get; set; }
    public int MaxConcurrent { get; set; }
    public bool WatchClipboard { get; set; }
    public bool CompletionSound { get; set; }
    public bool AllowInvalidCertificate { get; set; }
    public string UserAgent { get; set; } = string.Empty;
}

public sealed class UiStats
{
    public double TotalSpeed { get; set; }
    public int ActiveCount { get; set; }
    public int QueuedCount { get; set; }
    public int PausedCount { get; set; }
    public int CompletedCount { get; set; }
    public int ErrorCount { get; set; }
    public int RunningConnections { get; set; }
    public long DiskFreeBytes { get; set; }
    public long DiskTotalBytes { get; set; }
}

public sealed class AddDownloadOptions
{
    public string? SaveDirectory { get; set; }
    public string? FileName { get; set; }
    public int? Connections { get; set; }
    public string? Referer { get; set; }
    public string? Cookie { get; set; }
    public string? CustomHeaders { get; set; }
}

public sealed class ToastMessage
{
    public string Kind { get; set; } = "info";
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
