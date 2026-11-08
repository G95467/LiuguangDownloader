using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace LumenDownloader.App;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private StateStore? _startupStore;

    public IReadOnlyList<string> StartupUrls { get; private set; } = Array.Empty<string>();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _startupStore = new StateStore();
        _startupStore.Log("INFO", "流光下载器启动，版本 " + GetVersion());
        StartupUrls = ParseStartupUrls(e.Args);

        var mutexName = BuildMutexName();
        _singleInstance = new Mutex(initiallyOwned: true, mutexName, out var createdNew);
        if (!createdNew)
        {
            ActivateExistingWindow();
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        var window = new MainWindow(StartupUrls);
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _singleInstance?.ReleaseMutex();
        }
        catch
        {
            // The OS also releases the mutex when the process exits.
        }
        _singleInstance?.Dispose();
        _singleInstance = null;
        base.OnExit(e);
    }

    private string BuildMutexName()
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(AppPaths.DataRoot.ToLowerInvariant());
        var hash = sha.ComputeHash(bytes);
        return "Local\\LiuguangDownloader-" + Convert.ToHexString(hash, 0, 8);
    }

    private static IReadOnlyList<string> ParseStartupUrls(IEnumerable<string> args)
    {
        var urls = new List<string>();
        foreach (var arg in args)
        {
            if (string.IsNullOrWhiteSpace(arg) || arg.StartsWith("-", StringComparison.Ordinal))
            {
                continue;
            }

            var value = arg.Trim().Trim('"');
            if (!value.Contains("://", StringComparison.Ordinal))
            {
                value = "https://" + value.TrimStart('/');
            }

            if (Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                urls.Add(uri.ToString());
            }
        }

        return urls.Distinct(StringComparer.OrdinalIgnoreCase).Take(30).ToList();
    }

    private static string GetVersion() =>
        typeof(App).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    private static void ActivateExistingWindow()
    {
        try
        {
            var processName = Process.GetCurrentProcess().ProcessName;
            for (var attempt = 0; attempt < 12; attempt++)
            {
                foreach (var process in Process.GetProcessesByName(processName))
                {
                    if (process.Id == Environment.ProcessId)
                    {
                        continue;
                    }

                    if (!string.Equals(process.MainWindowTitle, "流光下载器", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var handle = process.MainWindowHandle;
                    if (handle != IntPtr.Zero)
                    {
                        ShowWindowAsync(handle, 9); // SW_RESTORE
                        SetForegroundWindow(handle);
                        return;
                    }
                }

                Thread.Sleep(150);
            }
        }
        catch
        {
            // If activation fails, the user can switch to it from the taskbar.
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _startupStore?.Log("ERROR", "界面线程异常: " + e.Exception);
        try
        {
            MessageBox.Show(
                "界面遇到一个未处理的问题，下载任务状态已经尽量保留。\n\n" + e.Exception.Message,
                "流光下载器",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch
        {
            // Do not throw from the handler.
        }
        e.Handled = true;
    }

    private void OnDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        _startupStore?.Log("ERROR", "后台异常: " + e.ExceptionObject);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _startupStore?.Log("ERROR", "后台任务异常: " + e.Exception);
        e.SetObserved();
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
