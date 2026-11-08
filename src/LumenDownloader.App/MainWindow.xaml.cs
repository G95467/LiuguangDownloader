using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using LumenDownloader.Engine;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;

namespace LumenDownloader.App;

public partial class MainWindow : Window
{
    private const string AppHost = "app.liuguang.local";
    private const string AppUrl = "https://app.liuguang.local/index.html";

    private static readonly JsonSerializerOptions BridgeJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly JsonSerializerOptions ReadJson = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly DownloadManager _manager;
    private readonly StateStore _store;
    private readonly IReadOnlyList<string> _startupUrls;
    private readonly DispatcherTimer _clipboardTimer;
    private readonly List<ToastMessage> _pendingToasts = new();

    private bool _webReady;
    private bool _pageLoaded;
    private bool _revealed;
    private bool _revealScheduled;
    private bool _closing;
    private bool _captureDone;
    private bool _isDarkTheme = true;
    private IntPtr _windowHandle;
    private HwndSource? _hwndSource;
    private string _lastClipboardText = string.Empty;

    public MainWindow(IReadOnlyList<string>? startupUrls = null)
    {
        _startupUrls = startupUrls ?? Array.Empty<string>();
        InitializeComponent();
        _store = new StateStore();
        _manager = new DownloadManager(_store);
        _manager.Changed += Manager_Changed;
        _manager.ToastRequested += Manager_ToastRequested;
        _manager.TaskCompleted += Manager_TaskCompleted;

        TaskbarItemInfo = new TaskbarItemInfo();
        LoadWindowIcon();
        FitWindowToWorkArea();
        ApplyCaptureWindowSize();

        _clipboardTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(1350),
        };
        _clipboardTimer.Tick += ClipboardTimer_Tick;

        if (_startupUrls.Count > 0)
        {
            var (added, errors) = _manager.AddDownloads(_startupUrls);
            if (added > 0)
            {
                QueueToast(new ToastMessage
                {
                    Kind = "success",
                    Title = "已添加下载",
                    Message = $"{added} 个任务已加入下载队列。",
                });
            }
            if (errors.Count > 0)
            {
                QueueToast(new ToastMessage
                {
                    Kind = "error",
                    Title = "部分链接未能添加",
                    Message = string.Join("；", errors.Take(3)),
                });
            }
        }

