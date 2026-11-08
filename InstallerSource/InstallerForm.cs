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
    internal sealed class InstallerForm : LumenFormBase
    {
        private RoundedPathField _pathField;
        private ToggleCheckBox _desktopCheck;
        private ToggleCheckBox _sourceCheck;
        private ToggleCheckBox _launchCheck;
        private RuntimeCard _runtimeCard;
        private CopyrightNotice _copyrightCard;
        private ToggleCheckBox _consentCheck;
        private Label _consentHint;
        private ProgressBarEx _progress;
        private Label _statusLabel;
        private Label _title;
        private Label _subtitle;
        private Label _version;
        private Label _locationLabel;
        private Label _locationHint;
        private Label _optionsLabel;
        private Label _sourceHint;
        private Label _footerLabel;
        private Label _successTitle;
        private Label _successDetail;
        private Label _successWarning;
        private Label _successFooter;
        private Label _successTip;
        private SuccessBadge _successBadge;
        private AccentButton _installButton;
        private AccentButton _doneButton;
        private OutlineButton _cancelButton;
        private OutlineButton _openFolderButton;
        private Panel _separator;
        private Panel _footerSeparator;
        private Panel _successSeparator;
        private readonly List<Control> _installControls = new List<Control>();
        private readonly List<Control> _successControls = new List<Control>();
        private bool _installing;
        private bool _finished;
        private InstallOutcome _result;

        protected override int DesignWidth
        {
            get { return 760; }
        }

        protected override int DesignHeight
        {
            get { return 600; }
        }

        protected override string TopBarKind
        {
            get { return "安装程序"; }
        }

        protected override void BuildUi()
        {
            Text = AppInfo.Name + " 安装程序";
            BuildInstallPage();
            BuildSuccessPage();
            AcceptButton = _installButton;
            SetRuntimeStatus();
        }

        private void BuildInstallPage()
        {
            var logo = new LogoView(BrandLogo);
            logo.Bounds = new Rectangle(UiScale.S(32), UiScale.S(68), UiScale.S(56), UiScale.S(56));
            Controls.Add(logo);

            _title = MakeLabel("安装 流光下载器", 20f, FontStyle.Bold, Palette.Text);
            _title.Bounds = new Rectangle(UiScale.S(104), UiScale.S(71), UiScale.S(600), UiScale.S(32));
            Controls.Add(_title);

            _subtitle = MakeLabel(AppInfo.AppDescription, 9.5f, FontStyle.Regular, Palette.TextMuted);
            _subtitle.Bounds = new Rectangle(UiScale.S(106), UiScale.S(104), UiScale.S(600), UiScale.S(20));
            Controls.Add(_subtitle);

            _version = MakeLabel("v" + AppInfo.Version + "   ·   免费、无广告   ·   安装到当前用户", 8.5f, FontStyle.Regular, Palette.TextDim);
            _version.Bounds = new Rectangle(UiScale.S(106), UiScale.S(126), UiScale.S(600), UiScale.S(18));
            Controls.Add(_version);

            _separator = new Panel();
            _separator.BackColor = Palette.LineSoft;
            _separator.Bounds = new Rectangle(UiScale.S(32), UiScale.S(158), UiScale.S(696), 1);
            Controls.Add(_separator);

            _locationLabel = MakeLabel("安装位置", 9f, FontStyle.Bold, Palette.Text);
            _locationLabel.Bounds = new Rectangle(UiScale.S(32), UiScale.S(174), UiScale.S(400), UiScale.S(18));
            Controls.Add(_locationLabel);

            _pathField = new RoundedPathField();
            _pathField.Bounds = new Rectangle(UiScale.S(32), UiScale.S(196), UiScale.S(696), UiScale.S(40));
            _pathField.Value = AppPaths.ExistingInstallDir();
            Controls.Add(_pathField);

            _locationHint = MakeLabel(
                "无需管理员权限。请选择空的专用文件夹；安装器只写入本应用文件，卸载时也不会删除其他文件。",
                8.25f,
                FontStyle.Regular,
                Palette.TextDim);
            _locationHint.Bounds = new Rectangle(UiScale.S(32), UiScale.S(243), UiScale.S(696), UiScale.S(16));
            Controls.Add(_locationHint);

            _optionsLabel = MakeLabel("安装选项", 9f, FontStyle.Bold, Palette.Text);
            _optionsLabel.Bounds = new Rectangle(UiScale.S(32), UiScale.S(276), UiScale.S(300), UiScale.S(18));
            Controls.Add(_optionsLabel);

            _desktopCheck = new ToggleCheckBox();
            _desktopCheck.Text = "创建桌面快捷方式";
            _desktopCheck.Checked = true;
            _desktopCheck.Bounds = new Rectangle(UiScale.S(32), UiScale.S(300), UiScale.S(360), UiScale.S(26));
            Controls.Add(_desktopCheck);

            _sourceCheck = new ToggleCheckBox();
            _sourceCheck.Text = "附带完整源代码（保存至 Source）";
            _sourceCheck.Checked = true;
            _sourceCheck.Bounds = new Rectangle(UiScale.S(32), UiScale.S(328), UiScale.S(318), UiScale.S(26));
            Controls.Add(_sourceCheck);

            _sourceHint = MakeLabel("可自行编译研究，不影响程序使用", 8f, FontStyle.Regular, Palette.TextDim);
            _sourceHint.Bounds = new Rectangle(UiScale.S(362), UiScale.S(331), UiScale.S(366), UiScale.S(20));
            _sourceHint.TextAlign = ContentAlignment.MiddleLeft;
            Controls.Add(_sourceHint);

            _runtimeCard = new RuntimeCard();
            _runtimeCard.Bounds = new Rectangle(UiScale.S(32), UiScale.S(364), UiScale.S(696), UiScale.S(46));
            Controls.Add(_runtimeCard);

            _copyrightCard = new CopyrightNotice();
            _copyrightCard.Bounds = new Rectangle(UiScale.S(32), UiScale.S(418), UiScale.S(696), UiScale.S(54));
            Controls.Add(_copyrightCard);

            _consentCheck = new ToggleCheckBox();
            _consentCheck.Text = "我已阅读并同意上述版权声明，不会未经允许外传本应用";
            _consentCheck.Checked = false;
            _consentCheck.Font = DrawUtil.UiFont(8.75f, FontStyle.Regular);
            _consentCheck.Bounds = new Rectangle(UiScale.S(32), UiScale.S(479), UiScale.S(520), UiScale.S(26));
            Controls.Add(_consentCheck);

            _consentHint = MakeLabel("必须勾选后才能安装", 8.25f, FontStyle.Bold, Palette.Amber);
            _consentHint.Bounds = new Rectangle(UiScale.S(560), UiScale.S(481), UiScale.S(168), UiScale.S(20));
            _consentHint.TextAlign = ContentAlignment.MiddleRight;
            Controls.Add(_consentHint);

            _statusLabel = MakeLabel("", 8.5f, FontStyle.Regular, Palette.TextMuted);
            _statusLabel.Bounds = new Rectangle(UiScale.S(32), UiScale.S(426), UiScale.S(696), UiScale.S(18));
            _statusLabel.Visible = false;
            Controls.Add(_statusLabel);

            _progress = new ProgressBarEx();
            _progress.Bounds = new Rectangle(UiScale.S(32), UiScale.S(450), UiScale.S(696), UiScale.S(5));
            _progress.Visible = false;
            Controls.Add(_progress);

            var footerSeparator = new Panel();
            footerSeparator.Name = "FooterSeparator";
            footerSeparator.BackColor = Palette.LineSoft;
            footerSeparator.Bounds = new Rectangle(UiScale.S(32), UiScale.S(510), UiScale.S(696), 1);
            Controls.Add(footerSeparator);
            _footerSeparator = footerSeparator;

            _footerLabel = MakeLabel("本地安装   ·   不收集数据   ·   无广告", 8f, FontStyle.Regular, Palette.TextDim);
            _footerLabel.Bounds = new Rectangle(UiScale.S(32), UiScale.S(528), UiScale.S(360), UiScale.S(20));
            Controls.Add(_footerLabel);

            _cancelButton = new OutlineButton();
            _cancelButton.Text = "取消";
            _cancelButton.Font = DrawUtil.UiFont(9f, FontStyle.Regular);
            _cancelButton.Bounds = new Rectangle(UiScale.S(470), UiScale.S(520), UiScale.S(84), UiScale.S(40));
            _cancelButton.Click += delegate { Close(); };
            Controls.Add(_cancelButton);

            _installButton = new AccentButton();
            _installButton.Text = "立即安装";
            _installButton.Bounds = new Rectangle(UiScale.S(566), UiScale.S(518), UiScale.S(162), UiScale.S(42));
            _installButton.Click += delegate { StartInstall(); };
            Controls.Add(_installButton);

            _installControls.Add(logo);
            _installControls.Add(_title);
            _installControls.Add(_subtitle);
            _installControls.Add(_version);
            _installControls.Add(_separator);
            _installControls.Add(_locationLabel);
            _installControls.Add(_pathField);
            _installControls.Add(_locationHint);
            _installControls.Add(_optionsLabel);
            _installControls.Add(_desktopCheck);
            _installControls.Add(_sourceCheck);
            _installControls.Add(_sourceHint);
            _installControls.Add(_runtimeCard);
            _installControls.Add(_copyrightCard);
            _installControls.Add(_consentCheck);
            _installControls.Add(_consentHint);
            _installControls.Add(_statusLabel);
            _installControls.Add(_progress);
            _installControls.Add(_footerSeparator);
            _installControls.Add(_footerLabel);
            _installControls.Add(_cancelButton);
            _installControls.Add(_installButton);
        }

        private void BuildSuccessPage()
        {
            _successBadge = new SuccessBadge();
            _successBadge.Bounds = new Rectangle(UiScale.S(350), UiScale.S(92), UiScale.S(60), UiScale.S(60));
            _successBadge.Visible = false;
            Controls.Add(_successBadge);
            _successControls.Add(_successBadge);

            _successTitle = MakeLabel("安装完成", 21f, FontStyle.Bold, Palette.Text);
            _successTitle.TextAlign = ContentAlignment.MiddleCenter;
            _successTitle.Bounds = new Rectangle(UiScale.S(80), UiScale.S(168), UiScale.S(600), UiScale.S(38));
            _successTitle.Visible = false;
            Controls.Add(_successTitle);
            _successControls.Add(_successTitle);

            _successDetail = MakeLabel("", 9.25f, FontStyle.Regular, Palette.TextMuted);
            _successDetail.TextAlign = ContentAlignment.MiddleCenter;
            _successDetail.Bounds = new Rectangle(UiScale.S(80), UiScale.S(216), UiScale.S(600), UiScale.S(58));
            _successDetail.Visible = false;
            Controls.Add(_successDetail);
            _successControls.Add(_successDetail);

            _successWarning = MakeLabel("", 8.25f, FontStyle.Regular, Palette.Amber);
            _successWarning.TextAlign = ContentAlignment.MiddleCenter;
            _successWarning.Bounds = new Rectangle(UiScale.S(80), UiScale.S(280), UiScale.S(600), UiScale.S(44));
            _successWarning.Visible = false;
            Controls.Add(_successWarning);

            _launchCheck = new ToggleCheckBox();
            _launchCheck.Text = "立即启动流光下载器";
            _launchCheck.Checked = true;
            _launchCheck.Font = DrawUtil.UiFont(9f, FontStyle.Regular);
            _launchCheck.Bounds = new Rectangle(UiScale.S(310), UiScale.S(354), UiScale.S(200), UiScale.S(28));
            _launchCheck.Visible = false;
            Controls.Add(_launchCheck);
            _successControls.Add(_launchCheck);

            _successTip = MakeLabel("可从桌面快捷方式、开始菜单或 Windows 搜索启动。", 8.25f, FontStyle.Regular, Palette.TextDim);
            _successTip.TextAlign = ContentAlignment.MiddleCenter;
            _successTip.Bounds = new Rectangle(UiScale.S(80), UiScale.S(402), UiScale.S(600), UiScale.S(24));
            _successTip.Visible = false;
            Controls.Add(_successTip);
            _successControls.Add(_successTip);

            _successSeparator = new Panel();
            _successSeparator.Name = "SuccessSeparator";
            _successSeparator.BackColor = Palette.LineSoft;
            _successSeparator.Bounds = new Rectangle(UiScale.S(32), UiScale.S(510), UiScale.S(696), 1);
            _successSeparator.Visible = false;
            Controls.Add(_successSeparator);
            _successControls.Add(_successSeparator);

            _openFolderButton = new OutlineButton();
            _openFolderButton.Text = "打开安装目录";
            _openFolderButton.Font = DrawUtil.UiFont(9f, FontStyle.Regular);
            _openFolderButton.Bounds = new Rectangle(UiScale.S(32), UiScale.S(526), UiScale.S(134), UiScale.S(38));
            _openFolderButton.Visible = false;
            _openFolderButton.Click += delegate { OpenInstallFolder(); };
            Controls.Add(_openFolderButton);
            _successControls.Add(_openFolderButton);

            _doneButton = new AccentButton();
            _doneButton.Text = "完成";
            _doneButton.Bounds = new Rectangle(UiScale.S(566), UiScale.S(518), UiScale.S(162), UiScale.S(42));
            _doneButton.Visible = false;
            _doneButton.Click += delegate { FinishAndMaybeLaunch(); };
            Controls.Add(_doneButton);
            _successControls.Add(_doneButton);

            _successFooter = MakeLabel("可以随时在 Windows“应用和功能”中卸载", 8f, FontStyle.Regular, Palette.TextDim);
            _successFooter.Name = "SuccessFooter";
            _successFooter.Bounds = new Rectangle(UiScale.S(184), UiScale.S(538), UiScale.S(350), UiScale.S(20));
            _successFooter.Visible = false;
            Controls.Add(_successFooter);
            _successControls.Add(_successFooter);
        }

        private static Label MakeLabel(string text, float size, FontStyle style, Color color)
        {
            var label = new Label();
            label.Text = text;
            label.AutoSize = false;
            label.BackColor = Color.Transparent;
            label.ForeColor = color;
            label.Font = DrawUtil.UiFont(size, style);
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.UseMnemonic = false;
            return label;
        }

        private void SetRuntimeStatus()
        {
            string dotnetVersion;
            string webViewVersion;
            bool dotnet = RuntimeCheck.HasDotNet10(out dotnetVersion);
            bool webView = RuntimeCheck.HasWebView2(out webViewVersion);
            if (dotnet && webView)
            {
                _runtimeCard.Ready = true;
                _runtimeCard.TitleText = "运行环境已就绪";
                _runtimeCard.DetailText = ".NET " + dotnetVersion + " Desktop Runtime   ·   Microsoft Edge WebView2 " + webViewVersion;
            }
            else
            {
                _runtimeCard.Ready = false;
                _runtimeCard.TitleText = "安装后可能还需要补充运行环境";
                string missing = !dotnet && !webView
                    ? "缺少 .NET 10 桌面运行时和 WebView2"
                    : (!dotnet ? "缺少 .NET 10 桌面运行时" : "缺少 Microsoft Edge WebView2");
                _runtimeCard.DetailText = missing + "，请按安装目录中的《使用说明》补装后再启动。";
            }
        }

        private void StartInstall()
        {
            if (_installing)
            {
                return;
            }

            string directory = _pathField.Value;
            if (string.IsNullOrWhiteSpace(directory))
            {
                MessageBox.Show(this, "请先选择安装位置。", "无法安装", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!_consentCheck.Checked)
            {
                MessageBox.Show(
                    this,
                    "请先阅读版权声明，并勾选“我已阅读并同意上述版权声明，不会未经允许外传本应用”后再开始安装。",
                    "请先同意版权声明",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                _consentCheck.Focus();
                return;
            }

            string validationError;
            if (!PackageInstaller.ValidateTarget(directory, out validationError))
            {
                MessageBox.Show(
                    this,
                    validationError,
                    "安装位置不可用",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            if (PackageInstaller.IsAppRunning())
            {
                MessageBox.Show(
                    this,
                    "流光下载器正在运行。请先关闭已打开的下载器，再继续安装。",
                    "请先关闭程序",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            bool desktop = _desktopCheck.Checked;
            bool source = _sourceCheck.Checked;

            _installing = true;
            SetBusy(true);
            _copyrightCard.Visible = false;
            _consentCheck.Visible = false;
            _consentHint.Visible = false;
            _statusLabel.Text = "正在准备安装…";
            _statusLabel.Visible = true;
            _progress.Value = 0;
            _progress.Visible = true;
            _installButton.Text = "正在安装…";

            var options = new InstallOptions
            {
                InstallDirectory = directory,
                IncludeSource = source,
                CreateDesktopShortcut = desktop,
                CreateStartMenuShortcuts = true,
                ConsentAccepted = true,
            };

            var thread = new Thread(delegate()
            {
                InstallOutcome outcome = PackageInstaller.Install(options, ReportProgress);
                try
                {
                    BeginInvoke(new Action(delegate { CompleteInstall(outcome); }));
                }
                catch
                {
                    // The window was closed unexpectedly.
                }
            });
            thread.IsBackground = true;
            thread.Start();
        }

        private void ReportProgress(int value, string message)
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }

            try
            {
                BeginInvoke(new Action(delegate
                {
                    if (!_installing || _finished)
                    {
                        return;
                    }
                    _progress.Value = value;
                    _statusLabel.Text = message;
                }));
            }
            catch
            {
                // Updating the UI is best-effort during shutdown.
            }
        }

        private void SetBusy(bool busy)
        {
            _pathField.Enabled = !busy;
            _desktopCheck.Enabled = !busy;
            _sourceCheck.Enabled = !busy;
            _consentCheck.Enabled = !busy;
            _installButton.Enabled = !busy;
            _cancelButton.Enabled = !busy;
            SetCloseEnabled(!busy);
            if (!busy)
            {
                _installButton.Text = "立即安装";
            }
        }

        private void CompleteInstall(InstallOutcome outcome)
        {
            _installing = false;
            SetBusy(false);
            _progress.Visible = false;
            _statusLabel.Visible = false;
            _result = outcome;

            if (!outcome.Ok)
            {
                _installButton.Text = "重试安装";
                _copyrightCard.Visible = true;
                _consentCheck.Visible = true;
                _consentHint.Visible = true;
                MessageBox.Show(
                    this,
                    "安装没有完成。\n\n" + (string.IsNullOrWhiteSpace(outcome.Error) ? "请稍后重试。" : outcome.Error) +
                    "\n\n诊断日志：" + SetupLog.LogPath,
                    "安装失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            _finished = true;
            HideInstallControls();
            ShowSuccessControls();
        }

        private void HideInstallControls()
        {
            foreach (Control control in _installControls)
            {
                control.Visible = false;
            }
        }

        private void ShowSuccessControls()
        {
            foreach (Control control in _successControls)
            {
                control.Visible = true;
            }

            _successDetail.Text =
                AppInfo.Name + " 已安装到\n" + _result.InstallDirectory +
                (_result.SourceInstalled ? "\n源代码保存在 Source 子文件夹中。" : "");
            _successWarning.Text = _result.Warning;

            if (!string.IsNullOrWhiteSpace(_result.Warning))
            {
                _successWarning.Visible = true;
                _successWarning.BringToFront();
            }

            AcceptButton = _doneButton;
            _doneButton.Focus();
        }

        private void OpenInstallFolder()
        {
            if (_result == null || string.IsNullOrWhiteSpace(_result.InstallDirectory))
            {
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _result.InstallDirectory,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "无法打开安装目录：\n" + ex.Message, "打开失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void FinishAndMaybeLaunch()
        {
            if (_result == null || !_result.Ok)
            {
                Close();
                return;
            }

            if (_launchCheck.Checked)
            {
                try
                {
                    PackageInstaller.LaunchApp(_result.InstallDirectory);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "安装已完成，但启动主程序失败：\n" + ex.Message, "启动失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }

            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_installing)
            {
                e.Cancel = true;
                return;
            }
            base.OnFormClosing(e);
        }
    }

    internal static class InstallerProgram
    {
        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                SetupLog.Write("Installer process started. args=" + string.Join(" ", args));
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                if (args != null && args.Any(a => string.Equals(a, "/S", StringComparison.OrdinalIgnoreCase)))
                {
                    int silentCode = RunSilent(args);
                    SetupLog.Write("Silent installer finished with code " + silentCode + ".");
                    return silentCode;
                }

                Application.Run(new InstallerForm());
                return 0;
            }
            catch (Exception ex)
            {
                SetupLog.Write("Installer crash: " + ex);
                try
                {
                    MessageBox.Show("安装程序遇到问题：\n" + ex.Message, AppInfo.Name + " 安装程序", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                catch
                {
                }
                return 1;
            }
        }

        private static int RunSilent(string[] args)
        {
            string directory = AppPaths.ExistingInstallDir();
            foreach (string arg in args)
            {
                if (arg.StartsWith("/D=", StringComparison.OrdinalIgnoreCase))
                {
                    directory = arg.Substring(3).Trim('"');
                }
            }

            if (PackageInstaller.IsAppRunning())
            {
                SetupLog.Write("Silent install aborted: application is running.");
                return 2;
            }

            bool accepted = args.Any(a => string.Equals(a, "/ACCEPT", StringComparison.OrdinalIgnoreCase));
            if (!accepted)
            {
                SetupLog.Write("Silent install aborted: /ACCEPT is required.");
                return 3;
            }

            var options = new InstallOptions
            {
                InstallDirectory = directory,
                IncludeSource = !args.Any(a => string.Equals(a, "/NOSOURCE", StringComparison.OrdinalIgnoreCase)),
                CreateDesktopShortcut = !args.Any(a => string.Equals(a, "/NOSHORTCUT", StringComparison.OrdinalIgnoreCase)),
                CreateStartMenuShortcuts = !args.Any(a => string.Equals(a, "/NOSTARTMENU", StringComparison.OrdinalIgnoreCase)),
                ConsentAccepted = accepted,
            };
            InstallOutcome outcome = PackageInstaller.Install(options, delegate { });
            return outcome.Ok ? 0 : 1;
        }
    }
}
