using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ExplorerTreemap
{
    internal sealed class CompanionContext : ApplicationContext, IDisposable
    {
        private readonly FollowForm panel;
        private readonly NotifyIcon tray;
        private readonly Timer timer;
        private readonly ToolStripMenuItem followItem;
        private readonly Dictionary<string, CacheEntry> cache = new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);
        private CancellationTokenSource scanCts;
        private string lastKey = string.Empty;
        private ExplorerInfo lastExplorer;
        private bool disposed;

        public CompanionContext()
        {
            panel = new FollowForm();
            panel.RefreshRequested += delegate { ForceRefresh(); };

            var menu = new ContextMenuStrip();
            followItem = new ToolStripMenuItem("跟随资源管理器") { Checked = true, CheckOnClick = true };
            followItem.CheckedChanged += delegate
            {
                if (!followItem.Checked) panel.Hide();
                lastKey = string.Empty;
            };
            var refresh = new ToolStripMenuItem("强制刷新当前目录");
            refresh.Click += delegate { ForceRefresh(); };
            var exit = new ToolStripMenuItem("退出");
            exit.Click += delegate { ExitThread(); };
            menu.Items.Add(followItem);
            menu.Items.Add(refresh);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exit);

            tray = new NotifyIcon
            {
                Visible = true,
                Icon = SystemIcons.Application,
                Text = "磁盘空间地图 V0.2",
                ContextMenuStrip = menu
            };
            tray.DoubleClick += delegate
            {
                followItem.Checked = !followItem.Checked;
            };

            timer = new Timer { Interval = 650 };
            timer.Tick += async delegate { await TickAsync(); };
            timer.Start();

            tray.ShowBalloonTip(1800, "磁盘空间地图 V0.2", "已在后台运行。打开“此电脑”或任意文件夹，我会自动贴在资源管理器底部。", ToolTipIcon.Info);
        }

        private async Task TickAsync()
        {
            if (!followItem.Checked)
            {
                panel.Hide();
                return;
            }

            ExplorerInfo info = ExplorerProbe.TryGetForegroundExplorer();
            if (info == null)
            {
                panel.Hide();
                return;
            }

            lastExplorer = info;
            PositionPanel(info.Hwnd);
            if (!panel.Visible) panel.Show();

            string key = info.IsThisPc ? "<THIS_PC>" : info.Path;
            if (string.IsNullOrWhiteSpace(key))
            {
                panel.Hide();
                return;
            }

            panel.SetPath(info.DisplayName, info.Path, info.IsThisPc);

            if (!string.Equals(lastKey, key, StringComparison.OrdinalIgnoreCase))
            {
                lastKey = key;
                await LoadPathAsync(info, false);
            }
        }

        private void PositionPanel(IntPtr explorerHwnd)
        {
            NativeMethods.RECT r;
            if (!NativeMethods.GetWindowRect(explorerHwnd, out r)) return;

            int width = Math.Max(620, r.Right - r.Left - 16);
            int height = panel.Collapsed ? 42 : 190;
            Rectangle work = Screen.FromHandle(explorerHwnd).WorkingArea;

            int x = r.Left + 8;
            int y;
            if (r.Bottom + height + 6 <= work.Bottom)
                y = r.Bottom + 4;
            else
                y = r.Bottom - height - 8;

            if (x < work.Left) x = work.Left;
            if (x + width > work.Right) width = work.Right - x;
            if (y < work.Top) y = work.Top;

            panel.Bounds = new Rectangle(x, y, Math.Max(520, width), height);
        }

        private async Task LoadPathAsync(ExplorerInfo info, bool force)
        {
            string key = info.IsThisPc ? "<THIS_PC>" : info.Path;
            if (!force)
            {
                CacheEntry hit;
                if (cache.TryGetValue(key, out hit) && DateTime.UtcNow - hit.Created < TimeSpan.FromMinutes(8))
                {
                    panel.SetResult(hit.Result);
                    return;
                }
            }

            if (scanCts != null)
            {
                scanCts.Cancel();
                scanCts.Dispose();
            }
            scanCts = new CancellationTokenSource();
            var token = scanCts.Token;
            panel.SetScanning();

            var progress = new Progress<ScanProgress>(p => panel.SetProgress(p));
            try
            {
                ScanResult result = await Task.Run(() =>
                    info.IsThisPc ? Scanner.ScanThisPc(token) : Scanner.ScanFolder(info.Path, token, progress), token);

                if (token.IsCancellationRequested) return;
                string currentKey = lastExplorer == null ? string.Empty : (lastExplorer.IsThisPc ? "<THIS_PC>" : lastExplorer.Path);
                if (!string.Equals(currentKey, key, StringComparison.OrdinalIgnoreCase)) return;

                cache[key] = new CacheEntry { Created = DateTime.UtcNow, Result = result };
                panel.SetResult(result);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                    panel.SetError(ex.Message);
            }
        }

        private async void ForceRefresh()
        {
            if (lastExplorer == null) return;
            string key = lastExplorer.IsThisPc ? "<THIS_PC>" : lastExplorer.Path;
            cache.Remove(key);
            await LoadPathAsync(lastExplorer, true);
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
            if (timer != null) timer.Dispose();
            if (scanCts != null) { scanCts.Cancel(); scanCts.Dispose(); }
            if (tray != null) { tray.Visible = false; tray.Dispose(); }
            if (panel != null) panel.Dispose();
        }

        private sealed class CacheEntry
        {
            public DateTime Created;
            public ScanResult Result;
        }
    }

    internal sealed class FollowForm : Form
    {
        private readonly Label pathLabel = new Label();
        private readonly Label statusLabel = new Label();
        private readonly TileMapControl map = new TileMapControl();
        private readonly Label collapseLabel = new Label();
        public event EventHandler RefreshRequested;
        public bool Collapsed { get; private set; }

        private static readonly Color Bg = Color.FromArgb(235, 11, 13, 17);
        private static readonly Color Panel = Color.FromArgb(238, 21, 24, 30);
        private static readonly Color TextMain = Color.FromArgb(240, 244, 248);
        private static readonly Color TextMuted = Color.FromArgb(153, 161, 173);
        private static readonly Color Accent = Color.FromArgb(117, 232, 255);

        public FollowForm()
        {
            Text = "磁盘空间地图";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.FromArgb(11, 13, 17);
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;
            Opacity = 0.985;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(10, 7, 10, 8),
                BackColor = Panel
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
            Controls.Add(root);

            var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = new Padding(0) };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34));

            pathLabel.Dock = DockStyle.Fill;
            pathLabel.AutoEllipsis = true;
            pathLabel.TextAlign = ContentAlignment.MiddleLeft;
            pathLabel.ForeColor = TextMain;
            pathLabel.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
            header.Controls.Add(pathLabel, 0, 0);

            var refreshLabel = MakeHeaderButton("↻");
            refreshLabel.Click += delegate { var h = RefreshRequested; if (h != null) h(this, EventArgs.Empty); };
            header.Controls.Add(refreshLabel, 1, 0);

            collapseLabel = MakeHeaderButton("—");
            collapseLabel.Click += delegate
            {
                Collapsed = !Collapsed;
                map.Visible = !Collapsed;
                statusLabel.Visible = !Collapsed;
                collapseLabel.Text = Collapsed ? "▴" : "—";
            };
            header.Controls.Add(collapseLabel, 2, 0);
            root.Controls.Add(header, 0, 0);

            map.Dock = DockStyle.Fill;
            map.Margin = new Padding(0, 4, 0, 3);
            map.ItemActivated += delegate(object sender, DiskItem item)
            {
                if (item == null) return;
                try
                {
                    if (item.IsFolder)
                        Process.Start("explorer.exe", "\"" + item.Path + "\"");
                    else
                        Process.Start("explorer.exe", "/select,\"" + item.Path + "\"");
                }
                catch { }
            };
            map.HoverChanged += delegate(object sender, DiskItem item)
            {
                if (item == null) return;
                statusLabel.Text = item.Name + "  ·  " + FormatBytes(item.Size) + "  ·  " + item.Path;
            };
            root.Controls.Add(map, 0, 1);

            statusLabel.Dock = DockStyle.Fill;
            statusLabel.AutoEllipsis = true;
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            statusLabel.ForeColor = TextMuted;
            statusLabel.Font = new Font("Segoe UI", 8.5F);
            statusLabel.Text = "正在等待资源管理器…";
            root.Controls.Add(statusLabel, 0, 2);

            Paint += delegate(object sender, PaintEventArgs e)
            {
                using (var p = new Pen(Color.FromArgb(62, 72, 86)))
                    e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            };
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
                cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
                return cp;
            }
        }

        private Label MakeHeaderButton(string text)
        {
            var label = new Label
            {
                Dock = DockStyle.Fill,
                Text = text,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Accent,
                BackColor = Color.FromArgb(30, 34, 42),
                Cursor = Cursors.Hand,
                Margin = new Padding(2, 0, 0, 0),
                Font = new Font("Segoe UI", 10F, FontStyle.Bold)
            };
            return label;
        }

        public void SetPath(string name, string path, bool isThisPc)
        {
            pathLabel.Text = isThisPc ? "此电脑 · 磁盘占用" : (string.IsNullOrWhiteSpace(path) ? name : path);
        }

        public void SetScanning()
        {
            statusLabel.Text = "正在计算这一层的文件 / 文件夹占用…";
            map.SetItems(new List<DiskItem>());
        }

        public void SetProgress(ScanProgress p)
        {
            statusLabel.Text = string.Format("扫描中 · 已检查 {0:N0} 项 · 跳过 {1:N0} 项 · {2}", p.Visited, p.Skipped, p.CurrentName);
        }

        public void SetResult(ScanResult r)
        {
            map.SetItems(r.Items);
            statusLabel.Text = string.Format("{0:N0} 项 · {1} · 从大到小排列 · 单击方块直接打开 / 定位", r.Items.Count, FormatBytes(r.TotalBytes));
            if (r.Skipped > 0) statusLabel.Text += string.Format(" · 跳过 {0:N0} 个受限项", r.Skipped);
        }

        public void SetError(string error)
        {
            map.SetItems(new List<DiskItem>());
            statusLabel.Text = "无法读取：" + error;
        }

        private static string FormatBytes(long bytes)
        {
            string[] u = { "B", "KB", "MB", "GB", "TB" };
            double v = Math.Max(0, bytes);
            int i = 0;
            while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
            return (i == 0 ? v.ToString("0") : v.ToString(v >= 100 ? "0" : v >= 10 ? "0.0" : "0.00")) + " " + u[i];
        }
    }

    internal sealed class TileMapControl : Control
    {
        private readonly List<DiskItem> items = new List<DiskItem>();
        private readonly List<Tile> tiles = new List<Tile>();
        private DiskItem hover;
        public event EventHandler<DiskItem> ItemActivated;
        public event EventHandler<DiskItem> HoverChanged;

        public TileMapControl()
        {
            DoubleBuffered = true;
            BackColor = Color.FromArgb(15, 18, 23);
            Cursor = Cursors.Hand;
            Resize += delegate { BuildLayout(); Invalidate(); };
        }

        public void SetItems(IEnumerable<DiskItem> source)
        {
            items.Clear();
            if (source != null) items.AddRange(source.Where(x => x.Size > 0).OrderByDescending(x => x.Size).Take(140));
            BuildLayout();
            Invalidate();
        }

        private void BuildLayout()
        {
            tiles.Clear();
            if (items.Count == 0 || Width < 10 || Height < 10) return;
            LayoutGroup(items, new RectangleF(1, 1, Width - 2, Height - 2), 0);
        }

        private void LayoutGroup(IList<DiskItem> group, RectangleF rect, int depth)
        {
            if (group == null || group.Count == 0 || rect.Width < 2 || rect.Height < 2) return;
            if (group.Count == 1)
            {
                tiles.Add(new Tile { Item = group[0], Bounds = Shrink(rect, 1.2F) });
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
            long aSum = a.Sum(x => x.Size);
            float ratio = Math.Max(0.08F, Math.Min(0.92F, (float)aSum / sum));

            if ((depth == 0) || rect.Width >= rect.Height)
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

        private static RectangleF Shrink(RectangleF r, float v)
        {
            return new RectangleF(r.X + v, r.Y + v, Math.Max(0, r.Width - v * 2), Math.Max(0, r.Height - v * 2));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            if (tiles.Count == 0)
            {
                using (var b = new SolidBrush(Color.FromArgb(126, 136, 150)))
                    e.Graphics.DrawString("等待扫描…", Font, b, new PointF(12, 12));
                return;
            }

            foreach (var tile in tiles)
            {
                RectangleF r = tile.Bounds;
                if (r.Width < 1 || r.Height < 1) continue;
                Color c = tile.Item.Fill;
                if (object.ReferenceEquals(tile.Item, hover)) c = Lighten(c, 20);
                using (var path = RoundedRect(r, Math.Min(7F, Math.Min(r.Width, r.Height) / 5F)))
                using (var b = new SolidBrush(c))
                using (var p = new Pen(Color.FromArgb(85, 255, 255, 255)))
                {
                    e.Graphics.FillPath(b, path);
                    e.Graphics.DrawPath(p, path);
                }

                if (r.Width >= 54 && r.Height >= 28)
                {
                    string name = tile.Item.Name;
                    string size = FormatBytes(tile.Item.Size);
                    Rectangle textRect = Rectangle.Round(new RectangleF(r.X + 5, r.Y + 4, r.Width - 10, r.Height - 8));
                    TextRenderer.DrawText(e.Graphics, name + "\r\n" + size, new Font("Segoe UI", r.Width > 150 ? 8.5F : 7.5F, FontStyle.Bold), textRect,
                        Color.White, TextFormatFlags.EndEllipsis | TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPadding);
                }
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
                var h = HoverChanged; if (h != null) h(this, hit);
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hover = null;
            Invalidate();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left) return;
            DiskItem hit = Hit(e.Location);
            if (hit != null)
            {
                var h = ItemActivated; if (h != null) h(this, hit);
            }
        }

        private DiskItem Hit(Point p)
        {
            for (int i = 0; i < tiles.Count; i++) if (tiles[i].Bounds.Contains(p)) return tiles[i].Item;
            return null;
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

        private static Color Lighten(Color c, int amount)
        {
            return Color.FromArgb(c.A, Math.Min(255, c.R + amount), Math.Min(255, c.G + amount), Math.Min(255, c.B + amount));
        }

        private static string FormatBytes(long bytes)
        {
            double v = bytes; string[] u = { "B", "KB", "MB", "GB", "TB" }; int i = 0;
            while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
            return v.ToString(v >= 100 ? "0" : v >= 10 ? "0.0" : "0.00") + " " + u[i];
        }

        private sealed class Tile
        {
            public DiskItem Item;
            public RectangleF Bounds;
        }
    }

    internal static class Scanner
    {
        public static ScanResult ScanThisPc(CancellationToken token)
        {
            var result = new ScanResult();
            foreach (DriveInfo d in DriveInfo.GetDrives().Where(x => x.IsReady))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    long used = Math.Max(0, d.TotalSize - d.AvailableFreeSpace);
                    result.Items.Add(new DiskItem
                    {
                        Name = string.Format("{0} {1}", d.Name, string.IsNullOrWhiteSpace(d.VolumeLabel) ? "本地磁盘" : d.VolumeLabel),
                        Path = d.RootDirectory.FullName,
                        Size = used,
                        IsFolder = true,
                        Fill = Color.FromArgb(58, 108, 155)
                    });
                    result.TotalBytes += used;
                }
                catch { result.Skipped++; }
            }
            result.Items = result.Items.OrderByDescending(x => x.Size).ToList();
            return result;
        }

        public static ScanResult ScanFolder(string path, CancellationToken token, IProgress<ScanProgress> progress)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) throw new IOException("目录不存在或当前无权访问。");
            var result = new ScanResult();
            FileSystemInfo[] entries;
            try { entries = new DirectoryInfo(path).GetFileSystemInfos(); }
            catch { throw new IOException("无法读取当前目录。"); }

            long visited = 0;
            foreach (FileSystemInfo entry in entries)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) { result.Skipped++; continue; }
                    bool folder = (entry.Attributes & FileAttributes.Directory) != 0;
                    long size = folder
                        ? CalculateFolder(entry.FullName, token, ref visited, ref result.Skipped, progress, entry.Name)
                        : ((FileInfo)entry).Length;
                    if (!folder) visited++;
                    if (size <= 0) continue;
                    result.Items.Add(new DiskItem
                    {
                        Name = entry.Name,
                        Path = entry.FullName,
                        Size = size,
                        IsFolder = folder,
                        Fill = ColorFor(entry.FullName, folder)
                    });
                    result.TotalBytes += size;
                }
                catch (OperationCanceledException) { throw; }
                catch { result.Skipped++; }

                if (progress != null) progress.Report(new ScanProgress { CurrentName = entry.Name, Visited = visited, Skipped = result.Skipped });
            }
            result.Items = result.Items.OrderByDescending(x => x.Size).ToList();
            return result;
        }

        private static long CalculateFolder(string root, CancellationToken token, ref long visited, ref int skipped, IProgress<ScanProgress> progress, string topName)
        {
            long total = 0;
            var stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                token.ThrowIfCancellationRequested();
                string dir = stack.Pop();

                try
                {
                    foreach (string file in Directory.EnumerateFiles(dir))
                    {
                        token.ThrowIfCancellationRequested();
                        try { total += new FileInfo(file).Length; } catch { skipped++; }
                        visited++;
                        if ((visited & 511) == 0 && progress != null)
                            progress.Report(new ScanProgress { CurrentName = topName, Visited = visited, Skipped = skipped });
                    }
                }
                catch { skipped++; }

                try
                {
                    foreach (string sub in Directory.EnumerateDirectories(dir))
                    {
                        token.ThrowIfCancellationRequested();
                        try
                        {
                            var di = new DirectoryInfo(sub);
                            if ((di.Attributes & FileAttributes.ReparsePoint) != 0) { skipped++; continue; }
                            stack.Push(sub);
                        }
                        catch { skipped++; }
                    }
                }
                catch { skipped++; }
            }
            return total;
        }

        private static Color ColorFor(string path, bool folder)
        {
            if (folder) return Color.FromArgb(55, 103, 151);
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (new[] { ".mp4", ".mkv", ".mov", ".avi", ".webm" }.Contains(ext)) return Color.FromArgb(122, 78, 158);
            if (new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".psd" }.Contains(ext)) return Color.FromArgb(55, 132, 111);
            if (new[] { ".zip", ".7z", ".rar", ".iso", ".tar", ".gz" }.Contains(ext)) return Color.FromArgb(170, 109, 57);
            if (new[] { ".exe", ".msi", ".dll", ".sys" }.Contains(ext)) return Color.FromArgb(150, 66, 74);
            if (new[] { ".mp3", ".flac", ".wav", ".aac", ".ogg" }.Contains(ext)) return Color.FromArgb(149, 75, 121);
            if (new[] { ".doc", ".docx", ".pdf", ".xls", ".xlsx", ".ppt", ".pptx", ".txt" }.Contains(ext)) return Color.FromArgb(54, 122, 140);
            return Color.FromArgb(82, 91, 105);
        }
    }

    internal static class ExplorerProbe
    {
        public static ExplorerInfo TryGetForegroundExplorer()
        {
            IntPtr foreground = NativeMethods.GetForegroundWindow();
            if (foreground == IntPtr.Zero) return null;

            object shell = null;
            object windows = null;
            try
            {
                Type shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType == null) return null;
                shell = Activator.CreateInstance(shellType);
                dynamic dshell = shell;
                windows = dshell.Windows();
                dynamic dwindows = windows;
                int count = Convert.ToInt32(dwindows.Count);
                for (int i = 0; i < count; i++)
                {
                    object wObj = null;
                    try
                    {
                        wObj = dwindows.Item(i);
                        if (wObj == null) continue;
                        dynamic w = wObj;
                        IntPtr hwnd = new IntPtr(Convert.ToInt64(w.HWND));
                        if (hwnd != foreground) continue;

                        string displayName = string.Empty;
                        string path = string.Empty;
                        try { displayName = Convert.ToString(w.LocationName); } catch { }
                        try { path = Convert.ToString(w.Document.Folder.Self.Path); } catch { }

                        bool real = !string.IsNullOrWhiteSpace(path) && Directory.Exists(path);
                        bool thisPc = (!real && (displayName.IndexOf("此电脑", StringComparison.OrdinalIgnoreCase) >= 0 || displayName.IndexOf("This PC", StringComparison.OrdinalIgnoreCase) >= 0 || path.IndexOf("20D04FE0", StringComparison.OrdinalIgnoreCase) >= 0));
                        if (!real && !thisPc) return null;

                        return new ExplorerInfo { Hwnd = hwnd, Path = real ? path : string.Empty, IsThisPc = thisPc, DisplayName = displayName };
                    }
                    catch { }
                    finally { if (wObj != null && Marshal.IsComObject(wObj)) try { Marshal.FinalReleaseComObject(wObj); } catch { } }
                }
            }
            catch { }
            finally
            {
                if (windows != null && Marshal.IsComObject(windows)) try { Marshal.FinalReleaseComObject(windows); } catch { }
                if (shell != null && Marshal.IsComObject(shell)) try { Marshal.FinalReleaseComObject(shell); } catch { }
            }
            return null;
        }
    }

    internal sealed class ExplorerInfo
    {
        public IntPtr Hwnd;
        public string Path;
        public string DisplayName;
        public bool IsThisPc;
    }

    internal sealed class ScanResult
    {
        public List<DiskItem> Items = new List<DiskItem>();
        public long TotalBytes;
        public int Skipped;
    }

    internal sealed class ScanProgress
    {
        public string CurrentName;
        public long Visited;
        public int Skipped;
    }

    internal sealed class DiskItem
    {
        public string Name;
        public string Path;
        public long Size;
        public bool IsFolder;
        public Color Fill;
    }

    internal static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct RECT { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    }
}
