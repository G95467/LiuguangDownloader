using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using LumenDownloader.Engine;

namespace LumenDownloader.Smoke;

internal static class Program
{
    private const string ETag = "\"smoke-v1\"";
    private static readonly byte[] Archive = CreateData(12 * 1024 * 1024, seed: 17);
    private static readonly byte[] Plain = CreateData(2_600_000, seed: 29);
    private static readonly byte[] ReportData = CreateData(3 * 1024 * 1024, seed: 41);
    private static readonly byte[] ResumeData = CreateData(8 * 1024 * 1024, seed: 53);
    private static readonly byte[] CookieData = CreateData(900_000, seed: 67);
    private static int _maxRangeConcurrency;
    private static int _activeRangeResponses;

    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args.Length > 0 && Uri.TryCreate(args[0], UriKind.Absolute, out var manualUri))
        {
            return await ManualDownloadAsync(manualUri, args.Length > 1 ? int.Parse(args[1]) : 16);
        }

        var failures = 0;
        var root = Path.Combine(Environment.CurrentDirectory, ".smoke-output");
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
        Directory.CreateDirectory(root);

        await using var server = new TestServer();
        await server.StartAsync();
        Console.WriteLine($"Test server: {server.BaseUri}");

        try
        {
            failures += await TestSegmentedAsync(server.BaseUri, root);
            failures += await TestNoRangeAsync(server.BaseUri, root);
            failures += await TestRedirectAndDispositionAsync(server.BaseUri, root);
            failures += await TestCookiesAsync(server.BaseUri, root);
            failures += await TestPauseResumeAsync(server.BaseUri, root);
            failures += await TestErrorsAsync(server.BaseUri, root);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("SMOKE TEST CRASH: " + ex);
            Console.ResetColor();
            failures++;
        }

        Console.WriteLine();
        if (failures == 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("ALL ENGINE TESTS PASSED");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"{failures} TEST(S) FAILED");
            Console.ResetColor();
        }

        return failures == 0 ? 0 : 1;
    }

    private static async Task<int> ManualDownloadAsync(Uri uri, int connections)
    {
        var dir = Path.Combine(Environment.CurrentDirectory, ".manual-output");
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
        Directory.CreateDirectory(dir);
        var lastPercent = -100d;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var job = new DownloadJob(
            new DownloadJobRequest
            {
                Url = uri.ToString(),
                SaveDirectory = dir,
                Connections = connections,
                TaskId = Guid.NewGuid().ToString("N"),
            },
            onProgress: progress =>
            {
                var percent = progress.TotalBytes > 0
                    ? progress.DownloadedBytes * 100d / progress.TotalBytes
                    : 0;
                if (percent - lastPercent >= 5 || percent >= 100)
                {
                    lastPercent = percent;
                    Console.WriteLine(
                        $"{percent,6:0.0}%  {progress.DownloadedBytes / 1048576.0,8:0.0} MB  " +
                        $"active={progress.ActiveConnections}  chunks={progress.CompletedSegments}/{progress.TotalSegments}");
                }
            },
            onMetadata: metadata =>
            {
                Console.WriteLine($"meta: name={metadata.FileName} size={metadata.TotalBytes} segments={metadata.TotalSegments} chunk={metadata.ChunkSize} ranges={metadata.SupportsRanges}");
            });

        try
        {
            var result = await job.RunAsync(timeout.Token);
            Console.WriteLine($"done: {result.FilePath} ({result.TotalBytes} bytes)");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("ERROR: " + ex);
            return 1;
        }
    }

    private static async Task<int> TestSegmentedAsync(Uri baseUri, string root)
    {
        Console.WriteLine("\n[1] Segmented download + parallel connections");
        var dir = Path.Combine(root, "segmented");
        Directory.CreateDirectory(dir);
        var maxConnections = 0;
        var progressEvents = 0;
        var job = new DownloadJob(
            new DownloadJobRequest
            {
                Url = new Uri(baseUri, "/range/archive.bin").ToString(),
                SaveDirectory = dir,
                Connections = 8,
                TaskId = Guid.NewGuid().ToString("N"),
            },
            onProgress: progress =>
            {
                Interlocked.Increment(ref progressEvents);
                InterlockedMax(ref maxConnections, progress.ActiveConnections);
            });

        using (job)
        {
            var result = await job.RunAsync();
            var ok = await CheckFileAsync(result.FilePath, Archive);
            var noPart = Directory.GetFiles(dir, "*.lgpart").Length == 0;
            var metadataOk = job.Metadata?.SupportsRanges == true && job.Metadata?.TotalSegments > 1;
            var connOk = maxConnections >= 2;
            var serverConcurrencyOk = Volatile.Read(ref _maxRangeConcurrency) >= 2;
            return Report("segmented content", ok) +
                   Report("part cleanup", noPart) +
                   Report("metadata ranges/segments", metadataOk) +
                   Report("parallel progress (>=2 active)", connOk) +
                   Report("server saw parallel range requests", serverConcurrencyOk);
        }
    }

    private static async Task<int> TestNoRangeAsync(Uri baseUri, string root)
    {
        Console.WriteLine("\n[2] Single-stream fallback when server ignores Range");
        var dir = Path.Combine(root, "plain");
        Directory.CreateDirectory(dir);
        DownloadMetadata? metadata = null;
        var tracked = new DownloadJob(new DownloadJobRequest
        {
            Url = new Uri(baseUri, "/plain/video.mp4").ToString(),
            SaveDirectory = dir,
            Connections = 8,
            TaskId = Guid.NewGuid().ToString("N"),
        }, onMetadata: m => metadata = m);

        using (tracked)
        {
            var result = await tracked.RunAsync();
            var ok = await CheckFileAsync(result.FilePath, Plain);
            return Report("plain content", ok) +
                   Report("metadata says single stream", metadata is { SupportsRanges: false }) +
                   Report("no leftover part file", Directory.GetFiles(dir, "*.lgpart").Length == 0);
        }
    }

    private static async Task<int> TestRedirectAndDispositionAsync(Uri baseUri, string root)
    {
        Console.WriteLine("\n[3] Redirect + RFC 5987 filename");
        var dir = Path.Combine(root, "redirect");
        Directory.CreateDirectory(dir);
        DownloadMetadata? metadata = null;
        var job = new DownloadJob(new DownloadJobRequest
        {
            Url = new Uri(baseUri, "/redirect/report").ToString(),
            SaveDirectory = dir,
            Connections = 4,
            TaskId = Guid.NewGuid().ToString("N"),
        }, onMetadata: m => metadata = m);

        using (job)
        {
            var result = await job.RunAsync();
            var expectedName = "测试报告 2026.bin";
            var nameOk = string.Equals(result.FileName, expectedName, StringComparison.Ordinal);
            var resolvedOk = metadata?.ResolvedUrl.Contains("report.bin", StringComparison.Ordinal) == true ||
                             metadata?.ResolvedUrl.Contains("report-download", StringComparison.Ordinal) == true;
            var hashOk = await CheckFileAsync(result.FilePath, ReportData);
            return Report("redirect resolved", resolvedOk) +
                   Report("UTF-8 Content-Disposition filename", nameOk, result.FileName) +
                   Report("redirect content", hashOk);
        }
    }

    private static async Task<int> TestCookiesAsync(Uri baseUri, string root)
    {
        Console.WriteLine("\n[4] Redirect cookies");
        var dir = Path.Combine(root, "cookies");
        Directory.CreateDirectory(dir);
        using var job = new DownloadJob(new DownloadJobRequest
        {
            Url = new Uri(baseUri, "/cookie/start").ToString(),
            SaveDirectory = dir,
            Connections = 4,
            TaskId = Guid.NewGuid().ToString("N"),
        });
        var result = await job.RunAsync();
        return Report("cookie-gated download", await CheckFileAsync(result.FilePath, CookieData));
    }

    private static async Task<int> TestPauseResumeAsync(Uri baseUri, string root)
    {
        Console.WriteLine("\n[5] Pause + checkpoint + resume");
        var dir = Path.Combine(root, "resume");
        Directory.CreateDirectory(dir);
        var taskId = Guid.NewGuid().ToString("N");
        DownloadMetadata? firstMetadata = null;
        var job = new DownloadJob(new DownloadJobRequest
        {
            Url = new Uri(baseUri, "/resume/big.bin").ToString(),
            SaveDirectory = dir,
            Connections = 4,
            TaskId = taskId,
        }, onMetadata: m => firstMetadata = m);

        using var cts = new CancellationTokenSource();
        Exception? cancelException = null;
        var runTask = Task.Run(async () =>
        {
            try
            {
                await job.RunAsync(cts.Token);
            }
            catch (Exception ex)
            {
                cancelException = ex;
            }
        });

        var wait = Stopwatch.StartNew();
        while (wait.Elapsed < TimeSpan.FromSeconds(20) &&
               (job.LastCheckpoint is null || job.LastCheckpoint.CompletedSegments < 1))
        {
            await Task.Delay(25);
        }

        cts.Cancel();
        await runTask;
        var checkpoint = job.LastCheckpoint;
        using (job)
        {
            var pauseOk = cancelException is OperationCanceledException;
            var checkpointOk = checkpoint is { CompletedSegments: > 0 };
            if (checkpoint is null || firstMetadata is null)
            {
                return Report("pause cancelled", pauseOk) +
                       Report("checkpoint captured", checkpointOk, "none") +
                       Report("resume content", false);
            }

            var partExists = File.Exists(checkpoint.PartPath);
            var resume = new ResumeState
            {
                FinalPath = firstMetadata.FinalPath,
                PartPath = checkpoint.PartPath,
                TotalBytes = firstMetadata.TotalBytes,
                ChunkSize = firstMetadata.ChunkSize,
                SupportsRanges = firstMetadata.SupportsRanges,
                CompletedChunks = checkpoint.CompletedChunks.ToList(),
                ETag = firstMetadata.ETag,
                LastModified = firstMetadata.LastModified,
            };

            DownloadMetadata? secondMetadata = null;
            using var resumeJob = new DownloadJob(new DownloadJobRequest
            {
                Url = new Uri(baseUri, "/resume/big.bin").ToString(),
                SaveDirectory = dir,
                Connections = 4,
                TaskId = taskId,
                Resume = resume,
            }, onMetadata: m => secondMetadata = m);

            var result = await resumeJob.RunAsync();
            var contentOk = await CheckFileAsync(result.FilePath, ResumeData);
            var resumedOk = secondMetadata?.Resumed == true && secondMetadata.AlreadyComplete == false;
            return Report("pause cancelled", pauseOk) +
                   Report("checkpoint captured", checkpointOk, $"{checkpoint.CompletedSegments} chunks") +
                   Report("part file retained", partExists) +
                   Report("resumed after pause", resumedOk) +
                   Report("resume content integrity", contentOk);
        }
    }

    private static async Task<int> TestErrorsAsync(Uri baseUri, string root)
    {
        Console.WriteLine("\n[6] Friendly HTTP errors");
        var dir = Path.Combine(root, "errors");
        Directory.CreateDirectory(dir);
        using var job = new DownloadJob(new DownloadJobRequest
        {
            Url = new Uri(baseUri, "/missing").ToString(),
            SaveDirectory = dir,
            TaskId = Guid.NewGuid().ToString("N"),
        });

        try
        {
            await job.RunAsync();
            return Report("404 throws DownloadException", false);
        }
        catch (DownloadException ex)
        {
            return Report("404 throws DownloadException", true, ex.Message) +
                   Report("404 message is readable", ex.Message.Contains("404", StringComparison.Ordinal) &&
                                                        ex.Message.Contains("不存在", StringComparison.Ordinal));
        }
    }

    private static int Report(string name, bool ok, string? detail = null)
    {
        Console.ForegroundColor = ok ? ConsoleColor.Green : ConsoleColor.Red;
        Console.Write(ok ? "  PASS  " : "  FAIL  ");
        Console.ResetColor();
        Console.WriteLine(name + (string.IsNullOrEmpty(detail) ? "" : $"  路  {detail}"));
        return ok ? 0 : 1;
    }

    private static async Task<bool> CheckFileAsync(string path, byte[] expected)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        var exists = new FileInfo(path);
        if (exists.Length != expected.Length)
        {
            return false;
        }

        await using var stream = File.OpenRead(path);
        var hashActual = await SHA256.HashDataAsync(stream);
        var hashExpected = SHA256.HashData(expected);
        return hashActual.SequenceEqual(hashExpected);
    }

    private static byte[] CreateData(int length, int seed)
    {
        var data = new byte[length];
        uint state = (uint)(seed * 2654435761);
        for (var i = 0; i < data.Length; i++)
        {
            state = state * 1664525 + 1013904223;
            data[i] = (byte)(state >> 24);
        }
        return data;
    }

    private static void InterlockedMax(ref int location, int value)
    {
        int current;
        while (value > (current = Volatile.Read(ref location)) &&
               Interlocked.CompareExchange(ref location, value, current) != current)
        {
        }
    }

    private sealed class TestServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _cts = new();
        private Task? _acceptLoop;

        public Uri BaseUri { get; private set; } = null!;

        public Task StartAsync()
        {
            _listener.Start();
            var endpoint = (IPEndPoint)_listener.LocalEndpoint;
            BaseUri = new Uri($"http://127.0.0.1:{endpoint.Port}/");
            _acceptLoop = Task.Run(AcceptLoopAsync);
            return Task.CompletedTask;
        }

        private async Task AcceptLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                    _ = Task.Run(() => HandleClientAsync(client));
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
            }
        }

        private async Task HandleClientAsync(TcpClient client)
        {
            try
            {
                await using var stream = client.GetStream();
                var request = await ReadRequestAsync(stream, _cts.Token);
                if (request is null)
                {
                    return;
                }

                var path = request.Path;
                var headers = request.Headers;
                if (path == "/redirect/report")
                {
                    await WriteResponseAsync(stream, 302, "Found", Array.Empty<byte>(), new()
                    {
                        ["Location"] = "/download/report.bin",
                    }, rangeHeader: null, ct: _cts.Token);
                    return;
                }

                if (path == "/cookie/start")
                {
                    await WriteResponseAsync(stream, 302, "Found", Array.Empty<byte>(), new()
                    {
                        ["Location"] = "/cookie/file.bin",
                        ["Set-Cookie"] = "smoke=ok; Path=/",
                    }, rangeHeader: null, ct: _cts.Token);
                    return;
                }

                if (path == "/cookie/file.bin")
                {
                    var cookieOk = headers.TryGetValue("Cookie", out var cookie) &&
                                   cookie.Contains("smoke=ok", StringComparison.Ordinal);
                    if (!cookieOk)
                    {
                        await WriteResponseAsync(stream, 403, "Forbidden", Encoding.ASCII.GetBytes("cookie required"),
                            new(), rangeHeader: null, ct: _cts.Token);
                        return;
                    }

                    await WriteDataResponseAsync(stream, CookieData, headers, "attachment; filename=\"cookie.bin\"",
                        rangeSupported: true, delayMs: 0, _cts.Token);
                    return;
                }

                if (path == "/range/archive.bin")
                {
                    await InterlockedCountRangeAsync(async () =>
                        await WriteDataResponseAsync(stream, Archive, headers, "attachment; filename=\"archive.bin\"",
                            rangeSupported: true, delayMs: 1, _cts.Token));
                    return;
                }

                if (path == "/download/report.bin")
                {
                    await WriteDataResponseAsync(stream, ReportData, headers,
                        "attachment; filename=\"fallback.bin\"; filename*=UTF-8''%E6%B5%8B%E8%AF%95%E6%8A%A5%E5%91%8A%202026.bin",
                        rangeSupported: true, delayMs: 0, _cts.Token);
                    return;
                }

                if (path == "/plain/video.mp4")
                {
                    await WriteDataResponseAsync(stream, Plain, headers, null, rangeSupported: false, delayMs: 0,
                        _cts.Token);
                    return;
                }

                if (path == "/resume/big.bin")
                {
                    await WriteDataResponseAsync(stream, ResumeData, headers, "attachment; filename=\"big.bin\"",
                        rangeSupported: true, delayMs: 4, _cts.Token);
                    return;
                }

                await WriteResponseAsync(stream, 404, "Not Found", Encoding.ASCII.GetBytes("not found"), new(),
                    rangeHeader: null, ct: _cts.Token);
            }
            catch
            {
                // Test clients can disappear when a download is cancelled.
            }
            finally
            {
                client.Dispose();
            }
        }

        private static async Task InterlockedCountRangeAsync(Func<Task> action)
        {
            var active = Interlocked.Increment(ref _activeRangeResponses);
            InterlockedMax(ref _maxRangeConcurrency, active);
            try
            {
                await action();
            }
            finally
            {
                Interlocked.Decrement(ref _activeRangeResponses);
            }
        }

        private static async Task WriteDataResponseAsync(
            NetworkStream stream,
            byte[] data,
            Dictionary<string, string> headers,
            string? contentDisposition,
            bool rangeSupported,
            int delayMs,
            CancellationToken ct)
        {
            var start = 0L;
            var end = data.Length - 1L;
            var isPartial = false;

            if (rangeSupported && headers.TryGetValue("Range", out var rangeHeader))
            {
                var parsed = ParseRange(rangeHeader, data.Length);
                if (parsed is not null)
                {
                    start = parsed.Value.Start;
                    end = parsed.Value.End;
                    isPartial = true;
                    if (headers.TryGetValue("If-Range", out var ifRange) && ifRange != ETag)
                    {
                        isPartial = false;
                        start = 0;
                        end = data.Length - 1L;
                    }
                }
            }

            var length = (int)(end - start + 1);
            var responseHeaders = new Dictionary<string, string>
            {
                ["Content-Type"] = "application/octet-stream",
                ["Accept-Ranges"] = rangeSupported ? "bytes" : "none",
                ["ETag"] = ETag,
                ["Last-Modified"] = "Wed, 07 Nov 2026 01:00:00 GMT",
            };
            if (contentDisposition is not null)
            {
                responseHeaders["Content-Disposition"] = contentDisposition;
            }
            if (isPartial)
            {
                responseHeaders["Content-Range"] = $"bytes {start}-{end}/{data.Length}";
            }

            var status = isPartial ? 206 : 200;
            var statusText = isPartial ? "Partial Content" : "OK";
            var headerBuilder = new StringBuilder();
            headerBuilder.Append($"HTTP/1.1 {status} {statusText}\r\n");
            headerBuilder.Append($"Content-Length: {length}\r\n");
            headerBuilder.Append("Connection: close\r\n");
            foreach (var pair in responseHeaders)
            {
                headerBuilder.Append($"{pair.Key}: {pair.Value}\r\n");
            }
            headerBuilder.Append("\r\n");
            var headerBytes = Encoding.ASCII.GetBytes(headerBuilder.ToString());
            await stream.WriteAsync(headerBytes, ct);

            var offset = (int)start;
            var remaining = length;
            var buffer = new byte[64 * 1024];
            while (remaining > 0)
            {
                var count = Math.Min(buffer.Length, remaining);
                Buffer.BlockCopy(data, offset, buffer, 0, count);
                await stream.WriteAsync(buffer.AsMemory(0, count), ct);
                offset += count;
                remaining -= count;
                if (delayMs > 0)
                {
                    await Task.Delay(delayMs, ct);
                }
            }
            await stream.FlushAsync(ct);
        }

        private static async Task WriteResponseAsync(
            NetworkStream stream,
            int status,
            string statusText,
            byte[] body,
            Dictionary<string, string> headers,
            string? rangeHeader,
            CancellationToken ct)
        {
            var builder = new StringBuilder();
            builder.Append($"HTTP/1.1 {status} {statusText}\r\n");
            builder.Append($"Content-Length: {body.Length}\r\n");
            builder.Append("Connection: close\r\n");
            foreach (var pair in headers)
            {
                builder.Append($"{pair.Key}: {pair.Value}\r\n");
            }
            builder.Append("\r\n");
            var head = Encoding.ASCII.GetBytes(builder.ToString());
            await stream.WriteAsync(head, ct);
            if (body.Length > 0)
            {
                await stream.WriteAsync(body, ct);
            }
            await stream.FlushAsync(ct);
        }

        private static (long Start, long End)? ParseRange(string header, int totalLength)
        {
            if (!header.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var value = header["bytes=".Length..].Trim();
            var dash = value.IndexOf('-');
            if (dash <= 0)
            {
                return null;
            }

            if (!long.TryParse(value[..dash], out var start) || start < 0 || start >= totalLength)
            {
                return null;
            }

            if (!long.TryParse(value[(dash + 1)..], out var end) || end < start)
            {
                end = totalLength - 1;
            }

            end = Math.Min(end, totalLength - 1);
            return (start, end);
        }

        private static async Task<HttpRequest?> ReadRequestAsync(NetworkStream stream, CancellationToken ct)
        {
            var headerBytes = new List<byte>(1024);
            var state = 0;
            while (headerBytes.Count < 64 * 1024)
            {
                var value = stream.ReadByte();
                if (value < 0)
                {
                    return null;
                }
                headerBytes.Add((byte)value);
                state = (state, value) switch
                {
                    (0, '\r') => 1,
                    (1, '\n') => 2,
                    (2, '\r') => 3,
                    (3, '\n') => 4,
                    (_, '\r') => 1,
                    _ => 0,
                };
                if (state == 4)
                {
                    break;
                }
            }

            var text = Encoding.ASCII.GetString(headerBytes.ToArray());
            var lines = text.Split("\r\n");
            if (lines.Length == 0)
            {
                return null;
            }

            var requestLine = lines[0].Split(' ');
            if (requestLine.Length < 2)
            {
                return null;
            }

            var target = requestLine[1];
            var path = target;
            var queryIndex = path.IndexOf('?');
            if (queryIndex >= 0)
            {
                path = path[..queryIndex];
            }

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 1; i < lines.Length; i++)
            {
                var colon = lines[i].IndexOf(':');
                if (colon > 0)
                {
                    headers[lines[i][..colon].Trim()] = lines[i][(colon + 1)..].Trim();
                }
            }

            await Task.Yield();
            return new HttpRequest(path, headers);
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            _listener.Stop();
            if (_acceptLoop is not null)
            {
                try
                {
                    await _acceptLoop;
                }
                catch
                {
                    // Ignore shutdown races.
                }
            }
            _cts.Dispose();
        }

        private sealed record HttpRequest(string Path, Dictionary<string, string> Headers);
    }
}
