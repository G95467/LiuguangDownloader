using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Authentication;
using System.Text;
using LumenDownloader.Engine;

namespace LumenDownloader.Engine;

/// <summary>
/// A single download job. One job opens multiple parallel ranged requests for a
/// file and writes directly into a preallocated part file at the correct offsets.
/// The job is single-use; create a new one to resume an interrupted download.
/// </summary>
public sealed class DownloadJob : IDisposable
{
    private const long MinSegmentedSize = 1L * 1024 * 1024;
    private const int MaxChunkCount = 4096;
    private const int MaxRetries = 4;

    private static readonly HashSet<string> BlockedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host", "Content-Length", "Transfer-Encoding", "Connection", "Proxy-Connection",
        "Range", "If-Range", "Accept-Encoding", "Cookie", "Referer", "Upgrade", "Trailer", "TE",
    };

    private readonly DownloadJobRequest _request;
    private readonly Uri _startUri;
    private readonly string? _basicAuthorization;
    private readonly HttpClient _http;
    private readonly CookieContainer _cookies = new();
    private readonly List<KeyValuePair<string, string>> _customHeaders = new();
    private readonly Action<DownloadMetadata>? _onMetadata;
    private readonly Action<TransferProgress>? _onProgress;
    private readonly Action<DownloadCheckpoint>? _onCheckpoint;
    private readonly object _stateLock = new();
    private readonly ConcurrentDictionary<int, long> _activeChunkBytes = new();
    private readonly Stopwatch _clock = new();

    private List<int> _completedChunks = new();
    private long _completedBytes;
    private long _activeBytes;
    private long _totalBytes;
    private long _chunkSize;
    private int _segmentCount;
    private int _activeConnections;
    private long _lastProgressTicks;
    private bool _supportsRanges;
    private bool _sizeKnown;
    private bool _resumed;
    private bool _started;
    private bool _disposed;
    private string _finalPath = string.Empty;
    private string _partPath = string.Empty;
    private string _fileName = string.Empty;
    private string _resolvedUrl = string.Empty;
    private string? _mimeType;
    private string? _etag;
    private string? _lastModified;

    private sealed record ProbeResult(
        Uri FinalUri,
        string FileName,
        string? MimeType,
        long TotalBytes,
        bool SizeKnown,
        bool SupportsRanges,
        string? ETag,
        string? LastModified);

    public DownloadJob(
        DownloadJobRequest request,
        Action<DownloadMetadata>? onMetadata = null,
        Action<TransferProgress>? onProgress = null,
        Action<DownloadCheckpoint>? onCheckpoint = null)
    {
        _request = request;
        _onMetadata = onMetadata;
        _onProgress = onProgress;
        _onCheckpoint = onCheckpoint;

        (_startUri, _basicAuthorization) = NormalizeUrl(request.Url);
        ParseCustomHeaders(request.CustomHeaders);

        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(20),
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(90),
            MaxConnectionsPerServer = Math.Max(8, Math.Clamp(request.Connections, 1, 32) + 4),
            EnableMultipleHttp2Connections = true,
        };

        if (request.AllowInvalidCertificate)
        {
            handler.SslOptions.RemoteCertificateValidationCallback =
                static (_, _, _, _) => true;
        }

        _http = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan,
            DefaultRequestVersion = HttpVersion.Version11,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
        };

        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", request.UserAgent);
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "*/*");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Encoding", "identity");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8");
    }

    public DownloadMetadata? Metadata { get; private set; }
    public DownloadCheckpoint? LastCheckpoint { get; private set; }
    public DownloadResult? Result { get; private set; }
    public string Url => _startUri.ToString();

    public async Task<DownloadResult> RunAsync(CancellationToken cancellationToken = default)
    {
        if (_started)
        {
            throw new InvalidOperationException("Each download job can only be started once.");
        }

        _started = true;
        _clock.Start();

        try
        {
            Directory.CreateDirectory(_request.SaveDirectory);

            var probe = await ProbeAsync(cancellationToken).ConfigureAwait(false);
            PreparePlan(probe);

            var metadata = BuildMetadata();
            Metadata = metadata;
            _onMetadata?.Invoke(metadata);
            RaiseProgress(force: true);

            if (metadata.AlreadyComplete)
            {
                TryDeleteFile(_partPath);
                Result = new DownloadResult
                {
                    FilePath = _finalPath,
                    FileName = _fileName,
                    TotalBytes = _totalBytes,
                    SupportsRanges = _supportsRanges,
                    AlreadyExisted = true,
                    Elapsed = _clock.Elapsed,
                };
                return Result;
            }

            if (_supportsRanges && _totalBytes > 0)
            {
                await DownloadSegmentedAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await DownloadSingleStreamAsync(cancellationToken).ConfigureAwait(false);
            }

            var final = await FinalizeFileAsync(cancellationToken).ConfigureAwait(false);
            Result = new DownloadResult
            {
                FilePath = final.Path,
                FileName = final.Name,
                TotalBytes = _totalBytes,
                SupportsRanges = _supportsRanges,
                Elapsed = _clock.Elapsed,
            };
            return Result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (DownloadException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            throw new DownloadException(FriendlyNetworkError(ex), ex);
        }
        catch (OperationCanceledException ex)
        {
            throw new DownloadException("连接超时，服务器响应过慢，请稍后重试。", ex);
        }
        catch (IOException ex)
        {
            throw new DownloadException("写入文件失败，请检查磁盘空间与保存目录权限。" + Environment.NewLine + ex.Message, ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new DownloadException("没有访问保存目录的权限，请在设置中更换下载位置。", ex);
        }
        catch (Exception ex)
        {
            throw new DownloadException(ex.Message, ex);
        }
    }

    private static (Uri Uri, string? BasicAuth) NormalizeUrl(string raw)
    {
        var value = (raw ?? string.Empty).Trim().Trim('\uFEFF');
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DownloadException("下载链接不能为空。");
        }

        if (!value.Contains("://", StringComparison.Ordinal))
        {
            value = "https://" + value.TrimStart('/');
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new DownloadException("仅支持 HTTP / HTTPS 下载链接。");
        }

        if (string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new DownloadException("无法识别链接中的服务器地址。");
        }

        string? auth = null;
        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            var parts = uri.UserInfo.Split(':', 2);
            var user = Uri.UnescapeDataString(parts[0]);
            var password = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
            auth = Convert.ToBase64String(Encoding.UTF8.GetBytes(user + ":" + password));
        }

        var builder = new UriBuilder(uri)
        {
            Fragment = string.Empty,
            UserName = string.Empty,
            Password = string.Empty,
        };
        if (builder.Port == (uri.Scheme == Uri.UriSchemeHttps ? 443 : 80))
        {
            builder.Port = -1;
        }

        return (builder.Uri, auth);
    }

    private void ParseCustomHeaders(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        foreach (var line in raw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            var colon = trimmed.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            var name = trimmed[..colon].Trim();
            var value = trimmed[(colon + 1)..].Trim();
            if (name.Length == 0 || value.Length == 0 ||
                name.Any(ch => ch <= 32 || ch >= 127 || "()<>@,;:\\\"/[]?={}".Contains(ch)) ||
                value.Contains('\r') || value.Contains('\n') ||
                BlockedHeaders.Contains(name))
            {
                continue;
            }

            _customHeaders.Add(new KeyValuePair<string, string>(name, value));
            if (_customHeaders.Count >= 20)
            {
                break;
            }
        }
    }

    private async Task<ProbeResult> ProbeAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));

        var (response, finalUri) = await SendFollowingRedirectsAsync(
            _startUri,
            new ByteRange(0, 0),
            ifRange: null,
            timeout.Token).ConfigureAwait(false);

        using (response)
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            var status = response.StatusCode;
            var code = (int)status;

            if (code is 401 or 403 or 404 or 410 or 429 or >= 500)
            {
                throw new DownloadException(FilenameHelper.FriendlyHttpError(status));
            }

            if (code == 416)
            {
                var total = ParseUnsatisfiedRangeTotal(response);
                if (total == 0)
                {
                    return new ProbeResult(
                        finalUri,
                        PickFileName(response, finalUri),
                        response.Content.Headers.ContentType?.MediaType,
                        0,
                        SizeKnown: true,
                        SupportsRanges: false,
                        ETag: response.Headers.ETag?.ToString(),
                        LastModified: response.Content.Headers.LastModified?.ToString("R"));
                }

                throw new DownloadException("服务器拒绝了分段请求（HTTP 416）。这个链接可能不是直接下载地址。");
            }

            if (status != HttpStatusCode.OK && status != HttpStatusCode.PartialContent)
            {
                throw new DownloadException(status == HttpStatusCode.Redirect || code is 301 or 302 or 303 or 307 or 308
                    ? "链接重定向次数过多或目标地址无效。"
                    : FilenameHelper.FriendlyHttpError(status));
            }

            var totalBytes = 0L;
            var sizeKnown = false;
            var supportsRanges = false;
            var contentRange = response.Content.Headers.ContentRange;

            if (status == HttpStatusCode.PartialContent)
            {
                if (contentRange?.From == 0 && contentRange.Length is > 0)
                {
                    totalBytes = contentRange.Length.Value;
                    sizeKnown = true;
                    supportsRanges = true;
                }
                else if (contentRange?.Length == 0)
                {
                    totalBytes = 0;
                    sizeKnown = true;
                }
                else
                {
                    // A valid range response without a total cannot be safely segmented.
                    totalBytes = 0;
                }
            }
            else
            {
                sizeKnown = response.Content.Headers.ContentLength.HasValue;
                totalBytes = response.Content.Headers.ContentLength ?? 0;
            }

            var etag = response.Headers.ETag?.ToString();
            var lastModified = response.Content.Headers.LastModified?.ToString("R");
            var contentEncoding = response.Content.Headers.ContentEncoding
                .FirstOrDefault(x => !string.Equals(x, "identity", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(contentEncoding))
            {
                // Range boundaries apply to the encoded representation; use one stream to be safe.
                supportsRanges = false;
            }

            if (supportsRanges && totalBytes < MinSegmentedSize)
            {
                supportsRanges = false;
            }

            return new ProbeResult(
                finalUri,
                PickFileName(response, finalUri),
                response.Content.Headers.ContentType?.MediaType,
                totalBytes,
                sizeKnown,
                supportsRanges,
                etag,
                lastModified);
        }
    }

    private string PickFileName(HttpResponseMessage response, Uri finalUri)
    {
        if (!string.IsNullOrWhiteSpace(_request.PreferredFileName))
        {
            return FilenameHelper.Sanitize(_request.PreferredFileName);
        }

        var disposition = response.Content.Headers.ContentDisposition?.ToString();
        var fileName = FilenameHelper.FromContentDisposition(disposition);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = FilenameHelper.FromUrl(finalUri);
        }

        if (string.IsNullOrWhiteSpace(fileName) && !string.IsNullOrWhiteSpace(finalUri.Query))
        {
            foreach (var pair in finalUri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = pair.Split('=', 2);
                if (parts.Length == 2 &&
                    (parts[0].Equals("filename", StringComparison.OrdinalIgnoreCase) ||
                     parts[0].Equals("file", StringComparison.OrdinalIgnoreCase) ||
                     parts[0].Equals("name", StringComparison.OrdinalIgnoreCase)))
                {
                    try
                    {
                        var candidate = Uri.UnescapeDataString(parts[1]);
                        if (candidate.Contains('.'))
                        {
                            fileName = candidate;
                            break;
                        }
                    }
                    catch
                    {
                        // Ignore malformed query values.
                    }
                }
            }
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = "download";
        }

        var sanitized = FilenameHelper.Sanitize(fileName);
        if (Path.GetExtension(sanitized).Length == 0)
        {
            var ext = FilenameHelper.ExtensionForMime(response.Content.Headers.ContentType?.MediaType);
            if (ext.Length > 0)
            {
                sanitized += ext;
            }
        }

        return sanitized;
    }

    private void PreparePlan(ProbeResult probe)
    {
        _totalBytes = probe.TotalBytes;
        _sizeKnown = probe.SizeKnown;
        _supportsRanges = probe.SupportsRanges && probe.TotalBytes > 0;
        _etag = probe.ETag;
        _lastModified = probe.LastModified;
        _resolvedUrl = probe.FinalUri.ToString();
        _mimeType = probe.MimeType;

        var resume = _request.Resume;
        var canReuseValidators = resume is null ||
            (string.IsNullOrWhiteSpace(resume.ETag) && string.IsNullOrWhiteSpace(resume.LastModified)) ||
            string.Equals(resume.ETag, _etag, StringComparison.Ordinal) ||
            (string.IsNullOrWhiteSpace(resume.ETag) &&
             string.Equals(resume.LastModified, _lastModified, StringComparison.Ordinal));

        var canResumeSameFile = resume is not null &&
            _supportsRanges &&
            resume.SupportsRanges &&
            resume.TotalBytes == _totalBytes &&
            resume.TotalBytes > 0 &&
            resume.ChunkSize > 0 &&
            canReuseValidators;

        // Prefer the name/path that the user already sees in the list.
        var rememberedFinal = resume?.FinalPath;
        if (!string.IsNullOrWhiteSpace(rememberedFinal) &&
            string.Equals(Path.GetDirectoryName(rememberedFinal), Path.GetFullPath(_request.SaveDirectory), StringComparison.OrdinalIgnoreCase))
        {
            _finalPath = rememberedFinal!;
        }

        if (string.IsNullOrWhiteSpace(_finalPath))
        {
            var candidate = Path.Combine(_request.SaveDirectory, FilenameHelper.Sanitize(probe.FileName));
            _finalPath = FilenameHelper.EnsureUniquePath(_request.SaveDirectory, Path.GetFileName(candidate));
        }

        _fileName = Path.GetFileName(_finalPath);

        if (File.Exists(_finalPath))
        {
            var existingLength = new FileInfo(_finalPath).Length;
            if (_sizeKnown && existingLength == _totalBytes)
            {
                _partPath = resume?.PartPath ?? FilenameHelper.BuildPartPath(_finalPath, _request.TaskId);
                return; // AlreadyComplete is reported via metadata.
            }

            _finalPath = FilenameHelper.EnsureUniquePath(_request.SaveDirectory, _fileName);
            _fileName = Path.GetFileName(_finalPath);
            canResumeSameFile = false;
        }

        _partPath = canResumeSameFile && !string.IsNullOrWhiteSpace(resume!.PartPath)
            ? resume.PartPath!
            : FilenameHelper.BuildPartPath(_finalPath, _request.TaskId);

        if (_supportsRanges)
        {
            if (canResumeSameFile)
            {
                _chunkSize = resume!.ChunkSize;
            }
            else
            {
                _chunkSize = CalculateChunkSize(_totalBytes, _request.Connections);
            }

            _segmentCount = (int)((_totalBytes + _chunkSize - 1) / _chunkSize);
            _segmentCount = Math.Max(1, _segmentCount);

            var reusableChunks = new List<int>();
            if (canResumeSameFile &&
                File.Exists(_partPath) &&
                new FileInfo(_partPath).Length == _totalBytes)
            {
                var seen = new HashSet<int>();
                foreach (var index in resume!.CompletedChunks)
                {
                    if (index >= 0 && index < _segmentCount && seen.Add(index))
                    {
                        reusableChunks.Add(index);
                    }
                }
            }
            else
            {
                TryDeleteFile(_partPath);
            }

            lock (_stateLock)
            {
                _completedChunks = reusableChunks;
                _completedBytes = 0;
                foreach (var index in _completedChunks)
                {
                    _completedBytes += ChunkLength(index);
                }
            }

            _resumed = _completedChunks.Count > 0;
        }
        else
        {
            _chunkSize = 0;
            _segmentCount = 0;
            _resumed = false;
            TryDeleteFile(_partPath);
        }

        EnsureDiskSpace(_request.SaveDirectory, _sizeKnown ? _totalBytes : 0);
    }

    private DownloadMetadata BuildMetadata()
    {
        var alreadyComplete = _sizeKnown &&
            File.Exists(_finalPath) &&
            new FileInfo(_finalPath).Length == _totalBytes;

        int plannedConnections;
        if (_supportsRanges)
        {
            var remaining = _segmentCount - _completedChunks.Count;
            plannedConnections = Math.Max(1, Math.Min(Math.Clamp(_request.Connections, 1, 32), Math.Max(1, remaining)));
        }
        else
        {
            plannedConnections = 1;
        }

        return new DownloadMetadata
        {
            FileName = _fileName,
            FinalPath = _finalPath,
            PartPath = _partPath,
            ResolvedUrl = _resolvedUrl,
            MimeType = _mimeType,
            TotalBytes = _totalBytes,
            ChunkSize = _chunkSize,
            TotalSegments = _segmentCount,
            PlannedConnections = plannedConnections,
            SupportsRanges = _supportsRanges,
            Resumed = _resumed,
            AlreadyComplete = alreadyComplete,
            ETag = _etag,
            LastModified = _lastModified,
        };
    }

    private async Task DownloadSegmentedAsync(CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            _partPath,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.Read,
            bufferSize: 1,
            FileOptions.Asynchronous | FileOptions.RandomAccess);

        if (stream.Length != _totalBytes)
        {
            stream.SetLength(_totalBytes);
        }

        var pending = new ConcurrentQueue<int>(
            Enumerable.Range(0, _segmentCount).Where(i => !_completedChunks.Contains(i)));

        if (pending.IsEmpty)
        {
            stream.SetLength(_totalBytes);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        var handle = stream.SafeFileHandle;
        using var fatalCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Exception? fatal = null;
        var workerCount = Math.Min(
            Math.Clamp(_request.Connections, 1, 32),
            pending.Count);

        var sendIfRange = _resumed && (_etag is not null || _lastModified is not null);

        async Task WorkerAsync()
        {
            while (!fatalCts.IsCancellationRequested && pending.TryDequeue(out var index))
            {
                try
                {
                    await DownloadChunkAsync(handle, index, sendIfRange, fatalCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (fatalCts.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (Interlocked.CompareExchange(ref fatal, ex, null) is null)
                    {
                        fatalCts.Cancel();
                    }
                    break;
                }
            }
        }

        var workers = Enumerable.Range(0, workerCount).Select(_ => WorkerAsync()).ToArray();
        await Task.WhenAll(workers).ConfigureAwait(false);

        if (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        if (fatal is not null)
        {
            if (fatal is DownloadException downloadException)
            {
                throw downloadException;
            }
            throw new DownloadException(FriendlyChunkError(fatal), fatal);
        }

        if (pending.Count > 0)
        {
            throw new DownloadException("部分分段没有完成，请重试以继续断点续传。");
        }

        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        if (stream.Length != _totalBytes)
        {
            stream.SetLength(_totalBytes);
        }
    }

    private async Task DownloadChunkAsync(
        Microsoft.Win32.SafeHandles.SafeFileHandle handle,
        int index,
        bool sendIfRange,
        CancellationToken cancellationToken)
    {
        var start = (long)index * _chunkSize;
        var end = Math.Min(_totalBytes - 1, start + _chunkSize - 1);
        var expected = end - start + 1;

        Interlocked.Increment(ref _activeConnections);
        RaiseProgress(force: true);

        try
        {
            for (var attempt = 1; attempt <= MaxRetries; attempt++)
            {
                _activeChunkBytes[index] = 0;
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeout.CancelAfter(TimeSpan.FromSeconds(40));

                    var (response, _) = await SendFollowingRedirectsAsync(
                        _startUri,
                        new ByteRange(start, end),
                        sendIfRange ? _etag ?? _lastModified : null,
                        timeout.Token).ConfigureAwait(false);

                    using (response)
                    {
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            throw new DownloadException("服务器上的文件内容已发生变化，请重试该任务。");
                        }

                        if (response.StatusCode != HttpStatusCode.PartialContent)
                        {
                            throw new DownloadException(FilenameHelper.FriendlyHttpError(response.StatusCode));
                        }

                        var range = response.Content.Headers.ContentRange;
                        if (range?.From != start || range.To != end)
                        {
                            throw new DownloadException("服务器返回了错误的分段范围，请重试。");
                        }

                        var contentLength = response.Content.Headers.ContentLength;
                        if (contentLength.HasValue && contentLength.Value != expected)
                        {
                            throw new DownloadException("服务器返回的分段长度不正确，请重试。");
                        }

                        await using var source = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
                        var buffer = ArrayPool<byte>.Shared.Rent(256 * 1024);
                        try
                        {
                            var position = start;
                            while (true)
                            {
                                var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), timeout.Token)
                                    .ConfigureAwait(false);
                                if (read == 0)
                                {
                                    break;
                                }

                                if (position + read > end + 1)
                                {
                                    throw new DownloadException("服务器返回了超出请求范围的数据。");
                                }

                                timeout.CancelAfter(TimeSpan.FromSeconds(60));
                                await RandomAccess.WriteAsync(handle, buffer.AsMemory(0, read), position, timeout.Token)
                                    .ConfigureAwait(false);
                                position += read;
                                _activeChunkBytes[index] = position - start;
                                RaiseProgress();
                            }

                            if (position != end + 1)
                            {
                                throw new IOException("连接提前中断，正在重试…");
                            }
                        }
                        finally
                        {
                            ArrayPool<byte>.Shared.Return(buffer);
                        }
                    }

                    _activeChunkBytes.TryRemove(index, out _);
                    int completedCount;
                    lock (_stateLock)
                    {
                        if (!_completedChunks.Contains(index))
                        {
                            _completedChunks.Add(index);
                            _completedChunks.Sort();
                            _completedBytes += expected;
                        }
                        completedCount = _completedChunks.Count;
                    }

                    RaiseCheckpoint();
                    RaiseProgress(force: true);
                    return;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (attempt < MaxRetries && IsRetryable(ex))
                {
                    _activeChunkBytes[index] = 0;
                    RaiseProgress(force: true);
                    try
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(350 * attempt), cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                }
            }

            throw new DownloadException("分段下载失败，请重试。");
        }
        finally
        {
            Interlocked.Decrement(ref _activeConnections);
            _activeChunkBytes.TryRemove(index, out _);
            RaiseProgress(force: true);
        }
    }

    private async Task DownloadSingleStreamAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(40));

        Interlocked.Exchange(ref _activeConnections, 1);
        RaiseProgress(force: true);

        try
        {
            var (response, _) = await SendFollowingRedirectsAsync(
                _startUri,
                range: null,
                ifRange: null,
                timeout.Token).ConfigureAwait(false);

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    throw new DownloadException(FilenameHelper.FriendlyHttpError(response.StatusCode));
                }

                var contentLength = response.Content.Headers.ContentLength;
                if (!_supportsRanges && contentLength.HasValue && _totalBytes == 0)
                {
                    _totalBytes = contentLength.Value;
                    _sizeKnown = true;
                }

                await using var source = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
                await using var destination = new FileStream(
                    _partPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.Read,
                    bufferSize: 256 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);

                var buffer = ArrayPool<byte>.Shared.Rent(256 * 1024);
                try
                {
                    long written = 0;
                    while (true)
                    {
                        var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), timeout.Token)
                            .ConfigureAwait(false);
                        if (read == 0)
                        {
                            break;
                        }

                        timeout.CancelAfter(TimeSpan.FromSeconds(60));
                        await destination.WriteAsync(buffer.AsMemory(0, read), timeout.Token)
                            .ConfigureAwait(false);
                        written += read;
                        Interlocked.Exchange(ref _completedBytes, written);
                        RaiseProgress();
                    }

                    await destination.FlushAsync(cancellationToken).ConfigureAwait(false);

                    if (contentLength.HasValue && written != contentLength.Value)
                    {
                        throw new DownloadException("连接提前中断，文件没有下载完整，请重试。");
                    }

                    if (_totalBytes <= 0)
                    {
                        _totalBytes = written;
                    }

                    Interlocked.Exchange(ref _completedBytes, written);
                    RaiseProgress(force: true);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _activeConnections, 0);
            RaiseProgress(force: true);
        }
    }

    private (string Path, string Name) FinalizeFile()
    {
        if (!File.Exists(_partPath))
        {
            throw new DownloadException("临时文件丢失，无法完成下载。");
        }

        var length = new FileInfo(_partPath).Length;
        if (_totalBytes > 0 && _supportsRanges && length != _totalBytes)
        {
            throw new DownloadException("文件大小校验失败，请重试。");
        }

        if (_totalBytes <= 0)
        {
            _totalBytes = length;
        }

        var finalPath = _finalPath;
        if (File.Exists(finalPath))
        {
            if (new FileInfo(finalPath).Length == _totalBytes)
            {
                TryDeleteFile(_partPath);
                return (finalPath, Path.GetFileName(finalPath));
            }

            finalPath = FilenameHelper.EnsureUniquePath(_request.SaveDirectory, Path.GetFileName(_finalPath));
        }

        Exception? last = null;
        for (var i = 0; i < 4; i++)
        {
            try
            {
                File.Move(_partPath, finalPath, overwrite: false);
                _finalPath = finalPath;
                _fileName = Path.GetFileName(finalPath);
                return (finalPath, _fileName);
            }
            catch (IOException ex)
            {
                last = ex;
                Thread.Sleep(120);
            }
        }

        throw new DownloadException("文件下载完成，但重命名失败。请检查文件是否被其他程序占用。" +
                                     (last is null ? "" : Environment.NewLine + last.Message));
    }

    private Task<(string Path, string Name)> FinalizeFileAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => FinalizeFile(), cancellationToken);
    }

    private async Task<(HttpResponseMessage Response, Uri FinalUri)> SendFollowingRedirectsAsync(
        Uri startUri,
        ByteRange? range,
        string? ifRange,
        CancellationToken cancellationToken)
    {
        var current = startUri;
        for (var redirects = 0; redirects <= 10; redirects++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, current);

            if (range.HasValue)
            {
                request.Headers.Range = new RangeHeaderValue(range.Value.Start, range.Value.End);
            }

            if (!string.IsNullOrWhiteSpace(ifRange))
            {
                request.Headers.TryAddWithoutValidation("If-Range", ifRange);
            }

            var cookie = _cookies.GetCookieHeader(current);
            if (!string.IsNullOrWhiteSpace(_request.Cookie))
            {
                cookie = string.IsNullOrWhiteSpace(cookie) ? _request.Cookie : cookie + "; " + _request.Cookie;
            }
            if (!string.IsNullOrWhiteSpace(cookie))
            {
                request.Headers.TryAddWithoutValidation("Cookie", cookie);
            }

            if (!string.IsNullOrWhiteSpace(_request.Referer))
            {
                request.Headers.TryAddWithoutValidation("Referer", _request.Referer);
            }

            if (!string.IsNullOrEmpty(_basicAuthorization) &&
                string.Equals(current.Host, _startUri.Host, StringComparison.OrdinalIgnoreCase))
            {
                request.Headers.TryAddWithoutValidation("Authorization", "Basic " + _basicAuthorization);
            }

            foreach (var header in _customHeaders)
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                request.Dispose();
                throw;
            }

            StoreCookies(current, response);

            if (IsRedirect(response.StatusCode))
            {
                var location = response.Headers.Location;
                if (location is null)
                {
                    request.Dispose();
                    return (response, current);
                }

                if (redirects == 10)
                {
                    response.Dispose();
                    request.Dispose();
                    throw new DownloadException("链接重定向次数过多。");
                }

                var next = location.IsAbsoluteUri ? location : new Uri(current, location);
                if (next.Scheme != Uri.UriSchemeHttp && next.Scheme != Uri.UriSchemeHttps)
                {
                    response.Dispose();
                    request.Dispose();
                    throw new DownloadException("重定向到了不支持的地址。");
                }

                response.Dispose();
                request.Dispose();
                current = next;
                continue;
            }

            request.Dispose();
            return (response, current);
        }

        throw new DownloadException("链接重定向次数过多。");
    }

    private void StoreCookies(Uri uri, HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            return;
        }

        foreach (var value in values)
        {
            try
            {
                _cookies.SetCookies(uri, value);
            }
            catch
            {
                // Ignore malformed cookies; a direct link may still succeed.
            }
        }
    }

    private static bool IsRedirect(HttpStatusCode status) =>
        status is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or
            HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    private long ParseUnsatisfiedRangeTotal(HttpResponseMessage response)
    {
        if (!response.Content.Headers.TryGetValues("Content-Range", out var values))
        {
            return -1;
        }

        var raw = values.FirstOrDefault();
        if (raw is null)
        {
            return -1;
        }

        var slash = raw.LastIndexOf('/');
        if (slash >= 0 && long.TryParse(raw[(slash + 1)..].Trim(), out var total))
        {
            return total;
        }

        return -1;
    }

    private static long CalculateChunkSize(long totalBytes, int connections)
    {
        var connectionCount = Math.Clamp(connections, 1, 32);
        var targetChunks = Math.Clamp(connectionCount * 6, 6, MaxChunkCount);
        var size = (long)Math.Ceiling(totalBytes / (double)targetChunks);
        // Four-megabyte chunks keep request overhead low while making pause and
        // crash checkpoints fine-grained enough that very little progress is lost.
        size = Math.Clamp(size, 512L * 1024, 4L * 1024 * 1024);
        size = (size + 65535) / 65536 * 65536;
        if (size > totalBytes)
        {
            size = totalBytes;
        }

        var count = (totalBytes + size - 1) / size;
        if (count > MaxChunkCount)
        {
            size = (long)Math.Ceiling(totalBytes / (double)MaxChunkCount);
            size = (size + 65535) / 65536 * 65536;
        }

        return Math.Max(1, size);
    }

    private long ChunkLength(int index)
    {
        var start = (long)index * _chunkSize;
        var end = Math.Min(_totalBytes - 1, start + _chunkSize - 1);
        return Math.Max(0, end - start + 1);
    }

    private void RaiseProgress(bool force = false)
    {
        if (_onProgress is null)
        {
            return;
        }

        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref _lastProgressTicks);
        if (!force && now - last < 140)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _lastProgressTicks, now, last) != last)
        {
            return;
        }

        var active = 0L;
        foreach (var pair in _activeChunkBytes)
        {
            active += pair.Value;
        }

        Interlocked.Exchange(ref _activeBytes, active);
        var downloaded = Interlocked.Read(ref _completedBytes) + active;
        if (_totalBytes > 0 && downloaded > _totalBytes)
        {
            downloaded = _totalBytes;
        }

        var completedSegments = 0;
        lock (_stateLock)
        {
            completedSegments = _completedChunks.Count;
        }

        _onProgress(new TransferProgress
        {
            TotalBytes = _totalBytes,
            DownloadedBytes = downloaded,
            TotalSegments = _segmentCount,
            CompletedSegments = completedSegments,
            ActiveConnections = Volatile.Read(ref _activeConnections),
        });
    }

    private void RaiseCheckpoint()
    {
        int[] snapshot;
        long bytes;
        int completed;
        lock (_stateLock)
        {
            snapshot = _completedChunks.ToArray();
            bytes = _completedBytes;
            completed = snapshot.Length;
        }

        var checkpoint = new DownloadCheckpoint
        {
            DownloadedBytes = bytes,
            CompletedSegments = completed,
            CompletedChunks = snapshot,
            PartPath = _partPath,
        };
        LastCheckpoint = checkpoint;
        _onCheckpoint?.Invoke(checkpoint);
    }

    private static bool IsRetryable(Exception ex)
    {
        if (ex is DownloadException downloadException)
        {
            var message = downloadException.Message;
            if (message.Contains("发生变化", StringComparison.Ordinal))
            {
                return false;
            }
            if (message.Contains("HTTP 401", StringComparison.Ordinal) ||
                message.Contains("HTTP 403", StringComparison.Ordinal) ||
                message.Contains("HTTP 404", StringComparison.Ordinal))
            {
                return false;
            }
            return true;
        }

        return ex is IOException or HttpRequestException or TaskCanceledException or OperationCanceledException;
    }

    private static string FriendlyNetworkError(HttpRequestException ex)
    {
        if (ex.InnerException is System.Net.Sockets.SocketException socketException)
        {
            return socketException.SocketErrorCode switch
            {
                System.Net.Sockets.SocketError.HostNotFound => "找不到服务器，请检查链接或网络连接。",
                System.Net.Sockets.SocketError.ConnectionRefused => "服务器拒绝了连接。",
                System.Net.Sockets.SocketError.TimedOut => "连接服务器超时。",
                System.Net.Sockets.SocketError.NetworkUnreachable => "网络不可达，请检查网络连接。",
                _ => "网络连接失败：" + socketException.Message,
            };
        }

        if (ex.InnerException is AuthenticationException)
        {
            return "HTTPS 证书验证失败。如果确认网站可信，可在设置中临时允许无效证书。";
        }

        return "网络请求失败，请检查链接或网络连接。" + Environment.NewLine + ex.Message;
    }

    private static string FriendlyChunkError(Exception ex)
    {
        if (ex is DownloadException)
        {
            return ex.Message;
        }

        if (ex is TaskCanceledException or TimeoutException)
        {
            return "连接超时，服务器响应过慢，请稍后重试。";
        }

        if (ex is IOException)
        {
            return "写入临时文件失败，请检查磁盘空间。";
        }

        return string.IsNullOrWhiteSpace(ex.Message) ? "下载过程中发生未知错误。" : ex.Message;
    }

    private static void EnsureDiskSpace(string directory, long totalBytes)
    {
        if (totalBytes <= 0)
        {
            return;
        }

        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(directory));
            if (string.IsNullOrWhiteSpace(root))
            {
                return;
            }

            var drive = new DriveInfo(root);
            if (drive.IsReady && drive.AvailableFreeSpace < totalBytes + 16L * 1024 * 1024)
            {
                throw new DownloadException("磁盘可用空间不足，请清理空间或更换下载位置。");
            }
        }
        catch (DownloadException)
        {
            throw;
        }
        catch
        {
            // Network shares and unusual paths may not expose DriveInfo; the write will report real errors.
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // A locked stale part file must not block a new download.
        }
    }

    private readonly record struct ByteRange(long Start, long End);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _http.Dispose();
    }
}
