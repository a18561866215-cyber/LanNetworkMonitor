using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DiskTreemap
{
    public sealed class MainForm : Form
    {
        private readonly ComboBox driveBox = new ComboBox();
        private readonly Button scanButton = new Button();
        private readonly Button folderButton = new Button();
        private readonly Button upButton = new Button();
        private readonly Button stopButton = new Button();
        private readonly Button explorerButton = new Button();
        private readonly Label pathLabel = new Label();
        private readonly Label summaryLabel = new Label();
        private readonly Label statusLabel = new Label();
        private readonly TreemapControl treemap = new TreemapControl();
        private CancellationTokenSource scanCts;
        private string currentPath;

        private static readonly Color Bg = Color.FromArgb(10, 12, 16);
        private static readonly Color Panel = Color.FromArgb(18, 21, 27);
        private static readonly Color Panel2 = Color.FromArgb(25, 29, 37);
        private static readonly Color Border = Color.FromArgb(48, 55, 67);
        private static readonly Color TextMain = Color.FromArgb(239, 243, 248);
        private static readonly Color TextMuted = Color.FromArgb(146, 154, 166);
        private static readonly Color Accent = Color.FromArgb(116, 232, 255);
        private static readonly Color Danger = Color.FromArgb(255, 112, 123);

        public MainForm()
        {
            Text = "磁盘空间地图 · V0.1";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(920, 620);
            Size = new Size(1260, 820);
            BackColor = Bg;
            ForeColor = TextMain;
            Font = new Font("Segoe UI", 9F);
            BuildUi();
            LoadDrives();
            Shown += async (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(currentPath))
                    await ScanPathAsync(currentPath);
            };
        }

        private void BuildUi()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Bg,
                Padding = new Padding(18),
                ColumnCount = 1,
                RowCount = 5
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            var header = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                Margin = new Padding(0, 0, 0, 14)
            };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var titleStack = new FlowLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Margin = new Padding(0)
            };
            var title = new Label
            {
                Text = "磁盘空间地图",
                AutoSize = true,
                Font = new Font("Segoe UI", 23F, FontStyle.Bold),
                ForeColor = TextMain,
                Margin = new Padding(0)
            };
            var sub = new Label
            {
                Text = "越大的方块，占用空间越大 · 文件单击直接定位 · 文件夹单击继续钻取",
                AutoSize = true,
                ForeColor = TextMuted,
                Font = new Font("Segoe UI", 10F),
                Margin = new Padding(2, 5, 0, 0)
            };
            titleStack.Controls.Add(title);
            titleStack.Controls.Add(sub);
            header.Controls.Add(titleStack, 0, 0);

            summaryLabel.Text = "等待扫描";
            summaryLabel.AutoSize = true;
            summaryLabel.ForeColor = Accent;
            summaryLabel.Font = new Font("Consolas", 10.5F, FontStyle.Bold);
            summaryLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            summaryLabel.Margin = new Padding(14, 10, 0, 0);
            header.Controls.Add(summaryLabel, 1, 0);
            root.Controls.Add(header, 0, 0);

            var toolbar = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 7,
                BackColor = Panel,
                Padding = new Padding(10),
                Margin = new Padding(0, 0, 0, 10)
            };
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 235F));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            driveBox.DropDownStyle = ComboBoxStyle.DropDownList;
            driveBox.BackColor = Panel2;
            driveBox.ForeColor = TextMain;
            driveBox.FlatStyle = FlatStyle.Flat;
            driveBox.Dock = DockStyle.Fill;
            driveBox.Margin = new Padding(0, 2, 8, 2);
            driveBox.SelectedIndexChanged += (s, e) =>
            {
                var item = driveBox.SelectedItem as DriveEntry;
                if (item != null) currentPath = item.Root;
                UpdatePathLabel();
            };
            toolbar.Controls.Add(driveBox, 0, 0);

            ConfigureButton(scanButton, "扫描当前", Accent, Color.FromArgb(7, 24, 29));
            scanButton.Click += async (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(currentPath))
                    await ScanPathAsync(currentPath);
            };
            toolbar.Controls.Add(scanButton, 1, 0);

            ConfigureButton(folderButton, "选择文件夹", Panel2, TextMain);
            folderButton.Click += async (s, e) =>
            {
                using (var dlg = new FolderBrowserDialog())
                {
                    dlg.Description = "选择要分析的磁盘或文件夹";
                    dlg.ShowNewFolderButton = false;
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                    {
                        currentPath = dlg.SelectedPath;
                        UpdatePathLabel();
                        await ScanPathAsync(currentPath);
                    }
                }
            };
            toolbar.Controls.Add(folderButton, 2, 0);

            ConfigureButton(upButton, "上一级", Panel2, TextMain);
            upButton.Click += async (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(currentPath)) return;
                try
                {
                    var parent = Directory.GetParent(currentPath.TrimEnd(Path.DirectorySeparatorChar));
                    if (parent != null)
                    {
                        currentPath = parent.FullName;
                        UpdatePathLabel();
                        await ScanPathAsync(currentPath);
                    }
                }
                catch { }
            };
            toolbar.Controls.Add(upButton, 3, 0);

            ConfigureButton(explorerButton, "资源管理器", Panel2, TextMain);
            explorerButton.Click += (s, e) => OpenFolderInExplorer(currentPath);
            toolbar.Controls.Add(explorerButton, 4, 0);

            ConfigureButton(stopButton, "停止", Color.FromArgb(70, 31, 35), Danger);
            stopButton.Enabled = false;
            stopButton.Click += (s, e) => { if (scanCts != null) scanCts.Cancel(); };
            toolbar.Controls.Add(stopButton, 5, 0);
            root.Controls.Add(toolbar, 0, 1);

            pathLabel.Dock = DockStyle.Top;
            pathLabel.AutoSize = true;
            pathLabel.Font = new Font("Consolas", 9.5F);
            pathLabel.ForeColor = TextMuted;
            pathLabel.BackColor = Panel2;
            pathLabel.Padding = new Padding(12, 9, 12, 9);
            pathLabel.Margin = new Padding(0, 0, 0, 10);
            pathLabel.AutoEllipsis = true;
            root.Controls.Add(pathLabel, 0, 2);

            treemap.Dock = DockStyle.Fill;
            treemap.BackColor = Panel;
            treemap.Margin = new Padding(0);
            treemap.ItemActivated += async (s, item) =>
            {
                if (item == null || string.IsNullOrWhiteSpace(item.Path)) return;
                if (item.IsFolder)
                {
                    currentPath = item.Path;
                    UpdatePathLabel();
                    await ScanPathAsync(currentPath);
                }
                else
                {
                    RevealFile(item.Path);
                }
            };
            treemap.HoverChanged += (s, item) =>
            {
                if (item == null)
                {
                    statusLabel.Text = "提示：文件单击会在资源管理器中定位；文件夹单击会进入该文件夹。";
                }
                else
                {
                    statusLabel.Text = string.Format("{0}  ·  {1}  ·  {2}", item.Name, FormatBytes(item.Size), item.Path);
                }
            };
            root.Controls.Add(treemap, 0, 3);

            statusLabel.Dock = DockStyle.Top;
            statusLabel.AutoSize = true;
            statusLabel.ForeColor = TextMuted;
            statusLabel.BackColor = Bg;
            statusLabel.Padding = new Padding(2, 10, 2, 0);
            statusLabel.Text = "准备就绪";
            root.Controls.Add(statusLabel, 0, 4);
        }

        private void ConfigureButton(Button button, string text, Color back, Color fore)
        {
            button.Text = text;
            button.AutoSize = true;
            button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = Border;
            button.BackColor = back;
            button.ForeColor = fore;
            button.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            button.Padding = new Padding(10, 5, 10, 5);
            button.Margin = new Padding(4, 0, 4, 0);
        }

        private void LoadDrives()
        {
            driveBox.Items.Clear();
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
            {
                try
                {
                    driveBox.Items.Add(new DriveEntry
                    {
                        Root = drive.RootDirectory.FullName,
                        Text = string.Format("{0}  {1}  ·  可用 {2} / {3}",
                            drive.Name,
                            string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "本地磁盘" : drive.VolumeLabel,
                            FormatBytes(drive.AvailableFreeSpace),
                            FormatBytes(drive.TotalSize))
                    });
                }
                catch { }
            }

            if (driveBox.Items.Count > 0)
            {
                int cIndex = -1;
                for (int i = 0; i < driveBox.Items.Count; i++)
                {
                    var entry = driveBox.Items[i] as DriveEntry;
                    if (entry != null && entry.Root.StartsWith("C:", StringComparison.OrdinalIgnoreCase))
                    {
                        cIndex = i;
                        break;
                    }
                }
                driveBox.SelectedIndex = cIndex >= 0 ? cIndex : 0;
            }
        }

        private async Task ScanPathAsync(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                MessageBox.Show(this, "这个路径现在不可用。", "磁盘空间地图", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (scanCts != null)
            {
                scanCts.Cancel();
                scanCts.Dispose();
            }
            scanCts = new CancellationTokenSource();
            var token = scanCts.Token;

            SetScanningState(true);
            treemap.SetItems(new List<DiskItem>());
            statusLabel.Text = "正在读取目录结构…";
            summaryLabel.Text = "扫描中…";

            var progress = new Progress<ScanProgress>(p =>
            {
                statusLabel.Text = string.Format("正在扫描：{0}  ·  已检查 {1:N0} 个文件/目录  ·  跳过 {2:N0} 个受限项",
                    p.CurrentName, p.Visited, p.Skipped);
            });

            try
            {
                var result = await Task.Run(() => ScanDirectory(path, token, progress), token);
                if (token.IsCancellationRequested) return;

                currentPath = path;
                UpdatePathLabel();
                treemap.SetItems(result.Items);
                summaryLabel.Text = string.Format("可视化 {0}  ·  {1:N0} 项", FormatBytes(result.TotalBytes), result.Items.Count);
                statusLabel.Text = result.Skipped > 0
                    ? string.Format("完成 · 跳过 {0:N0} 个无权限/已消失项目。面积按文件或文件夹实际大小计算。", result.Skipped)
                    : "完成 · 面积按文件或文件夹实际大小计算。";
            }
            catch (OperationCanceledException)
            {
                statusLabel.Text = "扫描已停止。";
                summaryLabel.Text = "已停止";
            }
            catch (Exception ex)
            {
                statusLabel.Text = "扫描失败：" + ex.Message;
                MessageBox.Show(this, ex.Message, "扫描失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetScanningState(false);
            }
        }

        private static ScanResult ScanDirectory(string path, CancellationToken token, IProgress<ScanProgress> progress)
        {
            var result = new ScanResult();
            FileSystemInfo[] entries;
            try
            {
                entries = new DirectoryInfo(path).GetFileSystemInfos();
            }
            catch
            {
                throw new IOException("无法读取该目录。可以尝试选择一个你有权限访问的文件夹。");
            }

            long visited = 0;
            int topIndex = 0;
            foreach (var entry in entries)
            {
                token.ThrowIfCancellationRequested();
                topIndex++;
                try
                {
                    if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        result.Skipped++;
                        continue;
                    }

                    long size;
                    bool isFolder = (entry.Attributes & FileAttributes.Directory) != 0;
                    if (isFolder)
                    {
                        size = CalculateDirectorySize(entry.FullName, token, result, ref visited, progress, entry.Name);
                    }
                    else
                    {
                        size = ((FileInfo)entry).Length;
                        visited++;
                    }

                    if (size > 0)
                    {
                        result.Items.Add(new DiskItem
                        {
                            Name = entry.Name,
                            Path = entry.FullName,
                            Size = size,
                            IsFolder = isFolder,
                            Fill = GetItemColor(entry.FullName, isFolder)
                        });
                        result.TotalBytes += size;
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch
                {
                    result.Skipped++;
                }

                progress.Report(new ScanProgress
                {
                    CurrentName = entry.Name,
                    Visited = visited,
                    Skipped = result.Skipped
                });
            }

            result.Items = result.Items.OrderByDescending(i => i.Size).ToList();
            return result;
        }

        private static long CalculateDirectorySize(string root, CancellationToken token, ScanResult result,
            ref long visited, IProgress<ScanProgress> progress, string topName)
        {
            long total = 0;
            var stack = new Stack<string>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                token.ThrowIfCancellationRequested();
                var dir = stack.Pop();

                string[] files;
                try { files = Directory.GetFiles(dir); }
                catch { result.Skipped++; files = new string[0]; }

                foreach (var file in files)
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        var fi = new FileInfo(file);
                        total += fi.Length;
                        visited++;
                    }
                    catch { result.Skipped++; }

                    if ((visited & 1023) == 0)
                    {
                        progress.Report(new ScanProgress { CurrentName = topName, Visited = visited, Skipped = result.Skipped });
                    }
                }

                string[] dirs;
                try { dirs = Directory.GetDirectories(dir); }
                catch { result.Skipped++; dirs = new string[0]; }

                foreach (var child in dirs)
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        var di = new DirectoryInfo(child);
                        if ((di.Attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            result.Skipped++;
                            continue;
                        }
                        stack.Push(child);
                        visited++;
                    }
                    catch { result.Skipped++; }
                }
            }

            return total;
        }

        private static Color GetItemColor(string path, bool isFolder)
        {
            if (isFolder) return Color.FromArgb(74, 139, 189);
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (new[] { ".mp4", ".mkv", ".mov", ".avi", ".webm", ".flv" }.Contains(ext)) return Color.FromArgb(151, 103, 210);
            if (new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".psd", ".clip" }.Contains(ext)) return Color.FromArgb(210, 103, 151);
            if (new[] { ".zip", ".7z", ".rar", ".iso", ".tar", ".gz" }.Contains(ext)) return Color.FromArgb(205, 146, 69);
            if (new[] { ".exe", ".msi", ".dll", ".sys" }.Contains(ext)) return Color.FromArgb(89, 163, 117);
            if (new[] { ".mp3", ".wav", ".flac", ".aac", ".ogg" }.Contains(ext)) return Color.FromArgb(80, 171, 159);
            return Color.FromArgb(91, 119, 150);
        }

        private void SetScanningState(bool scanning)
        {
            scanButton.Enabled = !scanning;
            folderButton.Enabled = !scanning;
            upButton.Enabled = !scanning;
            driveBox.Enabled = !scanning;
            stopButton.Enabled = scanning;
            UseWaitCursor = scanning;
        }

        private void UpdatePathLabel()
        {
            pathLabel.Text = "当前位置  ›  " + (currentPath ?? "未选择");
        }

        private static void RevealFile(string path)
        {
            try
            {
                Process.Start("explorer.exe", "/select,\"" + path + "\"");
            }
            catch { }
        }

        private static void OpenFolderInExplorer(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try { Process.Start("explorer.exe", "\"" + path + "\""); }
            catch { }
        }

        public static string FormatBytes(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB", "PB" };
            double value = bytes;
            int unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }
            return value.ToString(value >= 100 ? "0" : value >= 10 ? "0.0" : "0.00") + " " + units[unit];
        }

        private sealed class DriveEntry
        {
            public string Root;
            public string Text;
            public override string ToString() { return Text; }
        }
    }

    internal sealed class TreemapControl : Control
    {
        private readonly List<DiskItem> items = new List<DiskItem>();
        private readonly List<LayoutCell> cells = new List<LayoutCell>();
        private readonly ToolTip tooltip = new ToolTip();
        private DiskItem hovered;

        public event EventHandler<DiskItem> ItemActivated;
        public event EventHandler<DiskItem> HoverChanged;

        public TreemapControl()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            tooltip.AutoPopDelay = 8000;
            tooltip.InitialDelay = 250;
            tooltip.ReshowDelay = 100;
        }

        public void SetItems(List<DiskItem> newItems)
        {
            items.Clear();
            if (newItems != null) items.AddRange(newItems.Where(i => i.Size > 0));
            RebuildLayout();
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            RebuildLayout();
        }

        private void RebuildLayout()
        {
            cells.Clear();
            if (items.Count == 0 || ClientSize.Width < 10 || ClientSize.Height < 10) return;
            LayoutRange(items, 0, items.Count, new RectangleF(3, 3, ClientSize.Width - 6, ClientSize.Height - 6), cells);
        }

        private static void LayoutRange(List<DiskItem> source, int start, int count, RectangleF rect, List<LayoutCell> output)
        {
            if (count <= 0 || rect.Width < 1 || rect.Height < 1) return;
            if (count == 1)
            {
                output.Add(new LayoutCell { Item = source[start], Rect = Inset(rect, 1.3f) });
                return;
            }

            long total = 0;
            for (int i = start; i < start + count; i++) total += source[i].Size;
            if (total <= 0) return;

            long acc = 0;
            int split = 1;
            long bestDiff = long.MaxValue;
            for (int i = 1; i < count; i++)
            {
                acc += source[start + i - 1].Size;
                long diff = Math.Abs(total - 2 * acc);
                if (diff < bestDiff)
                {
                    bestDiff = diff;
                    split = i;
                }
                else break;
            }

            long firstTotal = 0;
            for (int i = start; i < start + split; i++) firstTotal += source[i].Size;
            float ratio = Math.Max(0.02f, Math.Min(0.98f, (float)firstTotal / total));

            RectangleF a, b;
            if (rect.Width >= rect.Height)
            {
                float w = rect.Width * ratio;
                a = new RectangleF(rect.X, rect.Y, w, rect.Height);
                b = new RectangleF(rect.X + w, rect.Y, rect.Width - w, rect.Height);
            }
            else
            {
                float h = rect.Height * ratio;
                a = new RectangleF(rect.X, rect.Y, rect.Width, h);
                b = new RectangleF(rect.X, rect.Y + h, rect.Width, rect.Height - h);
            }

            LayoutRange(source, start, split, a, output);
            LayoutRange(source, start + split, count - split, b, output);
        }

        private static RectangleF Inset(RectangleF r, float p)
        {
            if (r.Width <= p * 2 || r.Height <= p * 2) return r;
            return new RectangleF(r.X + p, r.Y + p, r.Width - p * 2, r.Height - p * 2);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            e.Graphics.Clear(Color.FromArgb(16, 19, 24));

            if (cells.Count == 0)
            {
                using (var font = new Font("Segoe UI", 16F, FontStyle.Bold))
                using (var brush = new SolidBrush(Color.FromArgb(120, 130, 145)))
                {
                    var text = "选择磁盘或文件夹，然后开始扫描";
                    var size = e.Graphics.MeasureString(text, font);
                    e.Graphics.DrawString(text, font, brush, (Width - size.Width) / 2F, (Height - size.Height) / 2F);
                }
                return;
            }

            foreach (var cell in cells)
            {
                var r = cell.Rect;
                if (r.Width < 0.6f || r.Height < 0.6f) continue;
                var fill = cell.Item == hovered ? Lighten(cell.Item.Fill, 24) : cell.Item.Fill;
                using (var brush = new SolidBrush(fill))
                using (var pen = new Pen(Color.FromArgb(65, 7, 10, 14), 1F))
                {
                    e.Graphics.FillRectangle(brush, r);
                    e.Graphics.DrawRectangle(pen, r.X, r.Y, Math.Max(0, r.Width - 1), Math.Max(0, r.Height - 1));
                }

                if (r.Width >= 66 && r.Height >= 36)
                {
                    float fontSize = r.Width > 180 && r.Height > 90 ? 10F : 8.5F;
                    using (var font = new Font("Segoe UI", fontSize, FontStyle.Bold))
                    {
                        var textRect = Rectangle.Round(new RectangleF(r.X + 6, r.Y + 5, r.Width - 12, r.Height - 10));
                        TextRenderer.DrawText(e.Graphics,
                            cell.Item.Name + Environment.NewLine + MainForm.FormatBytes(cell.Item.Size),
                            font,
                            textRect,
                            Color.White,
                            TextFormatFlags.EndEllipsis | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
                    }
                }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var item = HitTest(e.Location);
            if (!ReferenceEquals(item, hovered))
            {
                hovered = item;
                Invalidate();
                if (item != null)
                {
                    tooltip.SetToolTip(this, item.Name + "\n" + MainForm.FormatBytes(item.Size) + "\n" + item.Path);
                }
                else
                {
                    tooltip.SetToolTip(this, null);
                }
                if (HoverChanged != null) HoverChanged(this, item);
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hovered = null;
            tooltip.SetToolTip(this, null);
            Invalidate();
            if (HoverChanged != null) HoverChanged(this, null);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left) return;
            var item = HitTest(e.Location);
            if (item != null && ItemActivated != null) ItemActivated(this, item);
        }

        private DiskItem HitTest(Point p)
        {
            for (int i = 0; i < cells.Count; i++)
            {
                if (cells[i].Rect.Contains(p)) return cells[i].Item;
            }
            return null;
        }

        private static Color Lighten(Color c, int amount)
        {
            return Color.FromArgb(c.A,
                Math.Min(255, c.R + amount),
                Math.Min(255, c.G + amount),
                Math.Min(255, c.B + amount));
        }

        private sealed class LayoutCell
        {
            public DiskItem Item;
            public RectangleF Rect;
        }
    }

    internal sealed class DiskItem
    {
        public string Name;
        public string Path;
        public long Size;
        public bool IsFolder;
        public Color Fill;
    }

    internal sealed class ScanResult
    {
        public List<DiskItem> Items = new List<DiskItem>();
        public long TotalBytes;
        public long Skipped;
    }

    internal sealed class ScanProgress
    {
        public string CurrentName;
        public long Visited;
        public long Skipped;
    }
}
