using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace LumenDownloader.Engine;

public static partial class FilenameHelper
{
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    private static readonly Dictionary<string, string> MimeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["application/zip"] = ".zip",
        ["application/x-zip-compressed"] = ".zip",
        ["application/x-rar-compressed"] = ".rar",
        ["application/vnd.rar"] = ".rar",
        ["application/x-7z-compressed"] = ".7z",
        ["application/pdf"] = ".pdf",
        ["application/json"] = ".json",
        ["application/xml"] = ".xml",
        ["application/octet-stream"] = ".bin",
        ["application/msword"] = ".doc",
        ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] = ".docx",
        ["application/vnd.ms-excel"] = ".xls",
        ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"] = ".xlsx",
        ["application/vnd.ms-powerpoint"] = ".ppt",
        ["application/vnd.openxmlformats-officedocument.presentationml.presentation"] = ".pptx",
        ["application/vnd.android.package-archive"] = ".apk",
        ["application/x-msdownload"] = ".exe",
        ["application/x-iso9660-image"] = ".iso",
        ["audio/mpeg"] = ".mp3",
        ["audio/mp4"] = ".m4a",
        ["audio/flac"] = ".flac",
        ["audio/ogg"] = ".ogg",
        ["audio/wav"] = ".wav",
        ["video/mp4"] = ".mp4",
        ["video/x-matroska"] = ".mkv",
        ["video/webm"] = ".webm",
        ["video/quicktime"] = ".mov",
        ["video/x-msvideo"] = ".avi",
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/gif"] = ".gif",
        ["image/webp"] = ".webp",
        ["image/svg+xml"] = ".svg",
        ["text/plain"] = ".txt",
        ["text/html"] = ".html",
        ["text/css"] = ".css",
        ["text/javascript"] = ".js",
        ["application/javascript"] = ".js",
    };

    public static string FromContentDisposition(string? header)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            return string.Empty;
        }

        try
        {
            var star = FilenameStarRegex().Match(header);
            if (star.Success)
            {
                var value = star.Groups["value"].Value.Trim().Trim('"');
                if (!string.IsNullOrEmpty(value))
                {
                    try
                    {
                        return Uri.UnescapeDataString(value);
                    }
                    catch
                    {
                        return value;
                    }
                }
            }

            var plain = FilenameRegex().Match(header);
            if (plain.Success)
            {
                var value = plain.Groups["value"].Value.Trim().Trim('"');
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }
        catch
        {
            // Malformed Content-Disposition should never break a download.
        }

        return string.Empty;
    }

    public static string FromUrl(Uri uri)
    {
        try
        {
            var name = Path.GetFileName(uri.AbsolutePath);
            return string.IsNullOrWhiteSpace(name) ? string.Empty : Uri.UnescapeDataString(name);
        }
        catch
        {
            return string.Empty;
        }
    }

    public static string ExtensionForMime(string? mime)
    {
        if (string.IsNullOrWhiteSpace(mime))
        {
            return string.Empty;
        }

        var clean = mime.Split(';')[0].Trim();
        return MimeExtensions.TryGetValue(clean, out var ext) ? ext : string.Empty;
    }

    public static string Sanitize(string? name, string? fallback = null)
    {
        var value = (name ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(value))
        {
            value = string.IsNullOrWhiteSpace(fallback) ? "download" : fallback!;
        }

        // Content-Disposition is allowed to carry a full path; keep only the leaf.
        value = value.Replace('\\', '/');
        var slash = value.LastIndexOf('/');
        if (slash >= 0)
        {
            value = value[(slash + 1)..];
        }

        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (c < 32 || c == 127 || "<>:\"/\\|?*".Contains(c))
            {
                builder.Append('_');
            }
            else
            {
                builder.Append(c);
            }
        }

        value = WhitespaceRegex().Replace(builder.ToString(), " ").Trim().TrimEnd('.', ' ');
        if (string.IsNullOrWhiteSpace(value))
        {
            value = "download";
        }

        var baseName = value;
        var extension = string.Empty;
        var dot = value.LastIndexOf('.');
        if (dot > 0 && dot < value.Length - 1)
        {
            baseName = value[..dot];
            extension = value[dot..];
        }

        if (ReservedNames.Contains(baseName))
        {
            baseName = "_" + baseName;
        }

        // Keep the extension readable while staying under the classic MAX_PATH budget.
        if (baseName.Length > 110)
        {
            baseName = baseName[..110].TrimEnd(' ', '.');
        }

        if (extension.Length > 24)
        {
            extension = extension[..24];
        }

        value = baseName + extension;
        return string.IsNullOrWhiteSpace(value) ? "download" : value;
    }

    public static string EnsureUniquePath(string directory, string fileName)
    {
        var safe = Sanitize(fileName);
        var candidate = Path.Combine(directory, safe);
        if (!File.Exists(candidate) && !Directory.Exists(candidate))
        {
            return candidate;
        }

        var extension = Path.GetExtension(safe);
        var baseName = Path.GetFileNameWithoutExtension(safe);
        for (var i = 1; i < 10_000; i++)
        {
            candidate = Path.Combine(directory, $"{baseName} ({i}){extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(directory, $"{baseName}-{DateTime.Now:HHmmssfff}{extension}");
    }

    public static string BuildPartPath(string finalPath, string taskId)
    {
        var shortId = taskId.Length > 8 ? taskId[..8] : taskId;
        return finalPath + "." + shortId + ".lgpart";
    }

    public static string FriendlyHttpError(HttpStatusCode code) => code switch
    {
        HttpStatusCode.BadRequest => "服务器拒绝了请求（HTTP 400）",
        HttpStatusCode.Unauthorized => "需要登录或授权（HTTP 401）",
        HttpStatusCode.Forbidden => "服务器拒绝访问，链接可能已过期（HTTP 403）",
        HttpStatusCode.NotFound => "文件不存在或链接已失效（HTTP 404）",
        HttpStatusCode.MethodNotAllowed => "服务器不支持该下载方式（HTTP 405）",
        HttpStatusCode.RequestTimeout => "服务器响应超时（HTTP 408）",
        HttpStatusCode.TooManyRequests => "请求过于频繁，请稍后再试（HTTP 429）",
        HttpStatusCode.InternalServerError => "服务器内部错误（HTTP 500）",
        HttpStatusCode.BadGateway => "网关错误（HTTP 502）",
        HttpStatusCode.ServiceUnavailable => "服务暂时不可用（HTTP 503）",
        HttpStatusCode.GatewayTimeout => "网关超时（HTTP 504）",
        _ => $"下载失败（HTTP {(int)code} {code}）",
    };

    [GeneratedRegex(@"filename\*\s*=\s*(?:(?<charset>[^']*)'[^']*')?(?<value>[^;]+)", RegexOptions.IgnoreCase)]
    private static partial Regex FilenameStarRegex();

    [GeneratedRegex(@"filename\s*=\s*(?<value>""(?:[^""\\]|\\.)*""|[^;]+)", RegexOptions.IgnoreCase)]
    private static partial Regex FilenameRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
