namespace LumenDownloader.Engine;

public static class DownloadStatus
{
    public const string Queued = "queued";
    public const string Probing = "probing";
    public const string Downloading = "downloading";
    public const string Paused = "paused";
    public const string Completed = "completed";
    public const string Error = "error";
}

/// <summary>The persisted state of one download.</summary>
public sealed class DownloadRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Url { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string SaveDirectory { get; set; } = string.Empty;
    public string? FinalPath { get; set; }
    public string? PartPath { get; set; }
    public string? ResolvedUrl { get; set; }
    public string? MimeType { get; set; }
    public string? Error { get; set; }
    public string Status { get; set; } = DownloadStatus.Queued;

    public long TotalBytes { get; set; }
    public long DownloadedBytes { get; set; }
    public long ChunkSize { get; set; }
    public int RequestedConnections { get; set; } = 16;
    public int ActiveConnections { get; set; }
    public int TotalSegments { get; set; }
    public int CompletedSegments { get; set; }
    public bool SupportsRanges { get; set; }
    public bool IsResumable { get; set; }

    public string? ETag { get; set; }
    public string? LastModified { get; set; }
    public List<int> CompletedChunks { get; set; } = new();

    public string? Referer { get; set; }
    public string? Cookie { get; set; }
    public string? CustomHeaders { get; set; }
    public bool AllowInvalidCertificate { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? CompletedAt { get; set; }

    /// <summary>UI-only, recalculated while downloading.</summary>
    public double SpeedBytesPerSecond { get; set; }
    public double? EtaSeconds { get; set; }
}

public sealed class DownloadJobRequest
{
    public required string Url { get; init; }
    public required string SaveDirectory { get; init; }
    public string? PreferredFileName { get; init; }
    public int Connections { get; init; } = 16;
    public string UserAgent { get; init; } =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";
    public string? Referer { get; init; }
    public string? Cookie { get; init; }
    public string? CustomHeaders { get; init; }
    public bool AllowInvalidCertificate { get; init; }
    public string TaskId { get; init; } = Guid.NewGuid().ToString("N");
    public ResumeState? Resume { get; init; }
}

/// <summary>A snapshot used to continue an interrupted segmented download.</summary>
public sealed class ResumeState
{
    public string? FinalPath { get; init; }
    public string? PartPath { get; init; }
    public long TotalBytes { get; init; }
    public long ChunkSize { get; init; }
    public bool SupportsRanges { get; init; }
    public List<int> CompletedChunks { get; init; } = new();
    public string? ETag { get; init; }
    public string? LastModified { get; init; }
}

public sealed class DownloadMetadata
{
    public required string FileName { get; init; }
    public required string FinalPath { get; init; }
    public required string PartPath { get; init; }
    public required string ResolvedUrl { get; init; }
    public string? MimeType { get; init; }
    public long TotalBytes { get; init; }
    public long ChunkSize { get; init; }
    public int TotalSegments { get; init; }
    public int PlannedConnections { get; init; }
    public bool SupportsRanges { get; init; }
    public bool Resumed { get; init; }
    public bool AlreadyComplete { get; init; }
    public string? ETag { get; init; }
    public string? LastModified { get; init; }
}

public sealed class TransferProgress
{
    public long TotalBytes { get; init; }
    public long DownloadedBytes { get; init; }
    public int TotalSegments { get; init; }
    public int CompletedSegments { get; init; }
    public int ActiveConnections { get; init; }
}

public sealed class DownloadCheckpoint
{
    public long DownloadedBytes { get; init; }
    public int CompletedSegments { get; init; }
    public required int[] CompletedChunks { get; init; }
    public required string PartPath { get; init; }
}

public sealed class DownloadResult
{
    public required string FilePath { get; init; }
    public required string FileName { get; init; }
    public long TotalBytes { get; init; }
    public bool SupportsRanges { get; init; }
    public bool AlreadyExisted { get; init; }
    public TimeSpan Elapsed { get; init; }
}

public sealed class DownloadException : Exception
{
    public DownloadException(string message) : base(message) { }
    public DownloadException(string message, Exception inner) : base(message, inner) { }
}
