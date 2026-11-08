using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace LumenInstaller
{
    internal sealed class UninstallerForm : LumenFormBase
    {
        private Label _title;
        private Label _subtitle;
        private Label _pathLabel;
        private Label _warningLabel;
        private Label _statusLabel;
        private OutlineButton _cancelButton;
        private DangerButton _uninstallButton;
        private readonly string _installDir;
        private bool _working;

        public UninstallerForm()
        {
            _installDir = Path.GetDirectoryName(Application.ExecutablePath);
        }

        protected override int DesignWidth
        {
            get { return 540; }
        }

        protected override int DesignHeight
        {
            get { return 340; }
        }

        protected override string TopBarKind
        {
            get { return "卸载程序"; }
        }

        protected override void BuildUi()
        {
            Text = "卸载 " + AppInfo.Name;

            var logo = new LogoView(BrandLogo);
            logo.Bounds = new Rectangle(UiScale.S(30), UiScale.S(70), UiScale.S(52), UiScale.S(52));
            Controls.Add(logo);

            _title = new Label();
            _title.Text = "卸载 流光下载器";
            _title.AutoSize = false;
            _title.BackColor = Color.Transparent;
            _title.ForeColor = Palette.Text;
            _title.Font = DrawUtil.UiFont(18f, FontStyle.Bold);
            _title.Bounds = new Rectangle(UiScale.S(98), UiScale.S(70), UiScale.S(400), UiScale.S(32));
            _title.TextAlign = ContentAlignment.MiddleLeft;
            Controls.Add(_title);

            _subtitle = new Label();
            _subtitle.Text = "v" + AppInfo.Version + "   ·   当前用户安装";
            _subtitle.AutoSize = false;
            _subtitle.BackColor = Color.Transparent;
            _subtitle.ForeColor = Palette.TextDim;
            _subtitle.Font = DrawUtil.UiFont(8.5f, FontStyle.Regular);
            _subtitle.Bounds = new Rectangle(UiScale.S(100), UiScale.S(103), UiScale.S(400), UiScale.S(18));
            Controls.Add(_subtitle);

            var separator = new Panel();
            separator.BackColor = Palette.LineSoft;
            separator.Bounds = new Rectangle(UiScale.S(30), UiScale.S(140), UiScale.S(480), 1);
            Controls.Add(separator);

            _pathLabel = new Label();
            _pathLabel.Text = "将从此位置移除程序文件：\n" + _installDir;
            _pathLabel.AutoSize = false;
            _pathLabel.BackColor = Color.Transparent;
            _pathLabel.ForeColor = Palette.TextMuted;
            _pathLabel.Font = DrawUtil.UiFont(8.5f, FontStyle.Regular);
            _pathLabel.Bounds = new Rectangle(UiScale.S(30), UiScale.S(158), UiScale.S(480), UiScale.S(44));
            _pathLabel.TextAlign = ContentAlignment.TopLeft;
            _pathLabel.AutoEllipsis = true;
            Controls.Add(_pathLabel);

            _warningLabel = new Label();
            _warningLabel.Text = "只删除本应用安装清单中的文件；安装目录中的其他文件会保留。";
            _warningLabel.AutoSize = false;
            _warningLabel.BackColor = Color.Transparent;
            _warningLabel.ForeColor = Palette.TextDim;
            _warningLabel.Font = DrawUtil.UiFont(8.25f, FontStyle.Regular);
            _warningLabel.Bounds = new Rectangle(UiScale.S(30), UiScale.S(210), UiScale.S(480), UiScale.S(32));
            _warningLabel.TextAlign = ContentAlignment.TopLeft;
            Controls.Add(_warningLabel);

            _statusLabel = new Label();
            _statusLabel.Text = "正在卸载，请稍候…";
            _statusLabel.AutoSize = false;
            _statusLabel.BackColor = Color.Transparent;
            _statusLabel.ForeColor = Palette.AccentCyan;
            _statusLabel.Font = DrawUtil.UiFont(8.5f, FontStyle.Regular);
            _statusLabel.Bounds = new Rectangle(UiScale.S(30), UiScale.S(210), UiScale.S(480), UiScale.S(26));
            _statusLabel.Visible = false;
            Controls.Add(_statusLabel);

            var footerSeparator = new Panel();
            footerSeparator.BackColor = Palette.LineSoft;
            footerSeparator.Bounds = new Rectangle(UiScale.S(30), UiScale.S(260), UiScale.S(480), 1);
            Controls.Add(footerSeparator);

            _cancelButton = new OutlineButton();
            _cancelButton.Text = "取消";
            _cancelButton.Bounds = new Rectangle(UiScale.S(310), UiScale.S(276), UiScale.S(88), UiScale.S(38));
            _cancelButton.Click += delegate { Close(); };
            Controls.Add(_cancelButton);

            _uninstallButton = new DangerButton();
            _uninstallButton.Text = "确认卸载";
            _uninstallButton.Bounds = new Rectangle(UiScale.S(410), UiScale.S(274), UiScale.S(100), UiScale.S(42));
            _uninstallButton.Click += delegate { StartUninstall(); };
            Controls.Add(_uninstallButton);
        }

        private void StartUninstall()
        {
            if (_working)
            {
                return;
            }

            if (!UninstallController.ValidateInstall(_installDir))
            {
                MessageBox.Show(
                    this,
                    "没有找到完整的安装清单，可能已经卸载，或者程序文件不完整。",
                    "无法卸载",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            if (PackageInstaller.IsAppRunning())
            {
                MessageBox.Show(
                    this,
                    "流光下载器正在运行。请先退出程序，再继续卸载。",
                    "请先退出程序",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            _working = true;
            SetCloseEnabled(false);
            _cancelButton.Enabled = false;
            _uninstallButton.Enabled = false;
            _warningLabel.Visible = false;
            _statusLabel.Visible = true;

            string startError;
            if (!UninstallController.StartWorker(_installDir, out startError))
            {
                _working = false;
                SetCloseEnabled(true);
                _cancelButton.Enabled = true;
                _uninstallButton.Enabled = true;
                _warningLabel.Visible = true;
                _statusLabel.Visible = false;
                MessageBox.Show(this, "无法启动卸载进程：\n" + startError, "卸载失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var timer = new System.Windows.Forms.Timer();
            timer.Interval = 900;
            timer.Tick += delegate
            {
                timer.Stop();
                timer.Dispose();
                _working = false;
                Close();
            };
            timer.Start();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_working)
            {
                e.Cancel = true;
                return;
            }
            base.OnFormClosing(e);
        }
    }

    internal static class UninstallController
    {
        public static bool ValidateInstall(string installDir)
        {
            if (string.IsNullOrWhiteSpace(installDir))
            {
                return false;
            }

            return File.Exists(Path.Combine(installDir, AppInfo.MarkerFile)) &&
                   File.Exists(Path.Combine(installDir, AppInfo.ManifestFile));
        }

        public static bool StartWorker(string installDir, out string error)
        {
            error = null;
            try
            {
                string tempRoot = Path.Combine(
                    Path.GetTempPath(),
                    "LumenUninstall-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempRoot);
                string worker = Path.Combine(tempRoot, "UninstallWorker.exe");
                File.Copy(Application.ExecutablePath, worker, true);

                var startInfo = new ProcessStartInfo
                {
                    FileName = worker,
                    Arguments = "--worker \"" + installDir.TrimEnd(Path.DirectorySeparatorChar) +
                                "\" --parent " + Process.GetCurrentProcess().Id,
                    WorkingDirectory = tempRoot,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                };
                Process.Start(startInfo);
                SetupLog.Write("Uninstall worker started for " + installDir);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                SetupLog.Write("Failed to start uninstall worker: " + ex);
                return false;
            }
        }

        public static void RunWorker(string installDir, int parentPid)
        {
            SetupLog.Write("Uninstall worker active: " + installDir);
            try
            {
                WaitForParent(parentPid);
                if (!ValidateInstall(installDir))
                {
                    SetupLog.Write("Uninstall aborted: installation marker or manifest missing.");
                    return;
                }

                List<string> managedFiles = PackageInstaller.ReadManagedFiles(installDir);
                if (managedFiles.Count == 0)
                {
                    SetupLog.Write("Uninstall aborted: managed file list is empty.");
                    return;
                }

                ShortcutHelper.RemoveInstallShortcuts();
                RegistryHelper.RemoveInstallRegistration();

                int failures = 0;
                int deleted = 0;
                foreach (string relative in managedFiles)
                {
                    if (string.Equals(
                        relative.TrimStart(Path.DirectorySeparatorChar),
                        AppInfo.Uninstaller,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        // The uninstaller executable is removed only after all other files succeed,
                        // so the user can retry if a file is locked.
                        continue;
                    }

                    string full;
                    try
                    {
                        full = Path.GetFullPath(Path.Combine(installDir, relative));
                        EnsureInside(installDir, full);
                    }
                    catch (Exception ex)
                    {
                        failures++;
                        SetupLog.Write("Uninstall skipped unsafe manifest entry " + relative + ": " + ex.Message);
                        continue;
                    }

                    if (!File.Exists(full))
                    {
                        continue;
                    }

                    string error;
                    if (!TryDeleteFile(full, out error))
                    {
                        failures++;
                        SetupLog.Write("Uninstall failed for " + full + ": " + error);
                    }
                    else
                    {
                        deleted++;
                    }
                }

                if (failures == 0)
                {
                    string uninstallerPath = Path.Combine(installDir, AppInfo.Uninstaller);
                    if (File.Exists(uninstallerPath))
                    {
                        string error;
                        if (!TryDeleteFile(uninstallerPath, out error))
                        {
                            failures++;
                            SetupLog.Write("Uninstall failed for uninstaller: " + error);
                        }
                    }
                }

                if (failures == 0)
                {
                    TryDeleteHiddenFile(Path.Combine(installDir, AppInfo.MarkerFile));
                    TryDeleteHiddenFile(Path.Combine(installDir, AppInfo.ManifestFile));
                    RemoveEmptyDirectories(installDir, managedFiles);
                    try
                    {
                        if (Directory.Exists(installDir) &&
                            !Directory.EnumerateFileSystemEntries(installDir).Any())
                        {
                            Directory.Delete(installDir, false);
                        }
                    }
                    catch (Exception ex)
                    {
                        SetupLog.Write("Uninstall could not remove empty install directory: " + ex.Message);
                    }

                    SetupLog.Write("Uninstall complete: " + installDir + " files=" + deleted);
                }
                else
                {
                    SetupLog.Write(
                        "Uninstall paused: " + failures +
                        " file(s) could not be removed. The uninstaller and manifest are kept for retry.");
                }
            }
            catch (Exception ex)
            {
                SetupLog.Write("Uninstall worker failed: " + ex);
            }
        }

        private static void WaitForParent(int parentPid)
        {
            if (parentPid <= 0)
            {
                Thread.Sleep(1400);
                return;
            }

            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < 6000)
            {
                try
                {
                    using (Process parent = Process.GetProcessById(parentPid))
                    {
                        if (parent.HasExited)
                        {
                            break;
                        }
                        parent.WaitForExit(200);
                    }
                    if (!ProcessExists(parentPid))
                    {
                        break;
                    }
                }
                catch (ArgumentException)
                {
                    break;
                }
                catch
                {
                    Thread.Sleep(150);
                }
            }

            Thread.Sleep(200);
        }

        private static bool ProcessExists(int pid)
        {
            try
            {
                using (Process process = Process.GetProcessById(pid))
                {
                    return !process.HasExited;
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool TryDeleteFile(string path, out string error)
        {
            error = null;
            for (int attempt = 0; attempt < 12; attempt++)
            {
                try
                {
                    if (!File.Exists(path))
                    {
                        return true;
                    }

                    File.SetAttributes(path, FileAttributes.Normal);
                    File.Delete(path);
                    return true;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    Thread.Sleep(300);
                }
            }

            return !File.Exists(path);
        }

        private static void TryDeleteHiddenFile(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return;
                }
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
            }
            catch (Exception ex)
            {
                SetupLog.Write("Uninstall could not remove metadata file " + path + ": " + ex.Message);
            }
        }

        private static void RemoveEmptyDirectories(string installDir, List<string> managedFiles)
        {
            var directories = new List<string>();
            foreach (string relative in managedFiles)
            {
                string current = Path.GetDirectoryName(Path.Combine(installDir, relative));
                while (!string.IsNullOrWhiteSpace(current))
                {
                    if (string.Equals(
                        current.TrimEnd(Path.DirectorySeparatorChar),
                        installDir.TrimEnd(Path.DirectorySeparatorChar),
                        StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }

                    if (!directories.Contains(current, StringComparer.OrdinalIgnoreCase))
                    {
                        directories.Add(current);
                    }
                    current = Path.GetDirectoryName(current);
                }
            }

            directories.Sort(delegate(string left, string right)
            {
                return right.Length.CompareTo(left.Length);
            });

            foreach (string directory in directories)
            {
                try
                {
                    if (Directory.Exists(directory) &&
                        !Directory.EnumerateFileSystemEntries(directory).Any())
                    {
                        Directory.Delete(directory, false);
                    }
                }
                catch (Exception ex)
                {
                    SetupLog.Write("Uninstall could not remove empty directory " + directory + ": " + ex.Message);
                }
            }
        }

        private static void EnsureInside(string installDir, string path)
        {
            string root = installDir.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(path);
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Manifest path escapes the installation directory.");
            }
        }
    }

    internal static class UninstallerProgram
    {
        [STAThread]
        private static int Main(string[] args)
        {
            int workerIndex = -1;
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--worker", StringComparison.OrdinalIgnoreCase))
                {
                    workerIndex = i;
                    break;
                }
            }

            int parentPid = 0;
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--parent", StringComparison.OrdinalIgnoreCase) &&
                    i + 1 < args.Length)
                {
                    int parsed;
                    if (int.TryParse(args[i + 1], out parsed) && parsed > 0)
                    {
                        parentPid = parsed;
                    }
                }
            }

            if (workerIndex >= 0 && workerIndex + 1 < args.Length)
            {
                UninstallController.RunWorker(args[workerIndex + 1], parentPid);
                return 0;
            }

            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                bool silent = args.Any(a => string.Equals(a, "/S", StringComparison.OrdinalIgnoreCase));
                string installDir = Path.GetDirectoryName(Application.ExecutablePath);
                if (silent)
                {
                    if (!UninstallController.ValidateInstall(installDir) ||
                        PackageInstaller.IsAppRunning())
                    {
                        return 1;
                    }

                    string error;
                    return UninstallController.StartWorker(installDir, out error) ? 0 : 1;                }

                Application.Run(new UninstallerForm());
                return 0;
            }
            catch (Exception ex)
            {
                SetupLog.Write("Uninstaller crash: " + ex);
                try
                {
                    MessageBox.Show(
                        "卸载程序遇到问题：\n" + ex.Message,
                        "卸载 " + AppInfo.Name,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
                catch
                {
                }
                return 1;
            }
        }
    }
}
