using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ExplorerTreemap
{
    internal sealed class CompanionContextV5 : ApplicationContext, IDisposable
    {
        private readonly XRayFormV5 panel = new XRayFormV5();
        private readonly NotifyIcon tray;
        private readonly System.Windows.Forms.Timer timer;
        private readonly ToolStripMenuItem followItem;
        private readonly Dictionary<string, CacheEntry> cache = new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);
        private CancellationTokenSource scanCts;
        private ExplorerInfo currentExplorer;
        private string lastKey = string.Empty;
        private bool disposed;

        public CompanionContextV5()
        {
            panel.RefreshRequested += delegate { ForceRefresh(); };
            panel.HeightPreferenceChanged += delegate { if (currentExplorer != null) PositionPanel(currentExplorer.Hwnd); };

            var menu = new ContextMenuStrip();
            followItem = new ToolStripMenuItem("跟随资源管理器") { Checked = true, CheckOnClick = true };
            followItem.CheckedChanged += delegate
            {
                if (!followItem.Checked) panel.Hide();
                lastKey = string.Empty;
            };

            var expand = new ToolStripMenuItem("展开 X 光层");
            expand.Click += delegate
            {
                panel.SetCollapsed(false, true);
                if (currentExplorer != null) PositionPanel(currentExplorer.Hwnd);
            };

            var collapse = new ToolStripMenuItem("收起为细线");
            collapse.Click += delegate
            {
                panel.SetPinned(false);
                panel.SetCollapsed(true, true);
                if (currentExplorer != null) PositionPanel(currentExplorer.Hwnd);
            };

            var refresh = new ToolStripMenuItem("强制刷新当前目录");
            refresh.Click += delegate { ForceRefresh(); };
            var exit = new ToolStripMenuItem("退出");
            exit.Click += delegate { ExitThread(); };

            menu.Items.Add(followItem);
            menu.Items.Add(expand);
            menu.Items.Add(collapse);
            menu.Items.Add(refresh);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exit);

            tray = new NotifyIcon
            {
                Visible = true,
                Icon = SystemIcons.Application,
                Text = "磁盘 X 光层 V0.5",
                ContextMenuStrip = menu
            };
            tray.DoubleClick += delegate { followItem.Checked = !followItem.Checked; };

            timer = new System.Windows.Forms.Timer { Interval = 500 };
            timer.Tick += async delegate { await TickAsync(); };
            timer.Start();

            tray.ShowBalloonTip(1800, "磁盘 X 光层 V0.5", "方块保持 40% 视觉面积，但改为紧凑铺排。", ToolTipIcon.Info);
        }

        private async Task TickAsync()
        {
            if (!followItem.Checked) { panel.Hide(); return; }

            ExplorerInfo info = ExplorerProbe.TryGetForegroundExplorer();
            if (info == null) { panel.Hide(); return; }

            currentExplorer = info;
            PositionPanel(info.Hwnd);
            if (!panel.Visible) panel.Show();

            string key = info.IsThisPc ? "<THIS_PC>" : info.Path;
            if (string.IsNullOrWhiteSpace(key)) { panel.Hide(); return; }

            panel.SetPath(info.DisplayName, info.Path, info.IsThisPc);
            if (!string.Equals(lastKey, key, StringComparison.OrdinalIgnoreCase))
            {
                lastKey = key;
                await LoadAsync(info, false);
            }
        }

        private void PositionPanel(IntPtr hwnd)
        {
            NativeMethods.RECT r;
            if (!NativeMethods.GetWindowRect(hwnd, out r)) return;

            uint dpi = DpiBootstrap.GetWindowDpi(hwnd);
            float scale = Math.Max(1f, dpi / 96f);
            panel.SetExternalDpiScale(scale);

            Rectangle work = Screen.FromHandle(hwnd).WorkingArea;
            int inset = Px(8, scale);
            int minWidth = Px(480, scale);
            int width = Math.Max(minWidth, r.Right - r.Left - inset * 2);
            int height = panel.GetDesiredHeightPx(scale);
            int x = Math.Max(work.Left, r.Left + inset);
            int bottom = Math.Min(work.Bottom, r.Bottom - inset);
            int y = Math.Max(work.Top, bottom - height);

            if (x + width > work.Right) width = work.Right - x;
            int minHeight = panel.Collapsed ? panel.GetThinHeightPx(scale) : Px(120, scale);
            height = Math.Max(minHeight, bottom - y);

            panel.Bounds = new Rectangle(x, y, Math.Max(minWidth, width), height);
        }

        private static int Px(int logical, float scale)
        {
            return Math.Max(1, (int)Math.Round(logical * scale));
        }

        private async Task LoadAsync(ExplorerInfo info, bool force)
        {
            string key = info.IsThisPc ? "<THIS_PC>" : info.Path;
            CacheEntry hit;
            if (!force && cache.TryGetValue(key, out hit) && DateTime.UtcNow - hit.Created < TimeSpan.FromMinutes(10))
            {
                panel.SetResult(hit.Result);
                return;
            }

            if (scanCts != null) { scanCts.Cancel(); scanCts.Dispose(); }
            scanCts = new CancellationTokenSource();
            var token = scanCts.Token;
            panel.SetScanning();
            var progress = new Progress<ScanProgress>(p => panel.SetProgress(p));

            try
            {
                ScanResult result = await Task.Run(() =>
                    info.IsThisPc ? Scanner.ScanThisPc(token) : Scanner.ScanFolder(info.Path, token, progress), token);

                if (token.IsCancellationRequested) return;
                string now = currentExplorer == null ? string.Empty :
                    (currentExplorer.IsThisPc ? "<THIS_PC>" : currentExplorer.Path);
                if (!string.Equals(now, key, StringComparison.OrdinalIgnoreCase)) return;

                cache[key] = new CacheEntry { Created = DateTime.UtcNow, Result = result };
                panel.SetResult(result);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (!token.IsCancellationRequested) panel.SetError(ex.Message); }
        }

        private async void ForceRefresh()
        {
            if (currentExplorer == null) return;
            string key = currentExplorer.IsThisPc ? "<THIS_PC>" : currentExplorer.Path;
            cache.Remove(key);
            await LoadAsync(currentExplorer, true);
        }

        protected override void ExitThreadCore()
        {
            Dispose();
            base.ExitThreadCore();
        }

        public new void Dispose()
        {
            if (disposed) return;
            disposed = true;
            timer.Dispose();
            if (scanCts != null) { scanCts.Cancel(); scanCts.Dispose(); }
            tray.Visible = false;
            tray.Dispose();
            panel.Dispose();
        }

        private sealed class CacheEntry
        {
            public DateTime Created;
            public ScanResult Result;
        }
    }

    internal sealed class XRayFormV5 : Form
    {
        private const int ThinHeightLogical = 18;

        private readonly XRayStripV5 strip = new XRayStripV5();
        private readonly Panel expandedRoot = new Panel();
        private readonly Label pathLabel = new Label();
        private readonly Label summaryLabel = new Label();
        private readonly Label statusLabel = new Label();
        private readonly TileMapV5 map = new TileMapV5();
        private readonly Label pinLabel;
        private readonly Panel resizeGrip = new Panel();
        private readonly System.Windows.Forms.Timer hoverTimer = new System.Windows.Forms.Timer();

        private int expandedHeightLogical = 236;
        private bool collapsed = true;
        private bool pinned;
        private int outsideTicks;
        private int insideTicks;
        private bool resizing;
        private int resizeStartY;
        private int resizeStartHeightLogical;
        private float externalDpiScale = 1f;

        public event EventHandler RefreshRequested;
        public event EventHandler HeightPreferenceChanged;
        public bool Collapsed { get { return collapsed; } }

        private static readonly Color Panel = Color.FromArgb(238, 19, 22, 28);
        private static readonly Color Panel2 = Color.FromArgb(30, 34, 42);
        private static readonly Color TextMain = Color.FromArgb(240, 244, 248);
        private static readonly Color TextMuted = Color.FromArgb(153, 161, 173);
        private static readonly Color Accent = Color.FromArgb(117, 232, 255);
        private static readonly Color Border = Color.FromArgb(58, 68, 82);

        public XRayFormV5()
        {
            Text = "磁盘 X 光层";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.FromArgb(11, 13, 17);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            DoubleBuffered = true;
            Opacity = 0.99;

            strip.Dock = DockStyle.Fill;
            strip.Cursor = Cursors.Hand;
            strip.Click += delegate { SetCollapsed(false, true); };
            Controls.Add(strip);

            expandedRoot.Dock = DockStyle.Fill;
            expandedRoot.BackColor = Panel;
            expandedRoot.Visible = false;
            Controls.Add(expandedRoot);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(10, 14, 10, 8),
                BackColor = Panel,
                Margin = new Padding(0)
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 31));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
            expandedRoot.Controls.Add(root);

            resizeGrip.Dock = DockStyle.Top;
            resizeGrip.Height = 7;
            resizeGrip.Cursor = Cursors.SizeNS;
            resizeGrip.BackColor = Color.FromArgb(25, 29, 36);
            resizeGrip.Paint += delegate(object sender, PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                int y = resizeGrip.Height / 2;
                int half = S(30);
                using (var p = new Pen(Color.FromArgb(112, 124, 142), Math.Max(1f, SFloat(1f))))
                    e.Graphics.DrawLine(p, resizeGrip.Width / 2 - half, y, resizeGrip.Width / 2 + half, y);
            };
            resizeGrip.MouseDown += GripDown;
            resizeGrip.MouseMove += GripMove;
            resizeGrip.MouseUp += GripUp;
            expandedRoot.Controls.Add(resizeGrip);
            resizeGrip.BringToFront();

            var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, Margin = new Padding(0) };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38));

            pathLabel.Dock = DockStyle.Fill;
            pathLabel.AutoEllipsis = true;
            pathLabel.TextAlign = ContentAlignment.MiddleLeft;
            pathLabel.ForeColor = TextMain;
            pathLabel.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold, GraphicsUnit.Point);
            header.Controls.Add(pathLabel, 0, 0);

            summaryLabel.AutoSize = true;
            summaryLabel.ForeColor = Accent;
            summaryLabel.Font = new Font("Consolas", 9F, FontStyle.Bold, GraphicsUnit.Point);
            summaryLabel.Margin = new Padding(8, 7, 8, 0);
            summaryLabel.Text = "等待扫描";
            header.Controls.Add(summaryLabel, 1, 0);

            var refresh = HeaderButton("↻");
            refresh.Click += delegate { var h = RefreshRequested; if (h != null) h(this, EventArgs.Empty); };
            header.Controls.Add(refresh, 2, 0);

            pinLabel = HeaderButton("○");
            pinLabel.Click += delegate { SetPinned(!pinned); if (pinned) SetCollapsed(false, true); };
            header.Controls.Add(pinLabel, 3, 0);

            var collapse = HeaderButton("⌄");
            collapse.Click += delegate { SetPinned(false); SetCollapsed(true, true); };
            header.Controls.Add(collapse, 4, 0);
            root.Controls.Add(header, 0, 0);

            map.Dock = DockStyle.Fill;
            map.Margin = new Padding(0, 4, 0, 3);
            map.ItemActivated += delegate(object sender, DiskItem item)
            {
                if (item == null) return;
                try
                {
                    if (item.IsFolder) Process.Start("explorer.exe", "\"" + item.Path + "\"");
                    else Process.Start("explorer.exe", "/select,\"" + item.Path + "\"");
                }
                catch { }
            };
            map.HoverChanged += delegate(object sender, DiskItem item)
            {
                statusLabel.Text = item == null
                    ? "单击文件夹打开 · 单击文件定位 · 拖动顶部细柄调整高度"
                    : item.Name + "  ·  " + FormatBytes(item.Size) + "  ·  " + item.Path;
            };
            root.Controls.Add(map, 0, 1);

            statusLabel.Dock = DockStyle.Fill;
            statusLabel.AutoEllipsis = true;
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            statusLabel.ForeColor = TextMuted;
            statusLabel.Font = new Font("Segoe UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
            statusLabel.Text = "正在等待资源管理器…";
            root.Controls.Add(statusLabel, 0, 2);

            var tips = new ToolTip();
            tips.SetToolTip(refresh, "重新扫描当前目录");
            tips.SetToolTip(pinLabel, "固定展开 / 恢复自动收起");
            tips.SetToolTip(collapse, "收起为底边细线");
            tips.SetToolTip(resizeGrip, "拖动调整 X 光层高度");

            hoverTimer.Interval = 90;
            hoverTimer.Tick += delegate { HoverTick(); };
            hoverTimer.Start();

            SizeChanged += delegate { UpdateRegion(); };
            DpiChanged += delegate
            {
                externalDpiScale = Math.Max(1f, DeviceDpi / 96f);
                map.SetDpiScale(externalDpiScale);
                strip.SetDpiScale(externalDpiScale);
                UpdateRegion();
            };

            ApplyVisual();
            Paint += delegate(object sender, PaintEventArgs e)
            {
                if (Width <= 1 || Height <= 1) return;
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var p = new Pen(Border, Math.Max(1f, SFloat(1f))))
                    e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            };
        }

        public void SetExternalDpiScale(float scale)
        {
            scale = Math.Max(1f, Math.Min(4f, scale));
            if (Math.Abs(scale - externalDpiScale) < 0.01f) return;
            externalDpiScale = scale;
            map.SetDpiScale(scale);
            strip.SetDpiScale(scale);
            UpdateRegion();
        }

        public int GetDesiredHeightPx(float scale)
        {
            int logical = collapsed ? ThinHeightLogical : expandedHeightLogical;
            return Math.Max(1, (int)Math.Round(logical * scale));
        }

        public int GetThinHeightPx(float scale)
        {
            return Math.Max(1, (int)Math.Round(ThinHeightLogical * scale));
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x08000000;
                cp.ExStyle |= 0x00000080;
                return cp;
            }
        }

        private Label HeaderButton(string text)
        {
            return new Label
            {
                Dock = DockStyle.Fill,
                Text = text,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Accent,
                BackColor = Panel2,
                Cursor = Cursors.Hand,
                Margin = new Padding(2, 0, 0, 0),
                Font = new Font("Segoe UI", 10F, FontStyle.Bold, GraphicsUnit.Point)
            };
        }

        private void HoverTick()
        {
            if (!Visible || resizing) return;
            bool inside = Bounds.Contains(Cursor.Position);

            if (collapsed)
            {
                outsideTicks = 0;
                if (inside && ++insideTicks >= 2)
                {
                    insideTicks = 0;
                    SetCollapsed(false, false);
                }
                else if (!inside) insideTicks = 0;
                return;
            }

            insideTicks = 0;
            if (pinned) { outsideTicks = 0; return; }
            if (inside) outsideTicks = 0;
            else if (++outsideTicks >= 9)
            {
                outsideTicks = 0;
                SetCollapsed(true, false);
            }
        }

        public void SetCollapsed(bool value, bool userInitiated)
        {
            if (value && pinned && !userInitiated) return;
            if (collapsed == value) return;
            collapsed = value;
            insideTicks = outsideTicks = 0;
            ApplyVisual();
            var h = HeightPreferenceChanged;
            if (h != null) h(this, EventArgs.Empty);
        }

        public void SetPinned(bool value)
        {
            pinned = value;
            pinLabel.Text = pinned ? "●" : "○";
            pinLabel.ForeColor = pinned ? Color.FromArgb(255, 214, 105) : Accent;
            strip.Pinned = pinned;
            strip.Invalidate();
        }

        private void ApplyVisual()
        {
            strip.Visible = collapsed;
            expandedRoot.Visible = !collapsed;
            if (collapsed) strip.BringToFront(); else expandedRoot.BringToFront();
            UpdateRegion();
        }

        private void GripDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            resizing = true;
            resizeStartY = Cursor.Position.Y;
            resizeStartHeightLogical = expandedHeightLogical;
            resizeGrip.Capture = true;
        }

        private void GripMove(object sender, MouseEventArgs e)
        {
            if (!resizing) return;
            int deltaPhysical = resizeStartY - Cursor.Position.Y;
            int deltaLogical = (int)Math.Round(deltaPhysical / Math.Max(1f, externalDpiScale));
            int next = Math.Max(140, Math.Min(430, resizeStartHeightLogical + deltaLogical));
            if (next == expandedHeightLogical) return;
            expandedHeightLogical = next;
            var h = HeightPreferenceChanged;
            if (h != null) h(this, EventArgs.Empty);
        }

        private void GripUp(object sender, MouseEventArgs e)
        {
            resizing = false;
            resizeGrip.Capture = false;
        }

        private void UpdateRegion()
        {
            if (Width < 4 || Height < 4) return;
            float radius = SFloat(collapsed ? 8f : 12f);
            using (var path = RoundedRect(new RectangleF(0, 0, Width, Height), radius))
            {
                Region old = Region;
                Region = new Region(path);
                if (old != null) old.Dispose();
            }
        }

        public void SetPath(string name, string path, bool thisPc)
        {
            string shown = thisPc ? "此电脑 · 磁盘占用" : (string.IsNullOrWhiteSpace(path) ? name : path);
            pathLabel.Text = shown;
            strip.PathText = shown;
            strip.Invalidate();
        }

        public void SetScanning()
        {
            summaryLabel.Text = "扫描中…";
            statusLabel.Text = "正在计算这一层的文件 / 文件夹占用…";
            strip.SummaryText = "扫描中";
            strip.IsBusy = true;
            strip.Invalidate();
            map.SetItems(new List<DiskItem>());
        }

        public void SetProgress(ScanProgress p)
        {
            statusLabel.Text = string.Format("扫描中 · 已检查 {0:N0} 项 · 跳过 {1:N0} 项 · {2}", p.Visited, p.Skipped, p.CurrentName);
            strip.SummaryText = string.Format("扫描 {0:N0} 项", p.Visited);
            strip.Invalidate();
        }

        public void SetResult(ScanResult r)
        {
            map.SetItems(r.Items);
            summaryLabel.Text = string.Format("{0} · {1:N0} 项", FormatBytes(r.TotalBytes), r.Items.Count);
            statusLabel.Text = string.Format("{0:N0} 项 · {1} · 40% 视觉面积 · 紧凑铺排", r.Items.Count, FormatBytes(r.TotalBytes));
            if (r.Skipped > 0) statusLabel.Text += string.Format(" · 跳过 {0:N0} 个受限项", r.Skipped);
            strip.SummaryText = string.Format("{0} · {1:N0} 项", FormatBytes(r.TotalBytes), r.Items.Count);
            strip.IsBusy = false;
            strip.Invalidate();
        }

        public void SetError(string error)
        {
            map.SetItems(new List<DiskItem>());
            summaryLabel.Text = "无法读取";
            statusLabel.Text = "无法读取：" + error;
            strip.SummaryText = "无法读取";
            strip.IsBusy = false;
            strip.Invalidate();
        }

        private int S(int logical) { return Math.Max(1, (int)Math.Round(logical * externalDpiScale)); }
        private float SFloat(float logical) { return logical * externalDpiScale; }

        private static string FormatBytes(long bytes)
        {
            string[] u = { "B", "KB", "MB", "GB", "TB" };
            double v = Math.Max(0, bytes);
            int i = 0;
            while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
            return (i == 0 ? v.ToString("0") : v.ToString(v >= 100 ? "0" : v >= 10 ? "0.0" : "0.00")) + " " + u[i];
        }

        private static GraphicsPath RoundedRect(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = radius * 2;
            if (d <= 1) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    internal sealed class XRayStripV5 : Control
    {
        public string PathText = "等待资源管理器…";
        public string SummaryText = "";
        public bool Pinned;
        public bool IsBusy;
        private float dpiScale = 1f;
        private static readonly Color Accent = Color.FromArgb(117, 232, 255);

        public XRayStripV5()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            BackColor = Color.FromArgb(12, 15, 20);
        }

        public void SetDpiScale(float scale)
        {
            dpiScale = Math.Max(1f, Math.Min(4f, scale));
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            using (var bg = new SolidBrush(Color.FromArgb(235, 12, 15, 20)))
                e.Graphics.FillRectangle(bg, ClientRectangle);
            using (var a = new SolidBrush(Accent))
                e.Graphics.FillRectangle(a, 0, 0, Width, Math.Max(2, S(2)));

            string lead = Pinned ? "●  X-RAY" : "▴  X-RAY";
            using (var lf = new Font("Segoe UI", 7.5F, FontStyle.Bold, GraphicsUnit.Point))
            using (var tf = new Font("Segoe UI", 7.5F, FontStyle.Regular, GraphicsUnit.Point))
            using (var lb = new SolidBrush(Pinned ? Color.FromArgb(255, 214, 105) : Accent))
            using (var tb = new SolidBrush(Color.FromArgb(214, 221, 230)))
            using (var mb = new SolidBrush(Color.FromArgb(132, 143, 158)))
            {
                float y = SFloat(2.3f);
                float left = SFloat(7f);
                e.Graphics.DrawString(lead, lf, lb, new PointF(left, y));
                float x = left + e.Graphics.MeasureString(lead, lf).Width + SFloat(5f);
                float rightWidth = SFloat(172f);
                float rightX = Math.Max(x + SFloat(30f), Width - rightWidth - SFloat(7f));

                using (var rightFmt = new StringFormat { Alignment = StringAlignment.Far, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                using (var leftFmt = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                {
                    e.Graphics.DrawString(IsBusy ? "扫描中…" : (SummaryText ?? ""), tf, mb,
                        new RectangleF(rightX, y, rightWidth, SFloat(14f)), rightFmt);
                    e.Graphics.DrawString(PathText ?? "", tf, tb,
                        new RectangleF(x, y, Math.Max(SFloat(20f), rightX - x - SFloat(8f)), SFloat(14f)), leftFmt);
                }
            }
        }

        private int S(int v) { return Math.Max(1, (int)Math.Round(v * dpiScale)); }
        private float SFloat(float v) { return v * dpiScale; }
    }

    internal sealed class TileMapV5 : Control
    {
        private const float VisualAreaRatio = 0.40f;
        private const float CompactLinearScale = 0.63245553f;

        private readonly List<DiskItem> items = new List<DiskItem>();
        private readonly List<Tile> tiles = new List<Tile>();
        private DiskItem hover;
        private long totalSize;
        private float dpiScale = 1f;

        public event EventHandler<DiskItem> ItemActivated;
        public event EventHandler<DiskItem> HoverChanged;

        public TileMapV5()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(14, 17, 22);
            Cursor = Cursors.Hand;
            Resize += delegate { BuildLayout(); Invalidate(); };
        }

        public void SetDpiScale(float scale)
        {
            dpiScale = Math.Max(1f, Math.Min(4f, scale));
            BuildLayout();
            Invalidate();
        }

        public void SetItems(IEnumerable<DiskItem> source)
        {
            items.Clear();
            if (source != null)
                items.AddRange(source.Where(x => x.Size > 0).OrderByDescending(x => x.Size).Take(180));
            totalSize = items.Sum(x => x.Size);
            BuildLayout();
            Invalidate();
        }

        private void BuildLayout()
        {
            tiles.Clear();
            if (items.Count == 0 || Width < 10 || Height < 10) return;

            float margin = SFloat(1.5f);
            float availableW = Math.Max(SFloat(8f), Width - margin * 2f);
            float availableH = Math.Max(SFloat(8f), Height - margin * 2f);

            float compactW = availableW * CompactLinearScale;
            float compactH = availableH * CompactLinearScale;

            // V0.4 是把每个 leaf 单独缩小，导致大量“空洞”。
            // V0.5 改为先缩整个画布，再在缩小后的画布中完整铺满。
            // 因此总视觉面积仍约为 40%，但 tile 之间只剩极细分隔线。
            RectangleF compactCanvas = new RectangleF(margin, margin, compactW, compactH);
            LayoutGroup(items, compactCanvas, 0);
        }

        private void LayoutGroup(IList<DiskItem> group, RectangleF rect, int depth)
        {
            if (group == null || group.Count == 0 || rect.Width < 1f || rect.Height < 1f) return;

            if (group.Count == 1)
            {
                tiles.Add(new Tile { Item = group[0], Bounds = TightInset(rect) });
                return;
            }

            long sum = group.Sum(x => x.Size);
            if (sum <= 0) return;

            long target = sum / 2;
            long leftSum = 0;
            int split = 0;
            for (int i = 0; i < group.Count - 1; i++)
            {
                long next = leftSum + group[i].Size;
                if (i > 0 && Math.Abs(target - leftSum) < Math.Abs(target - next)) break;
                leftSum = next;
                split = i + 1;
                if (leftSum >= target) break;
            }

            if (split <= 0) split = 1;
            if (split >= group.Count) split = group.Count - 1;

            var a = group.Take(split).ToList();
            var b = group.Skip(split).ToList();
            float ratio = Math.Max(0.06F, Math.Min(0.94F, (float)a.Sum(x => x.Size) / sum));
            bool vertical = depth == 0 || rect.Width >= rect.Height;

            if (vertical)
            {
                float w = rect.Width * ratio;
                LayoutGroup(a, new RectangleF(rect.X, rect.Y, w, rect.Height), depth + 1);
                LayoutGroup(b, new RectangleF(rect.X + w, rect.Y, rect.Width - w, rect.Height), depth + 1);
            }
            else
            {
                float h = rect.Height * ratio;
                LayoutGroup(a, new RectangleF(rect.X, rect.Y, rect.Width, h), depth + 1);
                LayoutGroup(b, new RectangleF(rect.X, rect.Y + h, rect.Width, rect.Height - h), depth + 1);
            }
        }

        private RectangleF TightInset(RectangleF r)
        {
            float gap = Math.Min(SFloat(0.9f), Math.Max(0.15f, Math.Min(r.Width, r.Height) * 0.08f));
            float half = gap / 2f;
            float w = Math.Max(0, r.Width - gap);
            float h = Math.Max(0, r.Height - gap);
            return new RectangleF(r.X + half, r.Y + half, w, h);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            e.Graphics.CompositingQuality = CompositingQuality.HighQuality;
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            if (tiles.Count == 0)
            {
                using (var f = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point))
                using (var b = new SolidBrush(Color.FromArgb(126, 136, 150)))
                    e.Graphics.DrawString("等待扫描…", f, b, new PointF(SFloat(12f), SFloat(12f)));
                return;
            }

            foreach (var tile in tiles)
            {
                RectangleF r = tile.Bounds;
                if (r.Width < 0.8f || r.Height < 0.8f) continue;

                Color baseColor = ColorForItem(tile.Item);
                bool isHover = object.ReferenceEquals(tile.Item, hover);
                Color top = isHover ? ShiftBrightness(baseColor, 0.14f) : ShiftBrightness(baseColor, 0.06f);
                Color bottom = isHover ? ShiftBrightness(baseColor, -0.04f) : ShiftBrightness(baseColor, -0.14f);
                float radius = Math.Min(SFloat(6f), Math.Min(r.Width, r.Height) / 6f);

                using (var path = RoundedRect(r, radius))
                using (var brush = new LinearGradientBrush(r, top, bottom, LinearGradientMode.Vertical))
                using (var pen = new Pen(isHover ? Color.FromArgb(235, 255, 255, 255) : Color.FromArgb(90, 255, 255, 255), Math.Max(1f, SFloat(isHover ? 1.4f : 0.7f))))
                {
                    e.Graphics.FillPath(brush, path);
                    e.Graphics.DrawPath(pen, path);
                }

                DrawLabel(e.Graphics, tile, r);
            }
        }

        private void DrawLabel(Graphics g, Tile tile, RectangleF r)
        {
            if (r.Width < SFloat(62f) || r.Height < SFloat(30f)) return;

            double pct = totalSize <= 0 ? 0 : tile.Item.Size * 100.0 / totalSize;
            string text = tile.Item.Name + "\r\n" + FormatBytes(tile.Item.Size) + "  ·  " + pct.ToString(pct >= 10 ? "0" : "0.0") + "%";
            Rectangle textRect = Rectangle.Round(new RectangleF(
                r.X + SFloat(5f), r.Y + SFloat(4f),
                Math.Max(SFloat(8f), r.Width - SFloat(10f)),
                Math.Max(SFloat(8f), r.Height - SFloat(8f))));

            float size = r.Width > SFloat(160f) ? 8.5F : 7.5F;
            using (var f = new Font("Segoe UI", size, FontStyle.Bold, GraphicsUnit.Point))
            {
                TextRenderer.DrawText(g, text, f, textRect, Color.White,
                    TextFormatFlags.EndEllipsis | TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPadding);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            DiskItem hit = Hit(e.Location);
            if (!object.ReferenceEquals(hit, hover))
            {
                hover = hit;
                Invalidate();
                var h = HoverChanged;
                if (h != null) h(this, hit);
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hover != null)
            {
                hover = null;
                Invalidate();
                var h = HoverChanged;
                if (h != null) h(this, null);
            }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left) return;
            DiskItem hit = Hit(e.Location);
            if (hit != null)
            {
                var h = ItemActivated;
                if (h != null) h(this, hit);
            }
        }

        private DiskItem Hit(Point p)
        {
            for (int i = 0; i < tiles.Count; i++)
                if (tiles[i].Bounds.Contains(p)) return tiles[i].Item;
            return null;
        }

        private Color ColorForItem(DiskItem item)
        {
            string key = (item.Path ?? item.Name ?? string.Empty).ToLowerInvariant();
            uint hash = Fnv1a(key);

            if (item.IsFolder)
            {
                float hue = 202f + (hash % 38u);
                float sat = 0.44f + ((hash >> 8) % 18u) / 100f;
                float val = 0.58f + ((hash >> 16) % 15u) / 100f;
                return Hsv(hue, sat, val);
            }
            else
            {
                float hue = hash % 360u;
                float sat = 0.50f + ((hash >> 8) % 24u) / 100f;
                float val = 0.64f + ((hash >> 16) % 18u) / 100f;
                return Hsv(hue, sat, val);
            }
        }

        private static uint Fnv1a(string s)
        {
            unchecked
            {
                uint hash = 2166136261u;
                for (int i = 0; i < s.Length; i++)
                {
                    hash ^= s[i];
                    hash *= 16777619u;
                }
                return hash;
            }
        }

        private static Color Hsv(float h, float s, float v)
        {
            h = (h % 360f + 360f) % 360f;
            s = Math.Max(0f, Math.Min(1f, s));
            v = Math.Max(0f, Math.Min(1f, v));
            float c = v * s;
            float x = c * (1f - Math.Abs((h / 60f) % 2f - 1f));
            float m = v - c;
            float r1 = 0, g1 = 0, b1 = 0;

            if (h < 60) { r1 = c; g1 = x; }
            else if (h < 120) { r1 = x; g1 = c; }
            else if (h < 180) { g1 = c; b1 = x; }
            else if (h < 240) { g1 = x; b1 = c; }
            else if (h < 300) { r1 = x; b1 = c; }
            else { r1 = c; b1 = x; }

            return Color.FromArgb(
                Clamp255((r1 + m) * 255f),
                Clamp255((g1 + m) * 255f),
                Clamp255((b1 + m) * 255f));
        }

        private static int Clamp255(float v)
        {
            return Math.Max(0, Math.Min(255, (int)Math.Round(v)));
        }

        private static Color ShiftBrightness(Color c, float amount)
        {
            int r = Math.Max(0, Math.Min(255, c.R + (int)(255 * amount)));
            int g = Math.Max(0, Math.Min(255, c.G + (int)(255 * amount)));
            int b = Math.Max(0, Math.Min(255, c.B + (int)(255 * amount)));
            return Color.FromArgb(c.A, r, g, b);
        }

        private static GraphicsPath RoundedRect(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = radius * 2f;
            if (d <= 1f || r.Width <= d || r.Height <= d)
            {
                p.AddRectangle(r);
                return p;
            }

            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        private static string FormatBytes(long bytes)
        {
            double v = Math.Max(0, bytes);
            string[] u = { "B", "KB", "MB", "GB", "TB" };
            int i = 0;
            while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
            return v.ToString(v >= 100 ? "0" : v >= 10 ? "0.0" : "0.00") + " " + u[i];
        }

        private float SFloat(float v) { return v * dpiScale; }

        private sealed class Tile
        {
            public DiskItem Item;
            public RectangleF Bounds;
        }
    }
}
