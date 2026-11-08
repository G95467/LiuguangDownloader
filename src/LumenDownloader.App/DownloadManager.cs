using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using LumenDownloader.Engine;

namespace LumenDownloader.App;

/// <summary>
/// Owns the queue, worker calls, persistence and live transfer metrics.
/// Structural changes and UI notifications happen on the WPF dispatcher; engine
/// callbacks only mutate protected records and never touch WPF directly.
/// </summary>
public sealed class DownloadManager : IDisposable
{
    private sealed class JobRuntime
    {
        public required DownloadRecord Task { get; init; }
        public required CancellationTokenSource Cts { get; init; }
        public DownloadJob? Job { get; set; }
        public bool PauseRequested { get; set; }
        public bool CancelRequested { get; set; }
        public bool DeletePartOnExit { get; set; }
    }

    private readonly object _gate = new();
    private readonly StateStore _store;
    private readonly List<DownloadRecord> _tasks;
    private readonly Dictionary<string, JobRuntime> _running = new();
    private readonly Dictionary<string, SpeedSample> _speedSamples = new();
    private readonly Dictionary<string, long> _sessionSeen = new();
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _ticks = Stopwatch.StartNew();
    private readonly string _version;

    private AppSettings _settings;
    private bool _uiDirty = true;
    private bool _saveDirty = true;
    private bool _shuttingDown;
    private bool _disposed;
    private long _sessionBytes;
    private long _lastSaveTicks;
    private long _lastDiskTicks;
    private long _diskFree;
    private long _diskTotal;
    private string _diskDirectory = string.Empty;

    private sealed class SpeedSample
    {
        public long Bytes;
        public long Timestamp;
        public double Speed;
    }