        Loaded += OnWindowLoaded;
    }

    private void LoadWindowIcon()
    {
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "assets", "icon.ico");
            if (File.Exists(iconPath))
            {
                Icon = BitmapFrame.Create(
                    new Uri(iconPath, UriKind.Absolute),
                    BitmapCreateOptions.None,
                    BitmapCacheOption.OnLoad);
            }
        }
        catch (Exception ex)
        {
            _store.Log("WARN", "无法加载窗口图标: " + ex.Message);
        }
    }

    private void FitWindowToWorkArea()
    {
        try
        {
            var workArea = SystemParameters.WorkArea;
            Width = Math.Min(Width, Math.Max(MinWidth, workArea.Width - 36));
            Height = Math.Min(Height, Math.Max(MinHeight, workArea.Height - 36));
        }
        catch
        {
            // The default dimensions are already safe.
        }
    }

    private void ApplyCaptureWindowSize()
    {
        try
        {
            if (int.TryParse(Environment.GetEnvironmentVariable("LUMEN_WINDOW_WIDTH"), out var width) &&
                width >= MinWidth)
            {
                Width = Math.Min(width, SystemParameters.WorkArea.Width);
            }
            if (int.TryParse(Environment.GetEnvironmentVariable("LUMEN_WINDOW_HEIGHT"), out var height) &&
                height >= MinHeight)
            {
                Height = Math.Min(height, SystemParameters.WorkArea.Height);
            }
        }
        catch
        {
            // Capture-only sizing must never interfere with the app.
        }
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        StartLoadingAnimation();
        _clipboardTimer.Start();
        _ = InitializeWebViewAsync();
    }

    private void StartLoadingAnimation()
    {
        var animation = new DoubleAnimation
        {
            From = -58,
            To = 196,
            Duration = TimeSpan.FromMilliseconds(1350),
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        LoadingPulseTransform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, animation);
    }

    private void StopLoadingAnimation()
    {
        LoadingPulseTransform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, null);
        LoadingPulseTransform.X = -58;
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            var uiFolder = Path.Combine(AppContext.BaseDirectory, "ui");
            if (!Directory.Exists(uiFolder))
            {
                throw new DirectoryNotFoundException("界面资源目录缺失：" + uiFolder);
            }

            Directory.CreateDirectory(AppPaths.WebViewData);
            var creationProperties = new CoreWebView2CreationProperties
            {
                UserDataFolder = AppPaths.WebViewData,
            };
            var debugPort = Environment.GetEnvironmentVariable("LUMEN_REMOTE_DEBUG");
            if (int.TryParse(debugPort, out var port) && port is > 0 and < 65536)
            {
                creationProperties.AdditionalBrowserArguments =
                    $"--remote-debugging-port={port} --remote-allow-origins=*";
                _store.Log("WARN", $"WebView2 remote debugging is enabled on port {port}.");
            }
            WebView.CreationProperties = creationProperties;
            await WebView.EnsureCoreWebView2Async();

            var core = WebView.CoreWebView2;
            var allowDevtools = string.Equals(
                Environment.GetEnvironmentVariable("LUMEN_DEVTOOLS"),
                "1",
                StringComparison.Ordinal);

            core.Settings.AreDevToolsEnabled = allowDevtools;
            core.Settings.AreDefaultContextMenusEnabled = true;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.IsSwipeNavigationEnabled = false;
            core.Settings.IsBuiltInErrorPageEnabled = false;

            core.SetVirtualHostNameToFolderMapping(
                AppHost,
                uiFolder,
                CoreWebView2HostResourceAccessKind.Allow);

            core.WebMessageReceived += Core_WebMessageReceived;
            core.NavigationStarting += Core_NavigationStarting;
            core.NavigationCompleted += Core_NavigationCompleted;
            core.NewWindowRequested += Core_NewWindowRequested;
            core.ProcessFailed += Core_ProcessFailed;

            var theme = Environment.GetEnvironmentVariable("LUMEN_CAPTURE_THEME");
            var themeQuery = theme is "light" or "dark" ? $"?theme={theme}" : string.Empty;
            core.Navigate(AppUrl + themeQuery);
            _store.Log("INFO", "WebView2 初始化完成，加载界面。");
        }
        catch (Exception ex)
        {
            _store.Log("ERROR", "WebView2 初始化失败: " + ex);
            ShowStartupError(
                "无法启动界面。\n\n请确认系统已安装 Microsoft Edge WebView2 运行时。\n\n" + ex.Message);
        }
    }

    private void Core_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Host, AppHost, StringComparison.OrdinalIgnoreCase))
        {
            e.Cancel = true;
        }
    }

    private void Core_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
        {
            _store.Log("ERROR", $"界面加载失败: {e.WebErrorStatus}");
            ShowStartupError("界面加载失败：" + e.WebErrorStatus);
            return;
        }

        _pageLoaded = true;
        Dispatcher.BeginInvoke(() =>
        {
            if (!_webReady && !_closing)
            {
                // If the renderer is alive but hasn't introduced itself, show it anyway
                // so a JavaScript issue never leaves a permanent splash screen.
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    if (!_webReady && _pageLoaded)
                    {
                        _store.Log("WARN", "渲染界面未发送 ready 消息，强制显示界面。");
                        RevealWebView();
                    }
                };
                timer.Start();
            }
        }, DispatcherPriority.Background);
    }

    private void Core_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
    }

    private void Core_ProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        _store.Log("ERROR", $"WebView2 进程异常: {e.ProcessFailedKind}, {e.Reason}");
        if (!_closing && e.ProcessFailedKind != CoreWebView2ProcessFailedKind.BrowserProcessExited)
        {
            try
            {
                WebView.Reload();
            }
            catch
            {
                // A reload loop is worse than leaving the current page in place.
            }
        }
    }

    private void Core_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!IsTrustedSource(e.Source) || string.IsNullOrWhiteSpace(e.WebMessageAsJson) || e.WebMessageAsJson.Length > 1_000_000)
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(e.WebMessageAsJson);
            HandleBridgeMessage(document.RootElement);
        }
        catch (Exception ex)
        {
            _store.Log("ERROR", "处理界面消息失败: " + ex);
            SendToast(new ToastMessage { Kind = "error", Title = "操作失败", Message = ex.Message });
        }
    }

    private static bool IsTrustedSource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return true;
        }

        return Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
               string.Equals(uri.Host, AppHost, StringComparison.OrdinalIgnoreCase);
    }

    private void HandleBridgeMessage(JsonElement root)
    {
        var type = GetString(root, "type");
        switch (type)
        {
            case "ready":
                _webReady = true;
                _store.Log("INFO", "界面已就绪。");
                PushState();
                FlushPendingToasts();
                break;

            case "add":
                HandleAdd(root);
                break;

            case "pause":
                if (GetString(root, "id") is { } pauseId)
                {
                    _manager.PauseTask(pauseId);
                }
                break;

            case "resume":
                if (GetString(root, "id") is { } resumeId)
                {
                    _manager.ResumeTask(resumeId);
                }
                break;

            case "remove":
                if (GetString(root, "id") is { } removeId)
                {
                    _manager.RemoveTask(removeId);
                }
                break;

            case "clearCompleted":
                var removed = _manager.ClearCompleted();
                if (removed > 0)
                {
                    SendToast(new ToastMessage
                    {
                        Kind = "success",
                        Title = "已清除记录",
                        Message = $"{removed} 条已完成记录已移除，文件仍然保留。",
                    });
                }
                break;

            case "updateSettings":
                HandleSettings(root);
                break;

            case "pickFolder":
                PickFolder(root);
                break;

            case "getClipboard":
                ReturnClipboard(root);
                break;

            case "openFile":
                OpenTaskFile(GetString(root, "id"));
                break;

            case "reveal":
                RevealTask(GetString(root, "id"));
                break;

            case "openDownloadFolder":
                OpenDownloadFolder(GetString(root, "directory"));
                break;

            case "theme":
                ApplyTheme(GetString(root, "effective") ?? "dark");
                break;

            case "copy":
                var text = GetString(root, "text");
                if (!string.IsNullOrEmpty(text))
                {
                    try
                    {
                        Clipboard.SetText(text);
                    }
                    catch
                    {
                        // Clipboard can be temporarily locked by another application.
                    }
                }
                break;
        }
    }

    private void HandleAdd(JsonElement root)
    {
        var urls = new List<string>();
        if (root.TryGetProperty("urls", out var urlsElement) && urlsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in urlsElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } url)
                {
                    urls.Add(url);
                }
            }
        }

        if (urls.Count == 0)
        {
            SendToast(new ToastMessage { Kind = "error", Title = "没有下载链接", Message = "请先粘贴 HTTP / HTTPS 下载地址。" });
            return;
        }

        var options = new AddDownloadOptions();
        if (root.TryGetProperty("options", out var optionsElement) && optionsElement.ValueKind == JsonValueKind.Object)
        {
            options.SaveDirectory = GetString(optionsElement, "saveDirectory");
            options.FileName = GetString(optionsElement, "fileName");
            var connections = GetInt(optionsElement, "connections");
            if (connections.HasValue)
            {
                options.Connections = connections.Value;
            }
            options.Referer = GetString(optionsElement, "referer");
            options.Cookie = GetString(optionsElement, "cookie");
            options.CustomHeaders = GetString(optionsElement, "customHeaders");
        }

        var (added, errors) = _manager.AddDownloads(urls, options);
        if (added > 0)
        {
            SendToast(new ToastMessage
            {
                Kind = errors.Count == 0 ? "success" : "info",
                Title = errors.Count == 0 ? "已加入下载队列" : "部分任务已添加",
                Message = errors.Count == 0
                    ? $"{added} 个任务已开始接收。"
                    : $"{added} 个任务已添加，{errors.Count} 个链接被跳过。{errors[0]}",
            });
        }
        else
        {
            SendToast(new ToastMessage
            {
                Kind = "error",
                Title = "未能添加下载",
                Message = errors.Count > 0 ? string.Join("；", errors.Take(3)) : "链接格式不正确。",
            });
        }
    }

    private void HandleSettings(JsonElement root)
    {
        if (!root.TryGetProperty("settings", out var settingsElement) || settingsElement.ValueKind != JsonValueKind.Object)
        {
            SendBridge(new { type = "settingsApplied", ok = false, message = "设置内容为空。" });
            return;
        }

        var current = _manager.Settings;
        var updated = new AppSettings
        {
            DownloadDirectory = GetString(settingsElement, "downloadDirectory") ?? current.DownloadDirectory,
            Connections = GetInt(settingsElement, "connections") ?? current.Connections,
            MaxConcurrent = GetInt(settingsElement, "maxConcurrent") ?? current.MaxConcurrent,
            WatchClipboard = GetBool(settingsElement, "watchClipboard") ?? current.WatchClipboard,
            CompletionSound = GetBool(settingsElement, "completionSound") ?? current.CompletionSound,
            AllowInvalidCertificate = GetBool(settingsElement, "allowInvalidCertificate") ?? current.AllowInvalidCertificate,
            UserAgent = GetString(settingsElement, "userAgent") ?? current.UserAgent,
        };

        var applied = _manager.UpdateSettings(updated);
        SendBridge(new
        {
            type = "settingsApplied",
            ok = applied,
            message = applied ? null : "保存目录不可用，请选择其他文件夹。",
        });
    }

    private void PickFolder(JsonElement root)
    {
        var requestId = GetString(root, "requestId");
        var initial = GetString(root, "initial");
        string? path = null;
        try
        {
            var dialog = new OpenFolderDialog
            {
                Title = "选择下载文件夹",
                Multiselect = false,
            };
            if (!string.IsNullOrWhiteSpace(initial) && Directory.Exists(initial))
            {
                dialog.InitialDirectory = initial;
            }
            else
            {
                var fallback = _manager.Settings.DownloadDirectory;
                if (Directory.Exists(fallback))
                {
                    dialog.InitialDirectory = fallback;
                }
            }

            var result = dialog.ShowDialog(this);
            if (result == true)
            {
                path = dialog.FolderName;
            }
        }
        catch (Exception ex)
        {
            _store.Log("ERROR", "文件夹选择失败: " + ex.Message);
        }

        SendBridge(new
        {
            type = "folderChosen",
            requestId,
            path,
            cancelled = string.IsNullOrWhiteSpace(path),
        });
    }

    private void ReturnClipboard(JsonElement root)
    {
        var requestId = GetString(root, "requestId");
        var text = string.Empty;
        try
        {
            if (Clipboard.ContainsText(TextDataFormat.UnicodeText))
            {
                text = Clipboard.GetText(TextDataFormat.UnicodeText);
            }
        }
        catch
        {
            // Clipboard access can race with other applications.
        }

        SendBridge(new { type = "clipboardText", requestId, text });
    }

    private void OpenTaskFile(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        var task = _manager.GetTask(id);
        var path = task?.FinalPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            SendToast(new ToastMessage
            {
                Kind = "error",
                Title = "找不到文件",
                Message = "文件可能已被移动或删除。",
            });
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            SendToast(new ToastMessage { Kind = "error", Title = "无法打开文件", Message = ex.Message });
        }
    }

    private void RevealTask(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        var task = _manager.GetTask(id);
        if (task is null)
        {
            return;
        }

        if (task.Status == DownloadStatus.Completed &&
            !string.IsNullOrWhiteSpace(task.FinalPath) &&
            File.Exists(task.FinalPath))
        {
            RevealPath(task.FinalPath);
            return;
        }

        OpenDownloadFolder(task.SaveDirectory);
    }

    private void RevealPath(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            SendToast(new ToastMessage { Kind = "error", Title = "无法打开文件夹", Message = ex.Message });
        }
    }

    private void OpenDownloadFolder(string? directory)
    {
        var path = string.IsNullOrWhiteSpace(directory) ? _manager.Settings.DownloadDirectory : directory;
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            SendToast(new ToastMessage { Kind = "error", Title = "无法打开下载文件夹", Message = ex.Message });
        }
    }

    private void Manager_Changed(object? sender, EventArgs e)
    {
        PushState();
    }

    private void PushState()
    {
        if (_closing || WebView.CoreWebView2 is null)
        {
            return;
        }

        try
        {
            var state = _manager.CreateState();
            UpdateTaskbar(state);
            if (!_webReady)
            {
                return;
            }

            SendBridge(new { type = "state", data = state });
            if (!_revealed && !_revealScheduled)
            {
                _revealScheduled = true;
                _ = RevealAfterFirstStateAsync();
            }
        }
        catch (Exception ex)
        {
            _store.Log("ERROR", "推送界面状态失败: " + ex);
        }
    }

    private async Task RevealAfterFirstStateAsync()
    {
        await Task.Delay(80);
        if (_closing)
        {
            return;
        }
        RevealWebView();
        if (!_captureDone)
        {
            _captureDone = true;
            _ = CaptureIfRequestedAsync();
        }
    }

    private void RevealWebView()
    {
        if (_revealed || _closing)
        {
            return;
        }

        _revealed = true;
        WebView.Visibility = Visibility.Visible;
        LoadingOverlay.Visibility = Visibility.Collapsed;
        StopLoadingAnimation();
    }

    private void Manager_ToastRequested(ToastMessage toast)
    {
        if (!_webReady)
        {
            QueueToast(toast);
            return;
        }

        SendToast(toast);
    }

    private void QueueToast(ToastMessage toast)
    {
        _pendingToasts.Add(toast);
        if (_pendingToasts.Count > 6)
        {
            _pendingToasts.RemoveAt(0);
        }
    }

    private void FlushPendingToasts()
    {
        if (!_webReady)
        {
            return;
        }

        var pending = _pendingToasts.ToList();
        _pendingToasts.Clear();
        foreach (var toast in pending)
        {
            SendToast(toast);
        }
    }

    private void Manager_TaskCompleted(DownloadRecord task)
    {
        try
        {
            if (_manager.Settings.CompletionSound)
            {
                SystemSounds.Asterisk.Play();
            }

            if (!IsActive)
            {
                FlashTaskbar();
            }
        }
        catch
        {
            // Feedback should never interrupt a completed download.
        }
    }

    private void SendToast(ToastMessage toast)
    {
        if (_closing)
        {
            return;
        }
        if (!_webReady)
        {
            QueueToast(toast);
            return;
        }

        SendBridge(new
        {
            type = "toast",
            kind = toast.Kind,
            title = toast.Title,
            message = toast.Message,
        });
    }

    private void SendBridge(object payload)
    {
        try
        {
            if (WebView.CoreWebView2 is null)
            {
                return;
            }

            var json = JsonSerializer.Serialize(payload, BridgeJson);
            WebView.CoreWebView2.PostWebMessageAsJson(json);
        }
        catch (Exception ex)
        {
            _store.Log("ERROR", "发送界面消息失败: " + ex);
        }
    }

    private void UpdateTaskbar(UiState state)
    {
        try
        {
            if (TaskbarItemInfo is null)
            {
                return;
            }

            var active = state.Tasks.FirstOrDefault(t =>
                t.Status is DownloadStatus.Probing or DownloadStatus.Downloading);
            if (active is null)
            {
                TaskbarItemInfo.ProgressState = TaskbarItemProgressState.None;
                TaskbarItemInfo.Description = "流光下载器";
                return;
            }

            if (active.TotalBytes > 0)
            {
                TaskbarItemInfo.ProgressState = TaskbarItemProgressState.Normal;
                TaskbarItemInfo.ProgressValue = Math.Clamp(
                    active.DownloadedBytes / (double)active.TotalBytes,
                    0d,
                    1d);
            }
            else
            {
                TaskbarItemInfo.ProgressState = TaskbarItemProgressState.Indeterminate;
            }

            var speed = FormatRate(state.Stats.TotalSpeed);
            TaskbarItemInfo.Description = $"{speed} · {state.Stats.ActiveCount} 个任务";
        }
        catch
        {
            // Decorative taskbar progress is not worth surfacing errors for.
        }
    }

    private static string FormatRate(double bytesPerSecond)
    {
        if (bytesPerSecond <= 0)
        {
            return "准备中";
        }
        if (bytesPerSecond >= 1024 * 1024)
        {
            return $"{bytesPerSecond / (1024 * 1024):0.0} MB/s";
        }
        if (bytesPerSecond >= 1024)
        {
            return $"{bytesPerSecond / 1024:0} KB/s";
        }
        return $"{bytesPerSecond:0} B/s";
    }

    private void ClipboardTimer_Tick(object? sender, EventArgs e)
    {
        if (!_webReady || _closing || !IsActive)
        {
            return;
        }

        try
        {
            var settings = _manager.Settings;
            if (!settings.WatchClipboard)
            {
                return;
            }

            if (!Clipboard.ContainsText(TextDataFormat.UnicodeText))
            {
                return;
            }

            var text = Clipboard.GetText(TextDataFormat.UnicodeText).Trim();
            if (text.Length == 0 || text.Length > 4096 ||
                string.Equals(text, _lastClipboardText, StringComparison.Ordinal))
            {
                return;
            }

            _lastClipboardText = text;
            if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                return;
            }

            SendBridge(new { type = "clipboardSuggestion", url = text });
        }
        catch
        {
            // Clipboard can be locked; the next tick will try again.
        }
    }

    private async Task CaptureIfRequestedAsync()
    {
        var path = Environment.GetEnvironmentVariable("LUMEN_CAPTURE_PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (WebView.CoreWebView2 is null)
            {
                return;
            }

            var action = Environment.GetEnvironmentVariable("LUMEN_CAPTURE_ACTION");
            if (string.Equals(action, "settings", StringComparison.OrdinalIgnoreCase))
            {
                await WebView.CoreWebView2.ExecuteScriptAsync(
                    "document.getElementById('openSettingsBtn')?.click();");
            }
            else if (string.Equals(action, "options", StringComparison.OrdinalIgnoreCase))
            {
                await WebView.CoreWebView2.ExecuteScriptAsync(
                    "document.getElementById('newDownloadBtn')?.click();");
            }

            if (string.Equals(Environment.GetEnvironmentVariable("LUMEN_CAPTURE_CLEAN"), "1", StringComparison.Ordinal))
            {
                await WebView.CoreWebView2.ExecuteScriptAsync(
                    "document.querySelectorAll('.toast').forEach(el => el.remove());");
            }

            var delay = 2200;
            if (int.TryParse(Environment.GetEnvironmentVariable("LUMEN_CAPTURE_DELAY_MS"), out var customDelay))
            {
                delay = Math.Clamp(customDelay, 200, 30000);
            }

            await Task.Delay(delay);
            if (_closing || WebView.CoreWebView2 is null)
            {
                return;
            }

            var fullPath = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var pageStream = new MemoryStream();
            await WebView.CoreWebView2.CapturePreviewAsync(
                CoreWebView2CapturePreviewImageFormat.Png,
                pageStream);

            var fullWindow = string.Equals(
                Environment.GetEnvironmentVariable("LUMEN_CAPTURE_FULL"),
                "1",
                StringComparison.Ordinal);
            if (fullWindow)
            {
                pageStream.Position = 0;
                SaveComposedWindowScreenshot(pageStream, fullPath);
            }
            else
            {
                pageStream.Position = 0;
                await using var file = File.Create(fullPath);
                await pageStream.CopyToAsync(file);
            }

            _store.Log("INFO", "界面截图已保存: " + fullPath);

            if (string.Equals(Environment.GetEnvironmentVariable("LUMEN_CAPTURE_EXIT"), "1", StringComparison.Ordinal))
            {
                Close();
            }
        }
        catch (Exception ex)
        {
            _store.Log("ERROR", "界面截图失败: " + ex);
        }
    }

    private void SaveComposedWindowScreenshot(Stream pageStream, string targetPath)
    {
        var page = new BitmapImage();
        page.BeginInit();
        page.CacheOption = BitmapCacheOption.OnLoad;
        page.StreamSource = pageStream;
        page.EndInit();
        page.Freeze();

        var dpi = VisualTreeHelper.GetDpi(TitleBar);
        var titleWidth = (int)Math.Ceiling(TitleBar.ActualWidth * dpi.DpiScaleX);
        var titleHeight = (int)Math.Ceiling(TitleBar.ActualHeight * dpi.DpiScaleY);
        var titleBitmap = new RenderTargetBitmap(
            Math.Max(1, titleWidth),
            Math.Max(1, titleHeight),
            96 * dpi.DpiScaleX,
            96 * dpi.DpiScaleY,
            PixelFormats.Pbgra32);
        titleBitmap.Render(TitleBar);

        var width = Math.Max(titleBitmap.PixelWidth, page.PixelWidth);
        var height = titleBitmap.PixelHeight + page.PixelHeight;
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            var background = Resources["WindowBackgroundBrush"] as Brush ?? Brushes.Black;
            context.DrawRectangle(background, null, new Rect(0, 0, width, height));
            context.DrawImage(titleBitmap, new Rect(0, 0, titleBitmap.PixelWidth, titleBitmap.PixelHeight));
            context.DrawImage(page, new Rect(0, titleBitmap.PixelHeight, page.PixelWidth, page.PixelHeight));
        }

        var composed = new RenderTargetBitmap(
            width,
            height,
            96 * dpi.DpiScaleX,
            96 * dpi.DpiScaleY,
            PixelFormats.Pbgra32);
        composed.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(composed));
        using var file = File.Create(targetPath);
        encoder.Save(file);
    }

    private void ApplyTheme(string effective)
    {
        var dark = !string.Equals(effective, "light", StringComparison.OrdinalIgnoreCase);
        _isDarkTheme = dark;

        SetBrush("WindowBackgroundBrush", dark ? "#070A10" : "#F8FAFE");
        SetBrush("TitleLeftBrush", dark ? "#0A0E16" : "#F1F5FB");
        SetBrush("TitleMainBrush", dark ? "#070A10" : "#F8FAFE");
        SetBrush("TitleForegroundBrush", dark ? "#EDF3FF" : "#182235");
        SetBrush("TitleMutedBrush", dark ? "#627086" : "#8A96AA");
        SetBrush("TitleBorderBrush", dark ? "#1A2434" : "#E3E9F3");
        SetBrush("TitleButtonHoverBrush", dark ? "#16FFFFFF" : "#0D172A");
        SetBrush("TitleButtonPressedBrush", dark ? "#0DFFFFFF" : "#14FFFFFF");
        SetBrush("FreeBadgeBackgroundBrush", dark ? "#121B29" : "#EAF1FA");
        SetBrush("FreeBadgeBorderBrush", dark ? "#223047" : "#DCE6F2");
        SetBrush("FreeBadgeForegroundBrush", dark ? "#7E8EA8" : "#74849B");
        SetBrush("LoadingTitleBrush", dark ? "#EDF3FF" : "#182235");
        SetBrush("LoadingMutedBrush", dark ? "#6A788E" : "#8492A8");

        ApplyDwmTheme(dark);

        try
        {
            WebView.DefaultBackgroundColor = dark
                ? System.Drawing.Color.FromArgb(255, 7, 10, 16)
                : System.Drawing.Color.FromArgb(255, 248, 250, 254);
        }
        catch
        {
            // Older WebView2 versions may not expose this property reliably.
        }
    }

    private void SetBrush(string key, string hex)
    {
        if (ColorConverter.ConvertFromString(hex) is Color color)
        {
            Resources[key] = new SolidColorBrush(color);
        }
    }

    private void ApplyDwmTheme(bool dark)
    {
        if (_windowHandle == IntPtr.Zero)
        {
            return;
        }

        try
        {
            var value = dark ? 1 : 0;
            if (DwmSetWindowAttribute(_windowHandle, 20, ref value, sizeof(int)) != 0)
            {
                DwmSetWindowAttribute(_windowHandle, 19, ref value, sizeof(int));
            }

            var corner = 2;
            DwmSetWindowAttribute(_windowHandle, 33, ref corner, sizeof(int));
        }
        catch
        {
            // DWM styling is best-effort and version-dependent.
        }
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        _windowHandle = new WindowInteropHelper(this).Handle;
        _hwndSource = HwndSource.FromHwnd(_windowHandle);
        _hwndSource?.AddHook(WindowProc);
        ApplyDwmTheme(_isDarkTheme);
    }

    private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmGetMinMaxInfo && lParam != IntPtr.Zero)
        {
            try
            {
                var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
                var info = new MonitorInfo
                {
                    Size = Marshal.SizeOf<MonitorInfo>(),
                };
                if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
                {
                    var minMax = Marshal.PtrToStructure<MinMaxInfo>(lParam);
                    minMax.MaxPosition.X = info.Work.Left - info.Monitor.Left;
                    minMax.MaxPosition.Y = info.Work.Top - info.Monitor.Top;
                    minMax.MaxSize.X = info.Work.Right - info.Work.Left;
                    minMax.MaxSize.Y = info.Work.Bottom - info.Work.Top;
                    Marshal.StructureToPtr(minMax, lParam, fDeleteOld: false);
                    handled = true;
                }
            }
            catch
            {
                // Falling back to the default WPF maximize behaviour is safe.
            }
        }

        return IntPtr.Zero;
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        var maximized = WindowState == WindowState.Maximized;
        if (MaximizeGlyph is not null && RestoreGlyph is not null && MaximizeButton is not null)
        {
            MaximizeGlyph.Visibility = maximized ? Visibility.Collapsed : Visibility.Visible;
            RestoreGlyph.Visibility = maximized ? Visibility.Visible : Visibility.Collapsed;
            MaximizeButton.ToolTip = maximized ? "还原" : "最大化";
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        _clipboardTimer.Stop();
        try
        {
            _hwndSource?.RemoveHook(WindowProc);
            _manager.Shutdown();
            _manager.Dispose();
        }
        catch (Exception ex)
        {
            _store.Log("ERROR", "关闭时保存状态失败: " + ex);
        }
    }

    private void ShowStartupError(string message)
    {
        try
        {
            MessageBox.Show(this, message, "流光下载器", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch
        {
            // Ignore secondary UI failures.
        }
        Close();
    }

    private void FlashTaskbar()
    {
        if (_windowHandle == IntPtr.Zero)
        {
            return;
        }
        var info = new FlashWindowInfo
        {
            Size = (uint)Marshal.SizeOf<FlashWindowInfo>(),
            Window = _windowHandle,
            Flags = 3,
            Count = 2,
            Timeout = 0,
        };
        FlashWindowEx(ref info);
    }

    private static string? GetString(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static int? GetInt(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number &&
               value.TryGetInt32(out var number)
            ? number
            : null;
    }

    private static bool? GetBool(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) &&
               (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
            ? value.GetBoolean()
            : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashWindowInfo
    {
        public uint Size;
        public IntPtr Window;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved;
        public NativePoint MaxSize;
        public NativePoint MaxPosition;
        public NativePoint MinTrackSize;
        public NativePoint MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    private const int WmGetMinMaxInfo = 0x0024;
    private const int MonitorDefaultToNearest = 0x00000002;

    [DllImport("user32.dll")]
    private static extern bool FlashWindowEx(ref FlashWindowInfo info);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
