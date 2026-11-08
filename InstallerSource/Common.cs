using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace LumenInstaller
{
    internal static class AppInfo
    {
        public const string Name = "流光下载器";
        public const string Version = "1.0.2";
        public const string Publisher = "Lumen Studio";
        public const string Executable = "流光下载器.exe";
        public const string Uninstaller = "Uninstall.exe";
        public const string MarkerFile = ".lumen-install";
        public const string ManifestFile = ".lumen-manifest";
        public const string CopyrightNotice = "本应用由Microsoft终结者版权所有，未经允许不得外传";
        public const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\LiuguangDownloader";
        public const string PayloadResource = "LumenPayload.zip";
        public const string AppDescription = "多线程 HTTP / HTTPS 极速直连下载器";
    }

    internal static class AppPaths
    {
        public static string DefaultInstallDir()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs",
                AppInfo.Name);
        }

        public static string ExistingInstallDir()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(AppInfo.RegistryKeyPath))
                {
                    if (key != null)
                    {
                        string location = key.GetValue("InstallLocation") as string;
                        if (!string.IsNullOrWhiteSpace(location) && Directory.Exists(location))
                        {
                            return location;
                        }
                    }
                }
            }
            catch
            {
                // Fall back to the per-user default below.
            }

            return DefaultInstallDir();
        }

        public static string DesktopShortcut()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                AppInfo.Name + ".lnk");
        }

        public static string StartMenuFolder()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                AppInfo.Name);
        }

        public static string StartMenuAppShortcut()
        {
            return Path.Combine(StartMenuFolder(), AppInfo.Name + ".lnk");
        }

        public static string StartMenuUninstallShortcut()
        {
            return Path.Combine(StartMenuFolder(), "卸载 " + AppInfo.Name + ".lnk");
        }

        public static string TempFolder()
        {
            var dir = Path.Combine(Path.GetTempPath(), "LumenDownloaderSetup");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    internal static class SetupLog
    {
        private static readonly object Sync = new object();

        public static string LogPath
        {
            get
            {
                string root = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "LiuguangDownloader");
                return Path.Combine(root, "setup.log");
            }
        }

        public static void Write(string message)
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + message + Environment.NewLine;
            WriteTo(LogPath, line);
            string debugPath = Environment.GetEnvironmentVariable("LUMEN_SETUP_DEBUG_PATH");
            if (!string.IsNullOrWhiteSpace(debugPath))
            {
                WriteTo(debugPath, line);
            }
        }

        private static void WriteTo(string path, string line)
        {
            try
            {
                lock (Sync)
                {
                    string directory = Path.GetDirectoryName(path);
                    if (!string.IsNullOrWhiteSpace(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }
                    File.AppendAllText(path, line, Encoding.UTF8);
                }
            }
            catch
            {
                // Installation must not fail because a diagnostic log could not be written.
            }
        }
    }

    internal static class Palette
    {
        public static readonly Color Window = Color.FromArgb(7, 10, 16);
        public static readonly Color TopBar = Color.FromArgb(10, 14, 22);
        public static readonly Color Card = Color.FromArgb(15, 21, 33);
        public static readonly Color CardRaised = Color.FromArgb(19, 27, 42);
        public static readonly Color Field = Color.FromArgb(12, 18, 28);
        public static readonly Color Text = Color.FromArgb(237, 243, 255);
        public static readonly Color TextMuted = Color.FromArgb(151, 165, 187);
        public static readonly Color TextDim = Color.FromArgb(96, 111, 134);
        public static readonly Color Line = Color.FromArgb(31, 43, 61);
        public static readonly Color LineSoft = Color.FromArgb(24, 34, 49);
        public static readonly Color AccentCyan = Color.FromArgb(96, 224, 255);
        public static readonly Color AccentBlue = Color.FromArgb(112, 143, 255);
        public static readonly Color AccentViolet = Color.FromArgb(177, 139, 255);
        public static readonly Color Ink = Color.FromArgb(6, 18, 29);
        public static readonly Color Green = Color.FromArgb(74, 222, 158);
        public static readonly Color GreenSoft = Color.FromArgb(16, 65, 52);
        public static readonly Color Amber = Color.FromArgb(250, 197, 104);
        public static readonly Color AmberSoft = Color.FromArgb(66, 52, 26);
        public static readonly Color Red = Color.FromArgb(255, 111, 128);
        public static readonly Color RedSoft = Color.FromArgb(69, 29, 37);
    }

    internal static class DrawUtil
    {
        public static GraphicsPath RoundedRect(Rectangle rect, float radius)
        {
            return RoundedRect(new RectangleF(rect.X, rect.Y, rect.Width, rect.Height), radius);
        }

        public static GraphicsPath RoundedRect(RectangleF rect, float radius)
        {
            var path = new GraphicsPath();
            float diameter = Math.Max(1f, Math.Min(radius * 2f, Math.Min(rect.Width, rect.Height)));
            path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void FillRounded(Graphics g, Rectangle rect, float radius, Brush brush)
        {
            using (GraphicsPath path = RoundedRect(rect, radius))
            {
                g.FillPath(brush, path);
            }
        }

        public static void DrawRoundedBorder(Graphics g, Rectangle rect, float radius, Color color, float width)
        {
            using (GraphicsPath path = RoundedRect(rect, radius))
            using (Pen pen = new Pen(color, width))
            {
                g.DrawPath(pen, path);
            }
        }

        public static LinearGradientBrush AccentBrush(Rectangle rect)
        {
            var brush = new LinearGradientBrush(
                rect,
                Palette.AccentCyan,
                Palette.AccentViolet,
                12f);
            brush.InterpolationColors = new ColorBlend
            {
                Colors = new[]
                {
                    Palette.AccentCyan,
                    Palette.AccentBlue,
                    Palette.AccentViolet,
                },
                Positions = new[] { 0f, 0.54f, 1f },
            };
            return brush;
        }

        public static Font UiFont(float size, FontStyle style)
        {
            try
            {
                return new Font("Microsoft YaHei UI", size, style, GraphicsUnit.Point);
            }
            catch
            {
                return new Font(SystemFonts.MessageBoxFont.FontFamily, size, style, GraphicsUnit.Point);
            }
        }

        public static Font DisplayFont(float size, FontStyle style)
        {
            try
            {
                return new Font("Segoe UI Variable Display", size, style, GraphicsUnit.Point);
            }
            catch
            {
                return UiFont(size, style);
            }
        }
    }

    internal sealed class InstallOptions
    {
        public string InstallDirectory;
        public bool IncludeSource = true;
        public bool CreateDesktopShortcut = true;
        public bool CreateStartMenuShortcuts = true;
        public bool ConsentAccepted;
    }

    internal static class InstallPathPolicy
    {
        public static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new InvalidOperationException("安装位置不能为空。");
            }

            string full = Path.GetFullPath(path.Trim().Trim('"'));
            string root = Path.GetPathRoot(full);
            if (string.IsNullOrWhiteSpace(root))
            {
                throw new InvalidOperationException("安装位置无效。");
            }

            full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (full.Length == 0)
            {
                full = root;
            }

            if (string.Equals(
                full.TrimEnd(Path.DirectorySeparatorChar),
                root.TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("不能直接安装到磁盘根目录，请选择一个专用文件夹。");
            }

            if (full.Length > 210)
            {
                throw new InvalidOperationException("安装路径过长，请选择更短的文件夹。");
            }

            return full;
        }

        public static void Validate(string path)
        {
            string installDir = Normalize(path);
            if (IsForbidden(installDir))
            {
                throw new InvalidOperationException(
                    "为保护你的文件，安装器不能安装到桌面、下载、文档或系统目录。\n" +
                    "请选择一个新建的空文件夹，例如 D:\\流光下载器。");
            }

            if (File.Exists(installDir))
            {
                throw new InvalidOperationException("安装位置是一个文件，请选择文件夹。");
            }

            if (!Directory.Exists(installDir))
            {
                return;
            }

            FileAttributes attributes = File.GetAttributes(installDir);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException("安装位置是一个链接目录，请选择普通文件夹。");
            }

            bool marker = File.Exists(Path.Combine(installDir, AppInfo.MarkerFile));
            bool manifest = File.Exists(Path.Combine(installDir, AppInfo.ManifestFile));
            if (marker && !manifest)
            {
                throw new InvalidOperationException(
                    "这是旧版安装目录，安装器不会继续使用。\n" +
                    "请删除该目录内容，或选择一个新的空文件夹。");
            }

            if (!marker && Directory.EnumerateFileSystemEntries(installDir).Any())
            {
                throw new InvalidOperationException(
                    "这个文件夹里已经有其他文件。为保护你的文件，安装器只允许安装到空的专用文件夹。\n" +
                    "请新建一个空文件夹，例如 D:\\流光下载器。");
            }
        }

        private static bool IsForbidden(string path)
        {
            var exact = new List<string>
            {
                SafeFolder(Environment.SpecialFolder.UserProfile),
                SafeFolder(Environment.SpecialFolder.DesktopDirectory),
                SafeFolder(Environment.SpecialFolder.MyDocuments),
                SafeFolder(Environment.SpecialFolder.LocalApplicationData),
                SafeFolder(Environment.SpecialFolder.ApplicationData),
                SafeFolder(Environment.SpecialFolder.ProgramFiles),
                SafeFolder(Environment.SpecialFolder.ProgramFilesX86),
                SafeFolder(Environment.SpecialFolder.Windows),
                SafeFolder(Environment.SpecialFolder.CommonApplicationData),
            };

            string profile = SafeFolder(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(profile))
            {
                exact.Add(Path.Combine(profile, "Downloads"));
                exact.Add(Path.Combine(profile, "下载"));
            }

            foreach (string folder in exact)
            {
                if (IsSamePath(path, folder))
                {
                    return true;
                }
            }

            var subtrees = new List<string>
            {
                SafeFolder(Environment.SpecialFolder.DesktopDirectory),
                SafeFolder(Environment.SpecialFolder.MyDocuments),
                SafeFolder(Environment.SpecialFolder.Windows),
            };
            if (!string.IsNullOrWhiteSpace(profile))
            {
                subtrees.Add(Path.Combine(profile, "Downloads"));
                subtrees.Add(Path.Combine(profile, "下载"));
            }

            string oneDrive = Environment.GetEnvironmentVariable("OneDrive");
            if (!string.IsNullOrWhiteSpace(oneDrive))
            {
                subtrees.Add(oneDrive);
            }
            string oneDriveCommercial = Environment.GetEnvironmentVariable("OneDriveCommercial");
            if (!string.IsNullOrWhiteSpace(oneDriveCommercial))
            {
                subtrees.Add(oneDriveCommercial);
            }

            foreach (string folder in subtrees)
            {
                if (IsSameOrUnder(path, folder))
                {
                    return true;
                }
            }

            return false;
        }

        private static string SafeFolder(Environment.SpecialFolder folder)
        {
            try
            {
                return Environment.GetFolderPath(folder);
            }
            catch
            {
                return null;
            }
        }

        private static bool IsSamePath(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                return false;
            }

            return string.Equals(
                left.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                right.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSameOrUnder(string candidate, string parent)
        {
            if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(parent))
            {
                return false;
            }

            string c = candidate.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string p = parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(c, p, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return c.StartsWith(p + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                   c.StartsWith(p + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
    }

    internal sealed class InstallOutcome
    {
        public bool Ok;
        public string Error;
        public string Warning;
        public string InstallDirectory;
        public string AppPath;
        public long AppBytes;
        public bool SourceInstalled;
    }

    internal static class RuntimeCheck
    {
        public static bool HasDotNet10(out string found)
        {
            var roots = new List<string>();
            AddRuntimeRoot(roots, Environment.SpecialFolder.ProgramFiles);
            AddRuntimeRoot(roots, Environment.SpecialFolder.ProgramFilesX86);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(local))
            {
                roots.Add(Path.Combine(local, "Microsoft", "dotnet", "shared", "Microsoft.WindowsDesktop.App"));
            }

            foreach (string root in roots)
            {
                try
                {
                    if (!Directory.Exists(root))
                    {
                        continue;
                    }

                    foreach (string directory in Directory.GetDirectories(root))
                    {
                        string version = Path.GetFileName(directory);
                        if (version.StartsWith("10.", StringComparison.OrdinalIgnoreCase))
                        {
                            found = version;
                            return true;
                        }
                    }
                }
                catch
                {
                    // Continue checking other possible runtime locations.
                }
            }

            found = null;
            return false;
        }

        private static void AddRuntimeRoot(List<string> roots, Environment.SpecialFolder folder)
        {
            try
            {
                string baseFolder = Environment.GetFolderPath(folder);
                if (!string.IsNullOrWhiteSpace(baseFolder))
                {
                    roots.Add(Path.Combine(baseFolder, "dotnet", "shared", "Microsoft.WindowsDesktop.App"));
                }
            }
            catch
            {
                // Ignore unavailable special folders.
            }
        }

        public static bool HasWebView2(out string found)
        {
            var roots = new List<string>();
            try
            {
                string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                if (!string.IsNullOrWhiteSpace(pf86))
                {
                    roots.Add(Path.Combine(pf86, "Microsoft", "EdgeWebView", "Application"));
                }
            }
            catch
            {
            }
            try
            {
                string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                if (!string.IsNullOrWhiteSpace(pf))
                {
                    roots.Add(Path.Combine(pf, "Microsoft", "EdgeWebView", "Application"));
                }
            }
            catch
            {
            }
            try
            {
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (!string.IsNullOrWhiteSpace(local))
                {
                    roots.Add(Path.Combine(local, "Microsoft", "EdgeWebView", "Application"));
                }
            }
            catch
            {
            }

            foreach (string root in roots)
            {
                try
                {
                    if (!Directory.Exists(root))
                    {
                        continue;
                    }

                    foreach (string directory in Directory.GetDirectories(root))
                    {
                        string version = Path.GetFileName(directory);
                        if (File.Exists(Path.Combine(directory, "msedgewebview2.exe")))
                        {
                            found = version;
                            return true;
                        }
                    }
                }
                catch
                {
                    // Continue with the next known location.
                }
            }

            found = null;
            return false;
        }
    }

    internal static class ShortcutHelper
    {
        public static void CreateShortcut(
            string shortcutPath,
            string targetPath,
            string workingDirectory,
            string description,
            string iconPath)
        {
            object shell = null;
            object shortcut = null;
            try
            {
                string parent = Path.GetDirectoryName(shortcutPath);
                if (!string.IsNullOrWhiteSpace(parent))
                {
                    Directory.CreateDirectory(parent);
                }

                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null)
                {
                    throw new InvalidOperationException("Windows Script Host is unavailable.");
                }

                shell = Activator.CreateInstance(shellType);
                shortcut = shellType.InvokeMember(
                    "CreateShortcut",
                    BindingFlags.InvokeMethod,
                    null,
                    shell,
                    new object[] { shortcutPath },
                    CultureInfo.InvariantCulture);

                Type shortcutType = shortcut.GetType();
                shortcutType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { targetPath }, CultureInfo.InvariantCulture);
                shortcutType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { workingDirectory }, CultureInfo.InvariantCulture);
                shortcutType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { description }, CultureInfo.InvariantCulture);
                shortcutType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { iconPath + ",0" }, CultureInfo.InvariantCulture);
                shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null, CultureInfo.InvariantCulture);
            }
            finally
            {
                if (shortcut != null && Marshal.IsComObject(shortcut))
                {
                    Marshal.ReleaseComObject(shortcut);
                }
                if (shell != null && Marshal.IsComObject(shell))
                {
                    Marshal.ReleaseComObject(shell);
                }
            }
        }

        public static void RemoveInstallShortcuts()
        {
            TryDeleteFile(AppPaths.DesktopShortcut());
            TryDeleteFile(AppPaths.StartMenuAppShortcut());
            TryDeleteFile(AppPaths.StartMenuUninstallShortcut());
            try
            {
                string folder = AppPaths.StartMenuFolder();
                if (Directory.Exists(folder) &&
                    !Directory.EnumerateFileSystemEntries(folder).Any())
                {
                    Directory.Delete(folder, false);
                }
            }
            catch (Exception ex)
            {
                SetupLog.Write("Failed to clean Start Menu folder: " + ex.Message);
            }
        }

        public static void TryDeleteFile(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex)
            {
                SetupLog.Write("Failed to delete " + path + ": " + ex.Message);
            }
        }
    }

    internal static class RegistryHelper
    {
        public static bool RegisterInstall(InstallOptions options, long installedBytes)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(AppInfo.RegistryKeyPath))
                {
                    if (key == null)
                    {
                        return false;
                    }

                    string appPath = Path.Combine(options.InstallDirectory, AppInfo.Executable);
                    string uninstallPath = Path.Combine(options.InstallDirectory, AppInfo.Uninstaller);
                    key.SetValue("DisplayName", AppInfo.Name, RegistryValueKind.String);
                    key.SetValue("DisplayVersion", AppInfo.Version, RegistryValueKind.String);
                    key.SetValue("Publisher", AppInfo.Publisher, RegistryValueKind.String);
                    key.SetValue("InstallLocation", options.InstallDirectory, RegistryValueKind.String);
                    key.SetValue("DisplayIcon", appPath + ",0", RegistryValueKind.String);
                    key.SetValue("UninstallString", "\"" + uninstallPath + "\"", RegistryValueKind.String);
                    key.SetValue("QuietUninstallString", "\"" + uninstallPath + "\" /S", RegistryValueKind.String);
                    key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"), RegistryValueKind.String);
                    key.SetValue("EstimatedSize", (int)Math.Max(1, installedBytes / 1024), RegistryValueKind.DWord);
                    key.SetValue("SourceInstalled", options.IncludeSource ? 1 : 0, RegistryValueKind.DWord);
                    return true;
                }
            }
            catch (Exception ex)
            {
                SetupLog.Write("Failed to register uninstall information: " + ex.Message);
                return false;
            }
        }

        public static void RemoveInstallRegistration()
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(AppInfo.RegistryKeyPath, false);
            }
            catch (Exception ex)
            {
                SetupLog.Write("Failed to remove uninstall registration: " + ex.Message);
            }
        }
    }

    internal static class PackageInstaller
    {
        public static bool IsAppRunning()
        {
            try
            {
                string[] names = new[] { "流光下载器", "LiuguangDownloader" };
                foreach (string name in names)
                {
                    if (Process.GetProcessesByName(name).Length > 0)
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // A process enumeration failure must not block installation.
            }

            return false;
        }

        public static InstallOutcome Install(InstallOptions options, Action<int, string> report)
        {
            var outcome = new InstallOutcome();
            try
            {
                if (options == null || string.IsNullOrWhiteSpace(options.InstallDirectory))
                {
                    throw new InvalidOperationException("安装位置不能为空。");
                }

                if (!options.ConsentAccepted)
                {
                    throw new InvalidOperationException("请先阅读并同意版权声明，然后再安装。");
                }

                string installDir = InstallPathPolicy.Normalize(options.InstallDirectory);
                InstallPathPolicy.Validate(installDir);
                options.InstallDirectory = installDir;

                try
                {
                    Directory.CreateDirectory(installDir);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        "无法创建安装目录：\n" + installDir +
                        "\n请检查文件夹权限，或把安装程序加入安全软件的信任列表。\n" +
                        ex.Message,
                        ex);
                }

                string probe = Path.Combine(installDir, ".lumen-write-test-" + Guid.NewGuid().ToString("N"));
                try
                {
                    File.WriteAllText(probe, "write-test");
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        "无法写入这个文件夹。\n" +
                        "请检查文件夹权限，或把安装程序加入安全软件的信任列表后重试。\n" +
                        ex.Message,
                        ex);
                }
                finally
                {
                    try
                    {
                        if (File.Exists(probe))
                        {
                            File.Delete(probe);
                        }
                    }
                    catch
                    {
                    }
                }

                SetupLog.Write("Install start: " + installDir + " source=" + options.IncludeSource);
                report(3, "正在检查安装目录…");

                int appFileCount = 0;
                long installedBytes = 0;
                var installedFiles = new List<string>();
                using (Stream payload = Assembly.GetExecutingAssembly().GetManifestResourceStream(AppInfo.PayloadResource))
                {
                    if (payload == null)
                    {
                        throw new InvalidOperationException("安装包数据损坏：找不到内置文件。");
                    }

                    SetupLog.Write("Payload opened. length=" + payload.Length);
                    using (var archive = new ZipArchive(payload, ZipArchiveMode.Read))
                    {
                        int total = archive.Entries.Count(e => ShouldExtract(e.FullName, options.IncludeSource));
                        int done = 0;
                        SetupLog.Write("Payload entries to extract: " + total);
                        foreach (ZipArchiveEntry entry in archive.Entries)
                        {
                            if (!ShouldExtract(entry.FullName, options.IncludeSource) || string.IsNullOrEmpty(entry.Name))
                            {
                                continue;
                            }

                            string destination = GetDestination(installDir, entry.FullName, options.IncludeSource);
                            EnsureInside(installDir, destination);
                            string parent = Path.GetDirectoryName(destination);
                            if (!string.IsNullOrWhiteSpace(parent))
                            {
                                Directory.CreateDirectory(parent);
                            }

                            if (File.Exists(destination))
                            {
                                File.SetAttributes(destination, FileAttributes.Normal);
                            }

                            entry.ExtractToFile(destination, true);
                            done++;
                            appFileCount++;
                            installedBytes += entry.Length;
                            installedFiles.Add(destination.Substring(installDir.Length + 1));
                            int percent = 6 + (int)((done / (double)Math.Max(1, total)) * 68);
                            report(percent, "正在安装 " + Path.GetFileName(destination));
                        }
                    }
                }

                SetupLog.Write("Payload extraction finished. files=" + appFileCount);

                string appPath = Path.Combine(installDir, AppInfo.Executable);
                if (!File.Exists(appPath))
                {
                    throw new InvalidOperationException("安装包中没有找到主程序，文件可能已损坏。");
                }

                WriteManagedManifest(installDir, installedFiles);

                var marker = new StringBuilder();
                marker.AppendLine(AppInfo.Name);
                marker.AppendLine("Version=" + AppInfo.Version);
                marker.AppendLine("Source=" + (options.IncludeSource ? "1" : "0"));
                marker.AppendLine("Consent=1");
                marker.AppendLine("ConsentTime=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                marker.AppendLine("FileCount=" + installedFiles.Count);
                marker.AppendLine("Bytes=" + installedBytes);
                marker.AppendLine("Copyright=" + AppInfo.CopyrightNotice);
                string markerPath = Path.Combine(installDir, AppInfo.MarkerFile);
                if (File.Exists(markerPath))
                {
                    File.SetAttributes(markerPath, FileAttributes.Normal);
                }
                File.WriteAllText(markerPath, marker.ToString(), new UTF8Encoding(false));
                File.SetAttributes(markerPath, FileAttributes.Hidden);

                report(80, "正在创建快捷方式…");
                if (options.CreateDesktopShortcut)
                {
                    try
                    {
                        ShortcutHelper.CreateShortcut(
                            AppPaths.DesktopShortcut(),
                            appPath,
                            installDir,
                            AppInfo.AppDescription,
                            appPath);
                    }
                    catch (Exception ex)
                    {
                        SetupLog.Write("Desktop shortcut failed: " + ex.Message);
                        outcome.Warning = AppendWarning(
                            outcome.Warning,
                            "桌面快捷方式创建失败，可以直接运行安装目录中的程序。");
                    }
                }

                if (options.CreateStartMenuShortcuts)
                {
                    try
                    {
                        ShortcutHelper.CreateShortcut(
                            AppPaths.StartMenuAppShortcut(),
                            appPath,
                            installDir,
                            AppInfo.AppDescription,
                            appPath);
                        ShortcutHelper.CreateShortcut(
                            AppPaths.StartMenuUninstallShortcut(),
                            Path.Combine(installDir, AppInfo.Uninstaller),
                            installDir,
                            "卸载 " + AppInfo.Name,
                            Path.Combine(installDir, AppInfo.Uninstaller));
                    }
                    catch (Exception ex)
                    {
                        SetupLog.Write("Start Menu shortcut failed: " + ex.Message);
                        outcome.Warning = AppendWarning(
                            outcome.Warning,
                            "开始菜单快捷方式创建失败，可以直接运行安装目录中的程序。");
                    }
                }

                report(91, "正在写入安装信息…");
                if (!RegistryHelper.RegisterInstall(options, installedBytes))
                {
                    outcome.Warning = AppendWarning(
                        outcome.Warning,
                        "卸载信息登记失败，但程序文件已安装完成。");
                }
                report(100, "安装完成");

                outcome.Ok = true;
                outcome.InstallDirectory = installDir;
                outcome.AppPath = appPath;
                outcome.AppBytes = installedBytes;
                outcome.SourceInstalled = options.IncludeSource &&
                    Directory.Exists(Path.Combine(installDir, "Source"));
                SetupLog.Write("Install success: " + installDir + " files=" + appFileCount);
                return outcome;
            }
            catch (Exception ex)
            {
                SetupLog.Write("Install failed: " + ex);
                outcome.Ok = false;
                outcome.Error = ex.Message;
                return outcome;
            }
        }

        public static bool ValidateTarget(string installDirectory, out string error)
        {
            try
            {
                InstallPathPolicy.Validate(installDirectory);
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static void WriteManagedManifest(string installDir, List<string> relativeFiles)
        {
            string manifestPath = Path.Combine(installDir, AppInfo.ManifestFile);
            if (File.Exists(manifestPath))
            {
                File.SetAttributes(manifestPath, FileAttributes.Normal);
            }

            var manifest = new StringBuilder();
            manifest.AppendLine("# LumenDownloader managed files v2");
            foreach (string relative in relativeFiles)
            {
                manifest.AppendLine(relative);
            }

            File.WriteAllText(manifestPath, manifest.ToString(), new UTF8Encoding(false));
            File.SetAttributes(manifestPath, FileAttributes.Hidden);
        }

        public static List<string> ReadManagedFiles(string installDir)
        {
            var result = new List<string>();
            string manifestPath = Path.Combine(installDir, AppInfo.ManifestFile);
            if (!File.Exists(manifestPath))
            {
                return result;
            }

            foreach (string raw in File.ReadAllLines(manifestPath, Encoding.UTF8))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                line = line.Replace('/', Path.DirectorySeparatorChar);
                if (Path.IsPathRooted(line))
                {
                    SetupLog.Write("Unsafe manifest entry ignored: " + line);
                    continue;
                }

                try
                {
                    string full = Path.GetFullPath(Path.Combine(installDir, line));
                    EnsureInside(installDir, full);
                    if (string.Equals(full.TrimEnd(Path.DirectorySeparatorChar), installDir.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    result.Add(line);
                }
                catch (Exception ex)
                {
                    SetupLog.Write("Invalid manifest entry ignored: " + line + " (" + ex.Message + ")");
                }
            }

            return result;
        }

        private static bool ShouldExtract(string entryName, bool includeSource)
        {
            string normalized = (entryName ?? string.Empty).Replace('\\', '/');
            if (normalized.StartsWith("app/", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("tools/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return includeSource && normalized.StartsWith("source/", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetDestination(string installDir, string entryName, bool includeSource)
        {
            string normalized = (entryName ?? string.Empty).Replace('\\', '/').Replace('/', Path.DirectorySeparatorChar);
            if (normalized.StartsWith("app" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return Path.Combine(installDir, normalized.Substring(4));
            }
            if (normalized.StartsWith("tools" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return Path.Combine(installDir, normalized.Substring(6));
            }
            if (includeSource && normalized.StartsWith("source" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return Path.Combine(installDir, "Source", normalized.Substring(7));
            }

            throw new InvalidOperationException("安装包包含未知的路径。");
        }

        private static void EnsureInside(string installDir, string destination)
        {
            string root = installDir.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(destination);
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("安装包包含不安全的文件路径。");
            }
        }

        private static string AppendWarning(string existing, string warning)
        {
            if (string.IsNullOrWhiteSpace(existing))
            {
                return warning;
            }

            return existing + " " + warning;
        }

        public static void LaunchApp(string installDir)
        {
            string app = Path.Combine(installDir, AppInfo.Executable);
            if (!File.Exists(app))
            {
                throw new FileNotFoundException("找不到主程序。", app);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = app,
                WorkingDirectory = installDir,
                UseShellExecute = true,
            });
        }
    }
}
