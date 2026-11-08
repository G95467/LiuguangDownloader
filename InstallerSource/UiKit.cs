using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LumenInstaller
{
    internal static class UiScale
    {
        public static float Factor = 1f;

        public static int S(float value)
        {
            return (int)Math.Round(value * Factor);
        }

        public static Size Sz(float width, float height)
        {
            return new Size(S(width), S(height));
        }
    }

    internal abstract class LumenFormBase : Form
    {
        private Image _logo;
        private CloseGlyphButton _closeButton;
        private IntPtr _handle;
        private const int WmNcLButtonDown = 0x00A1;
        private const int HtCaption = 2;

        protected LumenFormBase()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Palette.Window;
            ForeColor = Palette.Text;
            Font = DrawUtil.UiFont(9f, FontStyle.Regular);
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;
            KeyPreview = true;
            Opacity = 0;
            try
            {
                _logo = LoadLogo();
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch
            {
                _logo = null;
            }
        }

        protected abstract int DesignWidth { get; }
        protected abstract int DesignHeight { get; }
        protected abstract string TopBarKind { get; }

        protected Image BrandLogo
        {
            get { return _logo; }
        }

        protected abstract void BuildUi();

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            using (Graphics graphics = CreateGraphics())
            {
                UiScale.Factor = graphics.DpiX / 96f;
            }

            ClientSize = new Size(UiScale.S(DesignWidth), UiScale.S(DesignHeight));
            BuildUi();
            CreateWindowChrome();
            CenterToScreen();
            Opacity = 1;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            _handle = Handle;
            try
            {
                int dark = 1;
                DwmSetWindowAttribute(_handle, 20, ref dark, sizeof(int));
                int round = 2;
                DwmSetWindowAttribute(_handle, 33, ref round, sizeof(int));
            }
            catch
            {
                // DWM is only available on Windows 10 and 11.
            }
        }

        private void CreateWindowChrome()
        {
            _closeButton = new CloseGlyphButton();
            _closeButton.Bounds = new Rectangle(
                ClientSize.Width - UiScale.S(48),
                0,
                UiScale.S(48),
                UiScale.S(48));
            _closeButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _closeButton.Click += delegate { Close(); };
            Controls.Add(_closeButton);
            _closeButton.BringToFront();
        }

        protected void SetCloseEnabled(bool enabled)
        {
            if (_closeButton != null)
            {
                _closeButton.Enabled = enabled;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(Palette.Window);

            DrawGlows(g);

            var topBar = new Rectangle(0, 0, ClientSize.Width, UiScale.S(48));
            using (var topBrush = new SolidBrush(Palette.TopBar))
            {
                g.FillRectangle(topBrush, topBar);
            }
            using (var linePen = new Pen(Palette.LineSoft))
            {
                g.DrawLine(linePen, 0, topBar.Bottom, ClientSize.Width, topBar.Bottom);
            }

            int logoSize = UiScale.S(24);
            var logoRect = new Rectangle(UiScale.S(16), UiScale.S(12), logoSize, logoSize);
            if (_logo != null)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(_logo, logoRect);
            }

            using (Font nameFont = DrawUtil.UiFont(10.5f, FontStyle.Bold))
            using (Font kindFont = DrawUtil.UiFont(8f, FontStyle.Regular))
            using (var textBrush = new SolidBrush(Palette.Text))
            {
                TextRenderer.DrawText(
                    g,
                    AppInfo.Name,
                    nameFont,
                    new Point(UiScale.S(48), UiScale.S(16)),
                    Palette.Text,
                    TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

                int nameWidth = TextRenderer.MeasureText(g, AppInfo.Name, nameFont, Size.Empty, TextFormatFlags.NoPadding).Width;
                int pillX = UiScale.S(48) + nameWidth + UiScale.S(9);
                int pillWidth = TextRenderer.MeasureText(g, TopBarKind, kindFont, Size.Empty, TextFormatFlags.NoPadding).Width + UiScale.S(14);
                var pillRect = new Rectangle(pillX, UiScale.S(16), pillWidth, UiScale.S(17));
                using (GraphicsPath path = DrawUtil.RoundedRect(pillRect, UiScale.S(4)))
                using (var fill = new SolidBrush(Palette.CardRaised))
                using (var border = new Pen(Palette.Line))
                {
                    g.FillPath(fill, path);
                    g.DrawPath(border, path);
                }
                TextRenderer.DrawText(
                    g,
                    TopBarKind,
                    kindFont,
                    pillRect,
                    Palette.TextMuted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }

        protected virtual void DrawGlows(Graphics g)
        {
            using (var path = new GraphicsPath())
            {
                var rect = new Rectangle(UiScale.S(410), UiScale.S(-270), UiScale.S(620), UiScale.S(620));
                path.AddEllipse(rect);
                using (var brush = new PathGradientBrush(path))
                {
                    brush.CenterColor = Color.FromArgb(42, 62, 143, 255);
                    brush.SurroundColors = new[] { Color.Transparent };
                    g.FillPath(brush, path);
                }
            }

            using (var path = new GraphicsPath())
            {
                var rect = new Rectangle(UiScale.S(-260), UiScale.S(400), UiScale.S(620), UiScale.S(540));
                path.AddEllipse(rect);
                using (var brush = new PathGradientBrush(path))
                {
                    brush.CenterColor = Color.FromArgb(25, 138, 103, 255);
                    brush.SurroundColors = new[] { Color.Transparent };
                    g.FillPath(brush, path);
                }
            }

            using (var dotBrush = new SolidBrush(Color.FromArgb(17, 117, 166, 255)))
            using (var dotSoft = new SolidBrush(Color.FromArgb(10, 117, 166, 255)))
            {
                var rnd = new Random(27);
                for (int i = 0; i < 48; i++)
                {
                    int x = UiScale.S(340 + rnd.Next(0, 390));
                    int y = UiScale.S(60 + rnd.Next(0, 470));
                    int size = rnd.Next(1, 3);
                    g.FillEllipse(i % 4 == 0 ? dotBrush : dotSoft, x, y, Math.Max(1, UiScale.S(size)), Math.Max(1, UiScale.S(size)));
                }
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && e.Y < UiScale.S(48))
            {
                ReleaseCapture();
                SendMessage(Handle, WmNcLButtonDown, (IntPtr)HtCaption, IntPtr.Zero);
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape)
            {
                Close();
            }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ClassStyle |= 0x20000; // CS_DROPSHADOW
                return parameters;
            }
        }

        private static Image LoadLogo()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("LumenLogo.png"))
            {
                if (stream == null)
                {
                    return null;
                }

                using (var image = Image.FromStream(stream))
                {
                    return new Bitmap(image);
                }
            }
        }

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }

    internal class CloseGlyphButton : Button
    {
        private bool _hover;

        public CloseGlyphButton()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Palette.TopBar;
            TabStop = true;
            Cursor = Cursors.Hand;
            MouseEnter += delegate { _hover = true; Invalidate(); };
            MouseLeave += delegate { _hover = false; Invalidate(); };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(_hover ? Palette.RedSoft : Palette.TopBar);
            int x = (Width - UiScale.S(10)) / 2;
            int y = (Height - UiScale.S(10)) / 2;
            using (var pen = new Pen(_hover ? Color.White : Palette.TextMuted, Math.Max(1.3f, UiScale.Factor * 1.2f)))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawLine(pen, x, y, x + UiScale.S(10), y + UiScale.S(10));
                g.DrawLine(pen, x + UiScale.S(10), y, x, y + UiScale.S(10));
            }
            if (Focused)
            {
                using (var focus = new Pen(Palette.AccentCyan))
                {
                    g.DrawRectangle(focus, 2, 2, Width - 5, Height - 5);
                }
            }
        }
    }

    internal class AccentButton : Button
    {
        private bool _hover;
        private bool _pressed;

        public AccentButton()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Palette.Window;
            ForeColor = Palette.Ink;
            Font = DrawUtil.UiFont(9.5f, FontStyle.Bold);
            Cursor = Cursors.Hand;
            TabStop = true;
            MouseEnter += delegate { _hover = true; Invalidate(); };
            MouseLeave += delegate { _hover = false; _pressed = false; Invalidate(); };
            MouseDown += delegate { _pressed = true; Invalidate(); };
            MouseUp += delegate { _pressed = false; Invalidate(); };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Palette.Window);
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            if (!Enabled)
            {
                using (GraphicsPath path = DrawUtil.RoundedRect(rect, UiScale.S(10)))
                using (var brush = new SolidBrush(Palette.CardRaised))
                {
                    g.FillPath(brush, path);
                }
                TextRenderer.DrawText(g, Text, Font, rect, Palette.TextDim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                return;
            }

            Color cyan = _hover ? Color.FromArgb(125, 234, 255) : Palette.AccentCyan;
            Color blue = _hover ? Color.FromArgb(135, 160, 255) : Palette.AccentBlue;
            Color violet = _hover ? Color.FromArgb(197, 164, 255) : Palette.AccentViolet;
            using (var path = DrawUtil.RoundedRect(rect, UiScale.S(10)))
            using (var brush = new LinearGradientBrush(rect, cyan, violet, 12f))
            {
                brush.InterpolationColors = new ColorBlend
                {
                    Colors = new[] { cyan, blue, violet },
                    Positions = new[] { 0f, 0.55f, 1f },
                };
                g.FillPath(brush, path);
            }

            if (_pressed)
            {
                using (var overlay = new SolidBrush(Color.FromArgb(35, 0, 0, 0)))
                using (GraphicsPath path = DrawUtil.RoundedRect(rect, UiScale.S(10)))
                {
                    g.FillPath(overlay, path);
                }
            }

            TextRenderer.DrawText(
                g,
                Text,
                Font,
                rect,
                Palette.Ink,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            if (Focused)
            {
                Rectangle focus = rect;
                focus.Inflate(-UiScale.S(3), -UiScale.S(3));
                using (var pen = new Pen(Color.FromArgb(155, 4, 19, 30)))
                {
                    g.DrawPath(pen, DrawUtil.RoundedRect(focus, UiScale.S(7)));
                }
            }
        }
    }

    internal class DangerButton : Button
    {
        private bool _hover;
        private bool _pressed;

        public DangerButton()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Palette.Window;
            ForeColor = Color.White;
            Font = DrawUtil.UiFont(9.5f, FontStyle.Bold);
            Cursor = Cursors.Hand;
            TabStop = true;
            MouseEnter += delegate { _hover = true; Invalidate(); };
            MouseLeave += delegate { _hover = false; _pressed = false; Invalidate(); };
            MouseDown += delegate { _pressed = true; Invalidate(); };
            MouseUp += delegate { _pressed = false; Invalidate(); };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Palette.Window);
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = DrawUtil.RoundedRect(rect, UiScale.S(9)))
            using (var brush = new LinearGradientBrush(
                rect,
                _hover ? Color.FromArgb(255, 132, 145) : Palette.Red,
                Color.FromArgb(196, 58, 74),
                12f))
            {
                g.FillPath(brush, path);
            }
            if (_pressed)
            {
                using (var overlay = new SolidBrush(Color.FromArgb(35, 0, 0, 0)))
                using (GraphicsPath path = DrawUtil.RoundedRect(rect, UiScale.S(9)))
                {
                    g.FillPath(overlay, path);
                }
            }
            TextRenderer.DrawText(
                g,
                Text,
                Font,
                rect,
                Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            if (Focused)
            {
                using (var focus = new Pen(Color.FromArgb(210, 255, 255, 255)))
                {
                    g.DrawPath(focus, DrawUtil.RoundedRect(rect, UiScale.S(9)));
                }
            }
        }
    }

    internal class OutlineButton : Button
    {        private bool _hover;

        public OutlineButton()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Palette.Window;
            ForeColor = Palette.Text;
            Font = DrawUtil.UiFont(9f, FontStyle.Regular);
            Cursor = Cursors.Hand;
            TabStop = true;
            MouseEnter += delegate { _hover = true; Invalidate(); };
            MouseLeave += delegate { _hover = false; Invalidate(); };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Palette.Window);
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = DrawUtil.RoundedRect(rect, UiScale.S(9)))
            {
                using (var fill = new SolidBrush(_hover ? Palette.CardRaised : Palette.Card))
                {
                    g.FillPath(fill, path);
                }
                using (var border = new Pen(_hover ? Palette.Line : Palette.LineSoft))
                {
                    g.DrawPath(border, path);
                }
            }
            TextRenderer.DrawText(
                g,
                Text,
                Font,
                rect,
                Enabled ? Palette.Text : Palette.TextDim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            if (Focused)
            {
                using (var focus = new Pen(Palette.AccentBlue))
                {
                    g.DrawPath(focus, DrawUtil.RoundedRect(rect, UiScale.S(9)));
                }
            }
        }
    }

    internal class FieldButton : Button
    {
        private bool _hover;

        public FieldButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Palette.Field;
            ForeColor = Palette.TextMuted;
            Font = DrawUtil.UiFont(8.5f, FontStyle.Regular);
            Cursor = Cursors.Hand;
            TabStop = true;
            MouseEnter += delegate { _hover = true; Invalidate(); };
            MouseLeave += delegate { _hover = false; Invalidate(); };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Palette.Field);
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = DrawUtil.RoundedRect(rect, UiScale.S(6)))
            using (var fill = new SolidBrush(_hover ? Palette.CardRaised : Palette.Card))
            using (var border = new Pen(_hover ? Palette.Line : Palette.LineSoft))
            {
                g.FillPath(fill, path);
                g.DrawPath(border, path);
            }
            TextRenderer.DrawText(g, Text, Font, rect, _hover ? Palette.Text : Palette.TextMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    internal class ToggleCheckBox : CheckBox
    {
        private bool _hover;

        public ToggleCheckBox()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.SupportsTransparentBackColor,
                true);
            Appearance = Appearance.Button;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            AutoSize = false;
            BackColor = Palette.Window;
            ForeColor = Palette.TextMuted;
            Font = DrawUtil.UiFont(8.75f, FontStyle.Regular);
            Cursor = Cursors.Hand;
            TabStop = true;
            Height = UiScale.S(26);
            MouseEnter += delegate { _hover = true; Invalidate(); };
            MouseLeave += delegate { _hover = false; Invalidate(); };
            CheckedChanged += delegate { Invalidate(); };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Palette.Window);
            var box = new Rectangle(0, (Height - UiScale.S(17)) / 2, UiScale.S(17), UiScale.S(17));
            using (GraphicsPath path = DrawUtil.RoundedRect(box, UiScale.S(4)))
            {
                if (Checked)
                {
                    using (var brush = DrawUtil.AccentBrush(box))
                    {
                        g.FillPath(brush, path);
                    }
                }
                else
                {
                    using (var brush = new SolidBrush(_hover ? Palette.CardRaised : Palette.Card))
                    {
                        g.FillPath(brush, path);
                    }
                    using (var border = new Pen(_hover ? Palette.Line : Palette.LineSoft))
                    {
                        g.DrawPath(border, path);
                    }
                }
            }

            if (Checked)
            {
                using (var pen = new Pen(Palette.Ink, Math.Max(1.5f, 1.6f * UiScale.Factor)))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    int x = box.X + UiScale.S(4);
                    int y = box.Y + UiScale.S(8);
                    g.DrawLine(pen, x, y, x + UiScale.S(3), y + UiScale.S(4));
                    g.DrawLine(pen, x + UiScale.S(3), y + UiScale.S(4), x + UiScale.S(9), y - UiScale.S(4));
                }
            }

            int textX = UiScale.S(25);
            var textRect = new Rectangle(textX, 0, Math.Max(1, Width - textX), Height);
            TextRenderer.DrawText(
                g,
                Text,
                Font,
                textRect,
                Checked || _hover ? Palette.Text : Palette.TextMuted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);

            if (Focused)
            {
                using (var pen = new Pen(Color.FromArgb(90, Palette.AccentBlue)))
                {
                    g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
                }
            }
        }
    }

    internal sealed class CopyrightNotice : Control
    {
        public CopyrightNotice()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor,
                true);
            BackColor = Palette.Window;
            TabStop = false;
            AccessibleName = "版权声明：" + AppInfo.CopyrightNotice;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = DrawUtil.RoundedRect(rect, UiScale.S(9)))
            using (var fill = new SolidBrush(Palette.AmberSoft))
            using (var border = new Pen(Color.FromArgb(135, Palette.Amber)))
            {
                g.FillPath(fill, path);
                g.DrawPath(border, path);
            }

            int badgeSize = UiScale.S(26);
            var badge = new Rectangle(
                UiScale.S(14),
                (Height - badgeSize) / 2,
                badgeSize,
                badgeSize);
            using (var circle = new Pen(Color.FromArgb(210, Palette.Amber), Math.Max(1f, UiScale.Factor)))
            {
                g.DrawEllipse(circle, badge);
            }
            using (Font markFont = DrawUtil.UiFont(12f, FontStyle.Bold))
            {
                TextRenderer.DrawText(
                    g,
                    "©",
                    markFont,
                    badge,
                    Palette.Amber,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }

            int textX = UiScale.S(52);
            int textWidth = Math.Max(UiScale.S(80), Width - textX - UiScale.S(14));
            using (Font eyebrow = DrawUtil.UiFont(8f, FontStyle.Bold))
            {
                TextRenderer.DrawText(
                    g,
                    "版权声明",
                    eyebrow,
                    new Rectangle(textX, UiScale.S(6), textWidth, UiScale.S(16)),
                    Palette.Amber,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }

            using (Font body = DrawUtil.UiFont(10.5f, FontStyle.Bold))
            {
                TextRenderer.DrawText(
                    g,
                    AppInfo.CopyrightNotice,
                    body,
                    new Rectangle(textX, UiScale.S(23), textWidth, UiScale.S(25)),
                    Palette.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPadding |
                    TextFormatFlags.EndEllipsis);
            }
        }
    }

    internal class RoundedPathField : Panel
    {
        private readonly TextBox _input;
        private readonly FieldButton _browse;
        private bool _focused;

        public RoundedPathField()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);
            BackColor = Palette.Field;
            Height = UiScale.S(40);

            _input = new TextBox();
            _input.BorderStyle = BorderStyle.None;
            _input.BackColor = Palette.Field;
            _input.ForeColor = Palette.Text;
            _input.Font = DrawUtil.UiFont(9f, FontStyle.Regular);
            _input.ShortcutsEnabled = true;
            _input.Enter += delegate { _focused = true; Invalidate(); };
            _input.Leave += delegate { _focused = false; Invalidate(); };
            Controls.Add(_input);

            _browse = new FieldButton();
            _browse.Text = "浏览…";
            _browse.Click += delegate
            {
                using (var dialog = new FolderBrowserDialog())
                {
                    dialog.Description = "选择 " + AppInfo.Name + " 的安装位置";
                    dialog.ShowNewFolderButton = true;
                    string current = _input.Text;
                    if (Directory.Exists(current))
                    {
                        dialog.SelectedPath = current;
                    }
                    if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
                    {
                        _input.Text = dialog.SelectedPath;
                    }
                }
            };
            Controls.Add(_browse);

            Resize += delegate { LayoutInner(); };
            LayoutInner();
        }

        public TextBox Input
        {
            get { return _input; }
        }

        public string Value
        {
            get { return _input.Text.Trim(); }
            set { _input.Text = value; }
        }

        private void LayoutInner()
        {
            int pad = UiScale.S(7);
            int browseWidth = UiScale.S(68);
            int browseHeight = UiScale.S(26);
            _browse.Bounds = new Rectangle(
                Math.Max(0, Width - browseWidth - pad),
                Math.Max(0, (Height - browseHeight) / 2),
                browseWidth,
                browseHeight);

            int inputHeight = UiScale.S(22);
            _input.Bounds = new Rectangle(
                UiScale.S(12),
                Math.Max(0, (Height - inputHeight) / 2),
                Math.Max(10, Width - browseWidth - UiScale.S(30)),
                inputHeight);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // Painted in OnPaint so the rounded corners stay transparent.
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Palette.Window);
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = DrawUtil.RoundedRect(rect, UiScale.S(9)))
            {
                using (var fill = new SolidBrush(Palette.Field))
                {
                    g.FillPath(fill, path);
                }
                Color border = _focused ? Palette.AccentBlue : Palette.Line;
                using (var pen = new Pen(border))
                {
                    g.DrawPath(pen, path);
                }
            }
        }
    }

    internal class RuntimeCard : Control
    {
        private string _title = "";
        private string _detail = "";
        private bool _ready;

        public RuntimeCard()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);
            BackColor = Palette.Window;
            Height = UiScale.S(46);
            Font = DrawUtil.UiFont(8.5f, FontStyle.Regular);
        }

        public bool Ready
        {
            get { return _ready; }
            set { _ready = value; Invalidate(); }
        }

        public string TitleText
        {
            get { return _title; }
            set { _title = value; Invalidate(); }
        }

        public string DetailText
        {
            get { return _detail; }
            set { _detail = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Palette.Window);
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fill = _ready ? Color.FromArgb(16, 34, 31) : Color.FromArgb(40, 32, 20);
            Color border = _ready ? Color.FromArgb(35, 76, 62) : Color.FromArgb(67, 54, 31);
            using (GraphicsPath path = DrawUtil.RoundedRect(rect, UiScale.S(9)))
            {
                using (var brush = new SolidBrush(fill))
                {
                    g.FillPath(brush, path);
                }
                using (var pen = new Pen(border))
                {
                    g.DrawPath(pen, path);
                }
            }

            Color dot = _ready ? Palette.Green : Palette.Amber;
            using (var glow = new SolidBrush(Color.FromArgb(45, dot)))
            {
                g.FillEllipse(glow, UiScale.S(12), UiScale.S(16), UiScale.S(14), UiScale.S(14));
            }
            using (var dotBrush = new SolidBrush(dot))
            {
                g.FillEllipse(dotBrush, UiScale.S(17), UiScale.S(21), UiScale.S(5), UiScale.S(5));
            }

            using (Font titleFont = DrawUtil.UiFont(8.75f, FontStyle.Bold))
            {
                TextRenderer.DrawText(
                    g,
                    _title,
                    titleFont,
                    new Rectangle(UiScale.S(32), UiScale.S(8), Width - UiScale.S(44), UiScale.S(17)),
                    _ready ? Palette.Text : Palette.Amber,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            }
            TextRenderer.DrawText(
                g,
                _detail,
                Font,
                new Rectangle(UiScale.S(32), UiScale.S(24), Width - UiScale.S(44), UiScale.S(16)),
                Palette.TextMuted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }
    }

    internal class ProgressBarEx : Control
    {
        private float _value;

        public ProgressBarEx()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);
            BackColor = Palette.Window;
            Height = UiScale.S(5);
        }

        public float Value
        {
            get { return _value; }
            set
            {
                _value = Math.Max(0f, Math.Min(100f, value));
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Palette.Window);
            var track = new Rectangle(0, 0, Width, Height);
            using (GraphicsPath path = DrawUtil.RoundedRect(track, Height / 2f))
            using (var brush = new SolidBrush(Palette.CardRaised))
            {
                g.FillPath(brush, path);
            }

            int fillWidth = (int)Math.Round(Width * (_value / 100f));
            if (fillWidth <= 0)
            {
                return;
            }
            var fill = new Rectangle(0, 0, Math.Min(Width, fillWidth), Height);
            using (GraphicsPath path = DrawUtil.RoundedRect(fill, Height / 2f))
            using (var brush = DrawUtil.AccentBrush(fill))
            {
                g.FillPath(brush, path);
            }
        }
    }

    internal class SuccessBadge : Control
    {
        public SuccessBadge()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);
            BackColor = Palette.Window;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Palette.Window);
            int size = Math.Min(Width, Height);
            var rect = new Rectangle((Width - size) / 2, (Height - size) / 2, size, size);
            using (var glow = new SolidBrush(Color.FromArgb(28, Palette.Green)))
            {
                g.FillEllipse(glow, Inflate(rect, UiScale.S(9)));
            }
            using (var innerGlow = new SolidBrush(Color.FromArgb(48, Palette.Green)))
            {
                g.FillEllipse(innerGlow, Inflate(rect, UiScale.S(4)));
            }
            using (var fill = new SolidBrush(Color.FromArgb(31, 78, 63)))
            {
                g.FillEllipse(fill, rect);
            }
            using (var border = new Pen(Color.FromArgb(61, 139, 108)))
            {
                g.DrawEllipse(border, rect);
            }

            using (var pen = new Pen(Palette.Green, Math.Max(2f, 2.0f * UiScale.Factor)))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawLine(pen,
                    rect.Left + size * 0.29f, rect.Top + size * 0.52f,
                    rect.Left + size * 0.45f, rect.Top + size * 0.67f);
                g.DrawLine(pen,
                    rect.Left + size * 0.45f, rect.Top + size * 0.67f,
                    rect.Left + size * 0.72f, rect.Top + size * 0.35f);
            }
        }

        private static Rectangle Inflate(Rectangle rect, int amount)
        {
            return new Rectangle(rect.X - amount, rect.Y - amount, rect.Width + amount * 2, rect.Height + amount * 2);
        }
    }

    internal class LogoView : Control
    {
        private readonly Image _image;

        public LogoView(Image image)
        {
            _image = image;
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer,
                true);
            BackColor = Palette.Window;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Palette.Window);
            if (_image == null)
            {
                return;
            }
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(_image, new Rectangle(0, 0, Width, Height));
        }
    }
}