    public DownloadManager(StateStore store)
    {
        _store = store;
        _version = typeof(DownloadManager).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        var persisted = store.Load();
        _settings = persisted.Settings;
        _settings.Normalize();
        _tasks = persisted.Tasks
            .Where(t => !string.IsNullOrWhiteSpace(t.Url) && !string.IsNullOrWhiteSpace(t.Id))
            .ToList();

        NormalizeLoadedTasks();
        _lastSaveTicks = _ticks.ElapsedMilliseconds;
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(250),
        };
        _timer.Tick += Tick;
        _timer.Start();
        RefreshDiskInfo(force: true);
    }

    public event EventHandler? Changed;
    public event Action<ToastMessage>? ToastRequested;
    public event Action<DownloadRecord>? TaskCompleted;

    public AppSettings Settings
    {
        get
        {
            lock (_gate)
            {
                return _settings.Clone();
            }
        }
    }

    public int TaskCount
    {
        get
        {
            lock (_gate)
            {
                return _tasks.Count;
            }
        }
    }

    private void NormalizeLoadedTasks()
    {
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var task in _tasks)
        {
            if (!seenIds.Add(task.Id))
            {
                task.Id = Guid.NewGuid().ToString("N");
            }

            task.Url = NormalizeUrlOrOriginal(task.Url);
            try
            {
                task.Host = new Uri(task.Url).Host;
            }
            catch
            {
                task.Host = string.Empty;
            }

            task.SpeedBytesPerSecond = 0;
            task.EtaSeconds = null;
            task.ActiveConnections = 0;
            task.CompletedChunks = task.CompletedChunks?.Distinct().OrderBy(x => x).ToList() ?? new List<int>();

            if (task.Status is DownloadStatus.Probing or DownloadStatus.Downloading or DownloadStatus.Queued)
            {
                // Never surprise the user by silently restarting traffic on launch.
                task.Status = DownloadStatus.Paused;
            }

            if (task.Status == DownloadStatus.Paused || task.Status == DownloadStatus.Error)
            {
                RecomputeResumableBytes(task);
            }

            if (task.Status == DownloadStatus.Completed)
            {
                task.DownloadedBytes = Math.Max(task.DownloadedBytes, task.TotalBytes);
                task.CompletedAt ??= DateTime.Now;
            }

            _sessionSeen[task.Id] = task.DownloadedBytes;
        }

        if (_tasks.Count > 250)
        {
            var removable = _tasks
                .Where(t => t.Status == DownloadStatus.Completed)
                .OrderBy(t => t.CompletedAt ?? t.CreatedAt)
                .Take(_tasks.Count - 250)
                .Select(t => t.Id)
                .ToHashSet();
            _tasks.RemoveAll(t => removable.Contains(t.Id));
        }
    }

    public (int Added, List<string> Errors) AddDownloads(
        IEnumerable<string> rawUrls,
        AddDownloadOptions? options = null)
    {
        options ??= new AddDownloadOptions();
        var urls = rawUrls
            .SelectMany(SplitUrls)
            .Distinct(StringComparer.Ordinal)
            .Take(50)
            .ToList();

        var errors = new List<string>();
        var saveDirectory = string.IsNullOrWhiteSpace(options.SaveDirectory)
            ? Settings.DownloadDirectory
            : options.SaveDirectory.Trim();

        try
        {
            Directory.CreateDirectory(saveDirectory);
        }
        catch (Exception ex)
        {
            return (0, new List<string> { "无法使用保存目录：" + ex.Message });
        }

        if (!Path.IsPathFullyQualified(saveDirectory))
        {
            try
            {
                saveDirectory = Path.GetFullPath(saveDirectory);
            }
            catch
            {
                return (0, new List<string> { "保存目录无效。" });
            }
        }

        var added = 0;
        var toStart = new List<DownloadRecord>();
        lock (_gate)
        {
            foreach (var raw in urls)
            {
                var normalized = NormalizeUrlOrOriginal(raw);
                if (!TryCreateHttpUri(normalized, out var uri))
                {
                    errors.Add($"无效链接：{Shorten(raw, 60)}");
                    continue;
                }

                if (_tasks.Any(t =>
                        string.Equals(t.Url, normalized, StringComparison.OrdinalIgnoreCase) &&
                        t.Status is not (DownloadStatus.Completed or DownloadStatus.Error)))
                {
                    errors.Add($"已存在相同任务：{Shorten(raw, 60)}");
                    continue;
                }

                var preferredName = string.IsNullOrWhiteSpace(options.FileName) || urls.Count > 1
                    ? string.Empty
                    : FilenameHelper.Sanitize(options.FileName);

                var record = new DownloadRecord
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Url = normalized,
                    Host = uri.Host,
                    FileName = preferredName,
                    SaveDirectory = saveDirectory,
                    RequestedConnections = Math.Clamp(
                        options.Connections ?? _settings.Connections, 1, 32),
                    Referer = NullIfBlank(options.Referer),
                    Cookie = NullIfBlank(options.Cookie),
                    CustomHeaders = NullIfBlank(options.CustomHeaders),
                    AllowInvalidCertificate = _settings.AllowInvalidCertificate,
                    Status = DownloadStatus.Queued,
                    CreatedAt = DateTime.Now,
                };

                _tasks.Insert(0, record);
                _sessionSeen[record.Id] = 0;
                added++;
                toStart.Add(record);
            }

            TrimTaskListLocked();
            _uiDirty = true;
            _saveDirty = true;
        }

        if (added > 0)
        {
            SaveNow();
            StartQueuedTasks();
            NotifyChanged();
        }

        return (added, errors);
    }

    private void TrimTaskListLocked()
    {
        if (_tasks.Count <= 250)
        {
            return;
        }

        var removable = _tasks
            .Where(t => t.Status == DownloadStatus.Completed)
            .OrderBy(t => t.CompletedAt ?? t.CreatedAt)
            .Take(_tasks.Count - 250)
            .Select(t => t.Id)
            .ToHashSet();

        if (removable.Count > 0)
        {
            _tasks.RemoveAll(t => removable.Contains(t.Id));
        }
    }

    public void PauseTask(string id)
    {
        JobRuntime? runtimeToCancel = null;
        var changed = false;

        lock (_gate)
        {
            var task = FindTask(id);
            if (task is null)
            {
                return;
            }

            if (task.Status == DownloadStatus.Queued)
            {
                task.Status = DownloadStatus.Paused;
                task.SpeedBytesPerSecond = 0;
                task.EtaSeconds = null;
                task.ActiveConnections = 0;
                _speedSamples.Remove(id);
                changed = true;
                _uiDirty = true;
                _saveDirty = true;
            }
            else if (task.Status is DownloadStatus.Probing or DownloadStatus.Downloading)
            {
                if (_running.TryGetValue(id, out var runtime))
                {
                    runtime.PauseRequested = true;
                    runtimeToCancel = runtime;
                }

                task.Status = DownloadStatus.Paused;
                RecomputeResumableBytes(task);
                task.SpeedBytesPerSecond = 0;
                task.EtaSeconds = null;
                task.ActiveConnections = 0;
                _speedSamples.Remove(id);
                changed = true;
                _uiDirty = true;
                _saveDirty = true;
            }
        }

        if (runtimeToCancel is not null)
        {
            try
            {
                runtimeToCancel.Cts.Cancel();
            }
            catch
            {
                // Ignore cancellation races.
            }
        }

        if (changed)
        {
            SaveNow();
            NotifyChanged();
        }
    }

    public void ResumeTask(string id)
    {
        var changed = false;
        lock (_gate)
        {
            var task = FindTask(id);
            if (task is null || task.Status is not (DownloadStatus.Paused or DownloadStatus.Error))
            {
                return;
            }

            task.Status = DownloadStatus.Queued;
            task.Error = null;
            task.ActiveConnections = 0;
            task.SpeedBytesPerSecond = 0;
            task.EtaSeconds = null;
            if (!task.SupportsRanges)
            {
                task.DownloadedBytes = 0;
                task.CompletedSegments = 0;
                task.CompletedChunks.Clear();
            }

            _speedSamples.Remove(id);
            _sessionSeen[task.Id] = task.DownloadedBytes;
            changed = true;
            _uiDirty = true;
            _saveDirty = true;
        }

        if (changed)
        {
            SaveNow();
            StartQueuedTasks();
            NotifyChanged();
        }
    }

    public void RetryTask(string id) => ResumeTask(id);

    public void RemoveTask(string id)
    {
        DownloadRecord? removed = null;
        JobRuntime? runtimeToCancel = null;
        var deletePart = false;

        lock (_gate)
        {
            var task = FindTask(id);
            if (task is null)
            {
                return;
            }

            removed = task;
            _tasks.Remove(task);
            _speedSamples.Remove(id);
            _sessionSeen.Remove(id);
            _uiDirty = true;
            _saveDirty = true;

            if (task.Status == DownloadStatus.Completed)
            {
                deletePart = false;
            }
            else
            {
                deletePart = true;
                if (_running.TryGetValue(id, out var runtime))
                {
                    runtime.CancelRequested = true;
                    runtime.DeletePartOnExit = true;
                    runtimeToCancel = runtime;
                }
            }
        }

        if (deletePart && runtimeToCancel is null && removed is not null)
        {
            TryDeleteQuietly(removed.PartPath);
        }

        if (runtimeToCancel is not null)
        {
            try
            {
                runtimeToCancel.Cts.Cancel();
            }
            catch
            {
                // Ignore cancellation races.
            }
        }

        SaveNow();
        NotifyChanged();
    }

    public int ClearCompleted()
    {
        int removed;
        lock (_gate)
        {
            removed = _tasks.RemoveAll(t => t.Status == DownloadStatus.Completed);
            if (removed > 0)
            {
                _uiDirty = true;
                _saveDirty = true;
            }
        }

        if (removed > 0)
        {
            SaveNow();
            NotifyChanged();
        }

        return removed;
    }

    public bool UpdateSettings(AppSettings input)
    {
        var updated = input.Clone();
        updated.Normalize();
        try
        {
            Directory.CreateDirectory(updated.DownloadDirectory);
        }
        catch (Exception ex)
        {
            RaiseToast("error", "设置未保存", "保存目录不可用：" + ex.Message);
            return false;
        }

        lock (_gate)
        {
            _settings = updated;
            _diskDirectory = string.Empty;
            _uiDirty = true;
            _saveDirty = true;
        }

        RefreshDiskInfo(force: true);
        SaveNow();
        StartQueuedTasks();
        NotifyChanged();
        return true;
    }

    public DownloadRecord? GetTask(string id)
    {
        lock (_gate)
        {
            return FindTask(id) is { } task ? CloneForRead(task) : null;
        }
    }

    public UiState CreateState()
    {
        RefreshDiskInfo(force: false);

        lock (_gate)
        {
            var tasks = _tasks
                .OrderByDescending(t => t.CreatedAt)
                .Select(t => new UiTask
                {
                    Id = t.Id,
                    Url = t.Url,
                    Host = t.Host,
                    FileName = t.FileName,
                    SaveDirectory = t.SaveDirectory,
                    FinalPath = t.FinalPath,
                    ResolvedUrl = t.ResolvedUrl,
                    MimeType = t.MimeType,
                    Error = t.Error,
                    Status = t.Status,
                    TotalBytes = t.TotalBytes,
                    DownloadedBytes = t.DownloadedBytes,
                    RequestedConnections = t.RequestedConnections,
                    ActiveConnections = t.ActiveConnections,
                    TotalSegments = t.TotalSegments,
                    CompletedSegments = t.CompletedSegments,
                    SupportsRanges = t.SupportsRanges,
                    IsResumable = t.IsResumable,
                    SpeedBytesPerSecond = t.SpeedBytesPerSecond,
                    EtaSeconds = t.EtaSeconds,
                    CreatedAt = t.CreatedAt,
                    CompletedAt = t.CompletedAt,
                })
                .ToList();

            var stats = new UiStats
            {
                TotalSpeed = tasks.Sum(t => t.SpeedBytesPerSecond),
                ActiveCount = tasks.Count(t => t.Status is DownloadStatus.Probing or DownloadStatus.Downloading),
                QueuedCount = tasks.Count(t => t.Status == DownloadStatus.Queued),
                PausedCount = tasks.Count(t => t.Status == DownloadStatus.Paused),
                CompletedCount = tasks.Count(t => t.Status == DownloadStatus.Completed),
                ErrorCount = tasks.Count(t => t.Status == DownloadStatus.Error),
                RunningConnections = tasks.Sum(t => t.ActiveConnections),
                DiskFreeBytes = _diskFree,
                DiskTotalBytes = _diskTotal,
            };

            var settings = _settings;
            return new UiState
            {
                Tasks = tasks,
                Stats = stats,
                SessionBytes = _sessionBytes,
                Version = _version,
                DefaultDownloadDirectory = AppSettings.GetDefaultDownloadDirectory(),
                Settings = new UiSettings
                {
                    DownloadDirectory = settings.DownloadDirectory,
                    Connections = settings.Connections,
                    MaxConcurrent = settings.MaxConcurrent,
                    WatchClipboard = settings.WatchClipboard,
                    CompletionSound = settings.CompletionSound,
                    AllowInvalidCertificate = settings.AllowInvalidCertificate,
                    UserAgent = settings.UserAgent,
                },
            };
        }
    }

    private void StartQueuedTasks()
    {
        if (_shuttingDown || _disposed)
        {
            return;
        }

        var changed = false;
        lock (_gate)
        {
            while (_running.Count < _settings.MaxConcurrent)
            {
                var task = _tasks
                    .Where(t => t.Status == DownloadStatus.Queued && !_running.ContainsKey(t.Id))
                    .OrderBy(t => t.CreatedAt)
                    .FirstOrDefault();
                if (task is null)
                {
                    break;
                }

                try
                {
                    StartTaskLocked(task);
                    changed = true;
                }
                catch (Exception ex)
                {
                    task.Status = DownloadStatus.Error;
                    task.Error = ex.Message;
                    task.SpeedBytesPerSecond = 0;
                    task.ActiveConnections = 0;
                    _uiDirty = true;
                    _saveDirty = true;
                    _store.Log("ERROR", $"启动任务失败 {task.Url}: {ex}");
                }
            }

            if (changed)
            {
                _uiDirty = true;
                _saveDirty = true;
            }
        }

        if (changed)
        {
            NotifyChanged();
        }
    }

    private void StartTaskLocked(DownloadRecord task)
    {
        task.Status = DownloadStatus.Probing;
        task.Error = null;
        task.ActiveConnections = 0;
        task.SpeedBytesPerSecond = 0;
        task.EtaSeconds = null;

        var runtime = new JobRuntime
        {
            Task = task,
            Cts = new CancellationTokenSource(),
            DeletePartOnExit = false,
        };

        ResumeState? resume = null;
        if (task.SupportsRanges &&
            task.TotalBytes > 0 &&
            task.TotalSegments > 0 &&
            !string.IsNullOrWhiteSpace(task.PartPath))
        {
            resume = new ResumeState
            {
                FinalPath = task.FinalPath,
                PartPath = task.PartPath,
                TotalBytes = task.TotalBytes,
                ChunkSize = task.ChunkSize,
                SupportsRanges = task.SupportsRanges,
                CompletedChunks = task.CompletedChunks.ToList(),
                ETag = task.ETag,
                LastModified = task.LastModified,
            };
        }

        var request = new DownloadJobRequest
        {
            Url = task.Url,
            SaveDirectory = task.SaveDirectory,
            PreferredFileName = string.IsNullOrWhiteSpace(task.FileName) ? null : task.FileName,
            Connections = Math.Clamp(task.RequestedConnections, 1, 32),
            UserAgent = _settings.UserAgent,
            Referer = task.Referer,
            Cookie = task.Cookie,
            CustomHeaders = task.CustomHeaders,
            AllowInvalidCertificate = task.AllowInvalidCertificate || _settings.AllowInvalidCertificate,
            TaskId = task.Id,
            Resume = resume,
        };

        var job = new DownloadJob(
            request,
            onMetadata: metadata => OnJobMetadata(task, runtime, metadata),
            onProgress: progress => OnJobProgress(task, runtime, progress),
            onCheckpoint: checkpoint => OnJobCheckpoint(task, runtime, checkpoint));

        runtime.Job = job;
        _running[task.Id] = runtime;
        _sessionSeen.TryAdd(task.Id, task.DownloadedBytes);
        _ = RunTaskAsync(task, runtime);
    }

    private async Task RunTaskAsync(DownloadRecord task, JobRuntime runtime)
    {
        try
        {
            var result = await runtime.Job!.RunAsync(runtime.Cts.Token);
            OnJobCompleted(task, runtime, result);
        }
        catch (OperationCanceledException)
        {
            OnJobStopped(task, runtime);
        }
        catch (Exception ex)
        {
            OnJobFailed(task, runtime, ex);
        }
        finally
        {
            var partToDelete = (string?)null;
            lock (_gate)
            {
                if (_running.TryGetValue(task.Id, out var current) && ReferenceEquals(current, runtime))
                {
                    _running.Remove(task.Id);
                }

                if (runtime.CancelRequested && runtime.DeletePartOnExit)
                {
                    partToDelete = task.PartPath;
                }

                _uiDirty = true;
                _saveDirty = true;
            }

            try
            {
                runtime.Job?.Dispose();
                runtime.Cts.Dispose();
            }
            catch
            {
                // Ignore disposal races.
            }

            if (!string.IsNullOrWhiteSpace(partToDelete))
            {
                _ = Task.Run(() => TryDeleteQuietly(partToDelete));
            }

            SaveNow();
            StartQueuedTasks();
            NotifyChanged();
        }
    }

    private void OnJobMetadata(DownloadRecord task, JobRuntime runtime, DownloadMetadata metadata)
    {
        lock (_gate)
        {
            if (!IsActiveRuntimeLocked(task.Id, runtime))
            {
                return;
            }

            task.FileName = metadata.FileName;
            task.FinalPath = metadata.FinalPath;
            task.PartPath = metadata.PartPath;
            task.ResolvedUrl = metadata.ResolvedUrl;
            task.MimeType = metadata.MimeType;
            task.TotalBytes = metadata.TotalBytes;
            task.ChunkSize = metadata.ChunkSize;
            task.TotalSegments = metadata.TotalSegments;
            task.SupportsRanges = metadata.SupportsRanges;
            task.IsResumable = metadata.SupportsRanges && metadata.TotalBytes > 0;
            task.ETag = metadata.ETag;
            task.LastModified = metadata.LastModified;
            task.ActiveConnections = metadata.PlannedConnections;
            if (task.Status == DownloadStatus.Probing)
            {
                task.Status = DownloadStatus.Downloading;
            }

            _uiDirty = true;
            _saveDirty = true;
        }
    }

    private void OnJobProgress(DownloadRecord task, JobRuntime runtime, TransferProgress progress)
    {
        lock (_gate)
        {
            if (!IsActiveRuntimeLocked(task.Id, runtime))
            {
                return;
            }

            if (progress.TotalBytes > 0)
            {
                task.TotalBytes = progress.TotalBytes;
            }

            task.DownloadedBytes = Math.Max(0, progress.DownloadedBytes);
            task.ActiveConnections = progress.ActiveConnections;
            task.CompletedSegments = progress.CompletedSegments;
            if (progress.TotalSegments > 0)
            {
                task.TotalSegments = progress.TotalSegments;
            }

            var previous = _sessionSeen.TryGetValue(task.Id, out var value) ? value : 0;
            if (task.DownloadedBytes > previous)
            {
                _sessionBytes += task.DownloadedBytes - previous;
            }

            _sessionSeen[task.Id] = task.DownloadedBytes;
            _uiDirty = true;
            _saveDirty = true;
        }
    }

    private void OnJobCheckpoint(DownloadRecord task, JobRuntime runtime, DownloadCheckpoint checkpoint)
    {
        lock (_gate)
        {
            if (!IsActiveRuntimeLocked(task.Id, runtime))
            {
                return;
            }

            task.CompletedChunks = checkpoint.CompletedChunks.ToList();
            task.CompletedSegments = checkpoint.CompletedSegments;
            task.DownloadedBytes = checkpoint.DownloadedBytes;
            task.PartPath = checkpoint.PartPath;
            task.IsResumable = task.SupportsRanges && task.TotalBytes > 0;
            _uiDirty = true;
            _saveDirty = true;
        }
    }

    private void OnJobCompleted(DownloadRecord task, JobRuntime runtime, DownloadResult result)
    {
        var notifyToast = false;
        var completedRecord = default(DownloadRecord);

        lock (_gate)
        {
            if (!_tasks.Contains(task))
            {
                return;
            }

            task.Status = DownloadStatus.Completed;
            task.FinalPath = result.FilePath;
            task.FileName = result.FileName;
            task.TotalBytes = result.TotalBytes > 0 ? result.TotalBytes : task.TotalBytes;
            task.DownloadedBytes = task.TotalBytes;
            task.CompletedSegments = task.TotalSegments;
            task.ActiveConnections = 0;
            task.SpeedBytesPerSecond = 0;
            task.EtaSeconds = null;
            task.Error = null;
            task.CompletedAt = DateTime.Now;
            task.IsResumable = false;
            _speedSamples.Remove(task.Id);

            var previous = _sessionSeen.TryGetValue(task.Id, out var seen) ? seen : 0;
            if (task.TotalBytes > previous)
            {
                _sessionBytes += task.TotalBytes - previous;
            }
            _sessionSeen[task.Id] = task.TotalBytes;

            _uiDirty = true;
            _saveDirty = true;
            notifyToast = true;
            completedRecord = CloneForRead(task);
        }

        SaveNow();
        if (notifyToast)
        {
            RaiseToast("success", "下载完成", task.FileName);
            try
            {
                TaskCompleted?.Invoke(completedRecord!);
            }
            catch
            {
                // UI feedback must not disturb the queue.
            }
        }
    }

    private void OnJobStopped(DownloadRecord task, JobRuntime runtime)
    {
        lock (_gate)
        {
            if (!_running.TryGetValue(task.Id, out var current) || !ReferenceEquals(current, runtime))
            {
                if (runtime.CancelRequested)
                {
                    _tasks.Remove(task);
                }
                return;
            }

            if (runtime.CancelRequested)
            {
                _tasks.Remove(task);
            }
            else
            {
                task.Status = DownloadStatus.Paused;
                task.ActiveConnections = 0;
                task.SpeedBytesPerSecond = 0;
                task.EtaSeconds = null;
                if (runtime.PauseRequested || _shuttingDown)
                {
                    RecomputeResumableBytes(task);
                }
                else
                {
                    RecomputeResumableBytes(task);
                }
            }

            _speedSamples.Remove(task.Id);
            _uiDirty = true;
            _saveDirty = true;
        }

        SaveNow();
    }

    private void OnJobFailed(DownloadRecord task, JobRuntime runtime, Exception ex)
    {
        string message;
        lock (_gate)
        {
            if (!_running.TryGetValue(task.Id, out var current) || !ReferenceEquals(current, runtime))
            {
                return;
            }

            if (runtime.CancelRequested)
            {
                _tasks.Remove(task);
                _uiDirty = true;
                _saveDirty = true;
                return;
            }

            message = ex is DownloadException
                ? ex.Message.Trim()
                : "下载失败：" + ex.Message.Trim();
            task.Status = DownloadStatus.Error;
            task.Error = message;
            task.ActiveConnections = 0;
            task.SpeedBytesPerSecond = 0;
            task.EtaSeconds = null;
            RecomputeResumableBytes(task);
            _speedSamples.Remove(task.Id);
            _uiDirty = true;
            _saveDirty = true;
        }

        SaveNow();
        RaiseToast("error", "下载中断", Shorten(message, 160));
        _store.Log("ERROR", $"{task.FileName} ({task.Url}): {ex}");
    }

    private bool IsActiveRuntimeLocked(string taskId, JobRuntime runtime)
    {
        if (!_running.TryGetValue(taskId, out var current) || !ReferenceEquals(current, runtime))
        {
            return false;
        }

        var task = FindTask(taskId);
        return task is not null &&
               task.Status is DownloadStatus.Probing or DownloadStatus.Downloading;
    }

    private void Tick(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            UpdateSpeeds();
            RefreshDiskInfo(force: false);

            var shouldNotify = false;
            lock (_gate)
            {
                var active = _tasks.Any(t => t.Status is DownloadStatus.Probing or DownloadStatus.Downloading);
                if (active || _uiDirty)
                {
                    _uiDirty = false;
                    shouldNotify = true;
                }
            }

            if (_saveDirty && _ticks.ElapsedMilliseconds - _lastSaveTicks >= 1800)
            {
                SaveNow();
            }

            if (shouldNotify)
            {
                NotifyChanged();
            }
        }
        catch (Exception ex)
        {
            _store.Log("ERROR", "刷新下载状态失败: " + ex);
        }
    }

    private void UpdateSpeeds()
    {
        var now = _ticks.ElapsedMilliseconds;
        lock (_gate)
        {
            foreach (var task in _tasks)
            {
                var active = task.Status is DownloadStatus.Probing or DownloadStatus.Downloading;
                if (!active)
                {
                    task.SpeedBytesPerSecond = 0;
                    task.EtaSeconds = null;
                    _speedSamples.Remove(task.Id);
                    continue;
                }

                if (!_speedSamples.TryGetValue(task.Id, out var sample))
                {
                    _speedSamples[task.Id] = new SpeedSample
                    {
                        Bytes = task.DownloadedBytes,
                        Timestamp = now,
                        Speed = 0,
                    };
                    task.SpeedBytesPerSecond = 0;
                    continue;
                }

                var elapsed = Math.Max(1, now - sample.Timestamp) / 1000.0;
                var delta = task.DownloadedBytes - sample.Bytes;
                var instant = Math.Max(0, delta) / elapsed;
                sample.Speed = sample.Speed <= 0
                    ? instant
                    : sample.Speed * 0.62 + instant * 0.38;
                sample.Bytes = task.DownloadedBytes;
                sample.Timestamp = now;
                task.SpeedBytesPerSecond = sample.Speed;

                if (task.TotalBytes > 0 && task.DownloadedBytes < task.TotalBytes && sample.Speed > 32 * 1024)
                {
                    task.EtaSeconds = (task.TotalBytes - task.DownloadedBytes) / sample.Speed;
                }
                else
                {
                    task.EtaSeconds = null;
                }
            }
        }
    }

    private void RecomputeResumableBytes(DownloadRecord task)
    {
        if (task.SupportsRanges && task.ChunkSize > 0 && task.TotalBytes > 0 && task.TotalSegments > 0)
        {
            long bytes = 0;
            var valid = new List<int>();
            foreach (var index in task.CompletedChunks.Distinct().OrderBy(x => x))
            {
                if (index < 0 || index >= task.TotalSegments)
                {
                    continue;
                }

                var start = (long)index * task.ChunkSize;
                var end = Math.Min(task.TotalBytes - 1, start + task.ChunkSize - 1);
                if (end >= start)
                {
                    bytes += end - start + 1;
                    valid.Add(index);
                }
            }

            task.CompletedChunks = valid;
            task.DownloadedBytes = Math.Min(bytes, task.TotalBytes);
            task.CompletedSegments = valid.Count;
            task.IsResumable = task.TotalBytes > 0;
        }
        else
        {
            task.DownloadedBytes = 0;
            task.CompletedSegments = 0;
            task.CompletedChunks.Clear();
            task.IsResumable = false;
        }
    }

    private void RefreshDiskInfo(bool force)
    {
        var now = _ticks.ElapsedMilliseconds;
        var directory = Settings.DownloadDirectory;
        if (!force &&
            now - _lastDiskTicks < 30_000 &&
            string.Equals(directory, _diskDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        long free = 0;
        long total = 0;
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(directory));
            if (!string.IsNullOrWhiteSpace(root))
            {
                var drive = new DriveInfo(root);
                if (drive.IsReady)
                {
                    free = drive.AvailableFreeSpace;
                    total = drive.TotalSize;
                }
            }
        }
        catch
        {
            // Disk metrics are decorative; failure should never block downloads.
        }

        _diskFree = free;
        _diskTotal = total;
        _diskDirectory = directory;
        _lastDiskTicks = now;
    }

    private void SaveNow()
    {
        if (_disposed || _shuttingDown && _settings is null)
        {
            return;
        }

        PersistedState snapshot;
        lock (_gate)
        {
            snapshot = new PersistedState
            {
                Settings = _settings.Clone(),
                Tasks = _tasks.Select(CloneForRead).ToList(),
            };
            _saveDirty = false;
        }

        _lastSaveTicks = _ticks.ElapsedMilliseconds;
        _store.Save(snapshot);
    }

    private void NotifyChanged()
    {
        try
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            // A disposed window must not destabilize download workers.
        }
    }

    private void RaiseToast(string kind, string title, string message)
    {
        try
        {
            ToastRequested?.Invoke(new ToastMessage { Kind = kind, Title = title, Message = message });
        }
        catch
        {
            // Ignore UI failures.
        }
    }

    private DownloadRecord? FindTask(string id) =>
        _tasks.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.Ordinal));

    private static DownloadRecord CloneForRead(DownloadRecord t) => new()
    {
        Id = t.Id,
        Url = t.Url,
        Host = t.Host,
        FileName = t.FileName,
        SaveDirectory = t.SaveDirectory,
        FinalPath = t.FinalPath,
        PartPath = t.PartPath,
        ResolvedUrl = t.ResolvedUrl,
        MimeType = t.MimeType,
        Error = t.Error,
        Status = t.Status,
        TotalBytes = t.TotalBytes,
        DownloadedBytes = t.DownloadedBytes,
        ChunkSize = t.ChunkSize,
        RequestedConnections = t.RequestedConnections,
        ActiveConnections = t.ActiveConnections,
        TotalSegments = t.TotalSegments,
        CompletedSegments = t.CompletedSegments,
        SupportsRanges = t.SupportsRanges,
        IsResumable = t.IsResumable,
        ETag = t.ETag,
        LastModified = t.LastModified,
        CompletedChunks = t.CompletedChunks.ToList(),
        Referer = t.Referer,
        Cookie = t.Cookie,
        CustomHeaders = t.CustomHeaders,
        AllowInvalidCertificate = t.AllowInvalidCertificate,
        CreatedAt = t.CreatedAt,
        CompletedAt = t.CompletedAt,
        SpeedBytesPerSecond = t.SpeedBytesPerSecond,
        EtaSeconds = t.EtaSeconds,
    };

    private static IEnumerable<string> SplitUrls(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<string>();
        }

        return raw
            .Split(new[] { '\r', '\n', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim().Trim('"', '\''))
            .Where(s => s.Length > 0);
    }

    private static string NormalizeUrlOrOriginal(string raw)
    {
        var value = (raw ?? string.Empty).Trim().Trim('\uFEFF');
        if (value.Length == 0)
        {
            return value;
        }

        if (!value.Contains("://", StringComparison.Ordinal))
        {
            value = "https://" + value.TrimStart('/');
        }

        return value;
    }

    private static bool TryCreateHttpUri(string value, out Uri uri)
    {
        uri = null!;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed))
        {
            return false;
        }

        if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(parsed.Host))
        {
            return false;
        }

        uri = parsed;
        return true;
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Shorten(string value, int length) =>
        value.Length <= length ? value : value[..length] + "…";

    private static void TryDeleteQuietly(string? path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Locked temp files are harmless and will be reused/cleaned next time.
        }
    }

    public void Shutdown()
    {
        if (_shuttingDown)
        {
            return;
        }

        _shuttingDown = true;
        _timer.Stop();
        var runtimes = new List<JobRuntime>();
        lock (_gate)
        {
            foreach (var task in _tasks.Where(t => t.Status is DownloadStatus.Probing or DownloadStatus.Downloading))
            {
                task.Status = DownloadStatus.Paused;
                task.ActiveConnections = 0;
                task.SpeedBytesPerSecond = 0;
                task.EtaSeconds = null;
                RecomputeResumableBytes(task);
                if (_running.TryGetValue(task.Id, out var runtime))
                {
                    runtime.PauseRequested = true;
                    runtimes.Add(runtime);
                }
            }

            _saveDirty = true;
        }

        SaveNow();
        foreach (var runtime in runtimes)
        {
            try
            {
                runtime.Cts.Cancel();
            }
            catch
            {
                // Ignore shutdown races.
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
        _timer.Tick -= Tick;
        foreach (var runtime in _running.Values.ToList())
        {
            try
            {
                runtime.Cts.Cancel();
                runtime.Cts.Dispose();
                runtime.Job?.Dispose();
            }
            catch
            {
                // Ignore disposal races.
            }
        }
        _running.Clear();
    }
}
