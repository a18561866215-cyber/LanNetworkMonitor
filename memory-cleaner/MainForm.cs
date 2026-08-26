using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MemoryCleaner
{
    public sealed class MainForm : Form
    {
        private readonly DataGridView grid = new DataGridView();
        private readonly TextBox searchBox = new TextBox();
        private readonly CheckBox protectBrowsers = new CheckBox();
        private readonly CheckBox showProtected = new CheckBox();
        private readonly Label memoryLabel = new Label();
        private readonly Label selectionLabel = new Label();
        private readonly Label statusLabel = new Label();
        private readonly Label backgroundLabel = new Label();
        private readonly RoundButton refreshButton = new RoundButton();
        private readonly RoundButton killButton = new RoundButton();
        private readonly RoundButton clearSelectionButton = new RoundButton();
        private readonly RoundButton chooseBackgroundButton = new RoundButton();
        private readonly RoundButton clearBackgroundButton = new RoundButton();
        private readonly GlassPanel shell = new GlassPanel();
        private readonly Timer resizeTimer = new Timer();

        private readonly List<ProcessItem> allItems = new List<ProcessItem>();
        private readonly Dictionary<string, Bitmap> iconCache = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        private readonly int selfPid = Process.GetCurrentProcess().Id;

        private Bitmap renderedBackground;
        private Bitmap blurredBackground;
        private Bitmap gridBackdrop;
        private string backgroundPath = string.Empty;

        private static readonly HashSet<string> CriticalNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "system", "idle", "registry", "secure system", "memory compression",
            "smss", "csrss", "wininit", "winlogon", "services", "lsass", "svchost",
            "fontdrvhost", "dwm", "sihost", "taskhostw", "explorer",
            "startmenuexperiencehost", "shellexperiencehost", "searchhost", "searchindexer",
            "securityhealthservice", "securityhealthsystray", "msmpeng", "audiodg", "ctfmon",
            "systemsettings", "lockapp", "winlogon"
        };

        private static readonly HashSet<string> BrowserNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "chrome", "msedge", "firefox", "brave", "opera", "opera_gx",
            "vivaldi", "arc", "msedgewebview2"
        };

        private static readonly HashSet<string> ChatNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "chatgpt", "openai"
        };

        private static readonly Color FallbackBg = Color.FromArgb(8, 10, 14);
        private static readonly Color TextMain = Color.FromArgb(241, 244, 248);
        private static readonly Color TextMuted = Color.FromArgb(156, 164, 177);
        private static readonly Color Accent = Color.FromArgb(119, 236, 255);
        private static readonly Color AccentDark = Color.FromArgb(10, 28, 33);
        private static readonly Color Danger = Color.FromArgb(255, 103, 121);

        private string SettingsFolder
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BackgroundProcessCleaner"); }
        }

        private string BackgroundSettingFile
        {
            get { return Path.Combine(SettingsFolder, "background.txt"); }
        }

        public MainForm()
        {
            Text = "后台进程整理器 · V0.2";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(940, 620);
            Size = new Size(1220, 790);
            BackColor = FallbackBg;
            ForeColor = TextMain;
            Font = new Font("Segoe UI", 9F);
            DoubleBuffered = true;
            Padding = new Padding(16);

            TryRestoreBackgroundSetting();
            BuildUi();
            ApplyWindowIcon();

            resizeTimer.Interval = 180;
            resizeTimer.Tick += delegate
            {
                resizeTimer.Stop();
                RebuildBackground();
            };
            SizeChanged += delegate
            {
                if (WindowState != FormWindowState.Minimized)
                {
                    resizeTimer.Stop();
                    resizeTimer.Start();
                }
            };
            Shown += delegate
            {
                RebuildBackground();
                ScanProcesses();
            };
            FormClosed += delegate { DisposeVisualCache(); };
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (renderedBackground != null)
            {
                e.Graphics.DrawImage(renderedBackground, ClientRectangle);
                return;
            }

            using (LinearGradientBrush brush = new LinearGradientBrush(ClientRectangle,
                Color.FromArgb(8, 10, 14), Color.FromArgb(15, 22, 30), 35f))
            {
                e.Graphics.FillRectangle(brush, ClientRectangle);
            }
        }

        private void BuildUi()
        {
            shell.Dock = DockStyle.Fill;
            shell.CornerRadius = 24;
            shell.TintColor = Color.FromArgb(168, 12, 15, 20);
            Controls.Add(shell);

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.BackColor = Color.Transparent;
            root.Padding = new Padding(18);
            root.ColumnCount = 1;
            root.RowCount = 5;
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            shell.Controls.Add(root);

            TableLayoutPanel header = new TableLayoutPanel();
            header.Dock = DockStyle.Top;
            header.AutoSize = true;
            header.BackColor = Color.Transparent;
            header.ColumnCount = 3;
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            PictureBox logo = new PictureBox();
            logo.Size = new Size(52, 52);
            logo.SizeMode = PictureBoxSizeMode.Zoom;
            logo.Image = CreateLogoBitmap(52);
            logo.Margin = new Padding(0, 0, 14, 0);
            header.Controls.Add(logo, 0, 0);

            FlowLayoutPanel titleStack = new FlowLayoutPanel();
            titleStack.FlowDirection = FlowDirection.TopDown;
            titleStack.WrapContents = false;
            titleStack.AutoSize = true;
            titleStack.Dock = DockStyle.Fill;
            titleStack.BackColor = Color.Transparent;
            titleStack.Margin = new Padding(0);

            Label title = new Label();
            title.Text = "后台进程整理器";
            title.AutoSize = true;
            title.ForeColor = TextMain;
            title.Font = new Font("Segoe UI", 22F, FontStyle.Bold);
            title.Margin = new Padding(0);
            titleStack.Controls.Add(title);

            Label subtitle = new Label();
            subtitle.Text = "进程图标 · 手动勾选 · 强制关闭 · 系统与 ChatGPT 保护";
            subtitle.AutoSize = true;
            subtitle.ForeColor = TextMuted;
            subtitle.Font = new Font("Segoe UI", 10F);
            subtitle.Margin = new Padding(1, 5, 0, 0);
            titleStack.Controls.Add(subtitle);
            header.Controls.Add(titleStack, 1, 0);

            memoryLabel.Text = "内存读取中…";
            memoryLabel.AutoSize = true;
            memoryLabel.ForeColor = Accent;
            memoryLabel.Font = new Font("Consolas", 11F, FontStyle.Bold);
            memoryLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            memoryLabel.Margin = new Padding(18, 8, 0, 0);
            header.Controls.Add(memoryLabel, 2, 0);
            root.Controls.Add(header, 0, 0);

            TableLayoutPanel toolbar = new TableLayoutPanel();
            toolbar.Dock = DockStyle.Top;
            toolbar.AutoSize = true;
            toolbar.BackColor = Color.Transparent;
            toolbar.ColumnCount = 2;
            toolbar.RowCount = 2;
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            toolbar.Margin = new Padding(0, 16, 0, 12);

            searchBox.Dock = DockStyle.Fill;
            searchBox.BackColor = Color.FromArgb(34, 38, 46);
            searchBox.ForeColor = TextMain;
            searchBox.BorderStyle = BorderStyle.FixedSingle;
            searchBox.Font = new Font("Segoe UI", 10F);
            searchBox.Margin = new Padding(0, 2, 14, 6);
            searchBox.TextChanged += delegate { RenderRows(); };
            toolbar.Controls.Add(searchBox, 0, 0);

            FlowLayoutPanel primaryButtons = new FlowLayoutPanel();
            primaryButtons.AutoSize = true;
            primaryButtons.WrapContents = false;
            primaryButtons.BackColor = Color.Transparent;
            primaryButtons.Margin = new Padding(0);

            ConfigureButton(refreshButton, "刷新扫描", Accent, AccentDark, 102);
            refreshButton.Click += delegate { ScanProcesses(); };
            primaryButtons.Controls.Add(refreshButton);

            ConfigureButton(clearSelectionButton, "取消勾选", Color.FromArgb(48, 53, 64), TextMain, 102);
            clearSelectionButton.Click += delegate { ClearSelections(); };
            primaryButtons.Controls.Add(clearSelectionButton);

            ConfigureButton(killButton, "强制关闭选中", Danger, Color.White, 132);
            killButton.Click += delegate { KillSelected(); };
            primaryButtons.Controls.Add(killButton);
            toolbar.Controls.Add(primaryButtons, 1, 0);

            FlowLayoutPanel options = new FlowLayoutPanel();
            options.Dock = DockStyle.Fill;
            options.AutoSize = true;
            options.WrapContents = true;
            options.BackColor = Color.Transparent;
            options.Margin = new Padding(0, 2, 0, 0);

            protectBrowsers.Text = "保护浏览器 / ChatGPT";
            protectBrowsers.Checked = true;
            protectBrowsers.AutoSize = true;
            protectBrowsers.ForeColor = TextMain;
            protectBrowsers.Margin = new Padding(0, 7, 16, 0);
            protectBrowsers.CheckedChanged += delegate { ScanProcesses(); };
            options.Controls.Add(protectBrowsers);

            showProtected.Text = "显示受保护进程";
            showProtected.Checked = true;
            showProtected.AutoSize = true;
            showProtected.ForeColor = TextMain;
            showProtected.Margin = new Padding(0, 7, 18, 0);
            showProtected.CheckedChanged += delegate { RenderRows(); };
            options.Controls.Add(showProtected);

            ConfigureButton(chooseBackgroundButton, "选择背景图片", Color.FromArgb(55, 62, 76), TextMain, 112);
            chooseBackgroundButton.Click += delegate { ChooseBackground(); };
            options.Controls.Add(chooseBackgroundButton);

            ConfigureButton(clearBackgroundButton, "恢复默认背景", Color.FromArgb(43, 48, 58), TextMuted, 112);
            clearBackgroundButton.Click += delegate { ClearBackground(); };
            options.Controls.Add(clearBackgroundButton);

            backgroundLabel.AutoSize = true;
            backgroundLabel.ForeColor = TextMuted;
            backgroundLabel.Margin = new Padding(8, 8, 0, 0);
            UpdateBackgroundLabel();
            options.Controls.Add(backgroundLabel);
            toolbar.Controls.Add(options, 0, 1);
            toolbar.SetColumnSpan(options, 2);
            root.Controls.Add(toolbar, 0, 1);

            ConfigureGrid();
            root.Controls.Add(grid, 0, 2);

            TableLayoutPanel footer = new TableLayoutPanel();
            footer.Dock = DockStyle.Top;
            footer.AutoSize = true;
            footer.BackColor = Color.Transparent;
            footer.ColumnCount = 2;
            footer.Margin = new Padding(0, 12, 0, 0);
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            statusLabel.Text = "等待扫描";
            statusLabel.AutoSize = true;
            statusLabel.ForeColor = TextMuted;
            footer.Controls.Add(statusLabel, 0, 0);

            selectionLabel.Text = "已选 0 个 · 约 0 MB";
            selectionLabel.AutoSize = true;
            selectionLabel.ForeColor = Accent;
            selectionLabel.Font = new Font("Consolas", 10F, FontStyle.Bold);
            footer.Controls.Add(selectionLabel, 1, 0);
            root.Controls.Add(footer, 0, 3);

            RoundedLabel warning = new RoundedLabel();
            warning.Dock = DockStyle.Top;
            warning.AutoSize = true;
            warning.MaximumSize = new Size(1150, 0);
            warning.Text = "强制结束进程可能导致未保存数据丢失。浏览器默认受保护，避免把当前 ChatGPT 会话一起关掉；系统关键进程与本工具自身永久禁选。";
            warning.ForeColor = Color.FromArgb(235, 203, 133);
            warning.BackColor = Color.FromArgb(44, 35, 22);
            warning.Padding = new Padding(13, 10, 13, 10);
            warning.Margin = new Padding(0, 12, 0, 0);
            warning.CornerRadius = 12;
            root.Controls.Add(warning, 0, 4);

            Shown += delegate
            {
                SetCueBanner(searchBox, "搜索进程名 / PID / 路径");
                UpdateGridBackdrop();
            };
        }

        private void ConfigureGrid()
        {
            grid.Dock = DockStyle.Fill;
            grid.BackgroundColor = Color.FromArgb(18, 21, 27);
            grid.BorderStyle = BorderStyle.None;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.GridColor = Color.FromArgb(55, 61, 72);
            grid.RowHeadersVisible = false;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.MultiSelect = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            grid.RowTemplate.Height = 46;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersHeight = 40;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(38, 42, 50);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = TextMain;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(38, 42, 50);
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            grid.DefaultCellStyle.BackColor = Color.FromArgb(26, 29, 36);
            grid.DefaultCellStyle.ForeColor = TextMain;
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(45, 64, 73);
            grid.DefaultCellStyle.SelectionForeColor = Color.White;
            grid.DefaultCellStyle.Padding = new Padding(5, 0, 5, 0);

            DataGridViewCheckBoxColumn pick = new DataGridViewCheckBoxColumn();
            pick.Name = "Pick";
            pick.HeaderText = "选";
            pick.Width = 42;
            pick.FalseValue = false;
            pick.TrueValue = true;
            grid.Columns.Add(pick);

            DataGridViewImageColumn icon = new DataGridViewImageColumn();
            icon.Name = "Icon";
            icon.HeaderText = "";
            icon.Width = 48;
            icon.ImageLayout = DataGridViewImageCellLayout.Zoom;
            grid.Columns.Add(icon);

            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "程序 / 进程", Width = 200 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Pid", HeaderText = "PID", Width = 76 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Memory", HeaderText = "内存", Width = 105 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "状态", Width = 170 });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Path",
                HeaderText = "程序路径",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                MinimumWidth = 260
            });

            grid.CurrentCellDirtyStateChanged += delegate
            {
                if (grid.IsCurrentCellDirty)
                    grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            grid.CellValueChanged += delegate(object sender, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex >= 0 && e.ColumnIndex == grid.Columns["Pick"].Index)
                    UpdateSelectionSummary();
            };
            grid.CellPainting += PaintGlassCell;
            grid.Resize += delegate { UpdateGridBackdrop(); };
        }

        private void PaintGlassCell(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0) return;
            e.Handled = true;

            if (gridBackdrop != null)
            {
                Rectangle src = Rectangle.Intersect(e.CellBounds, new Rectangle(Point.Empty, gridBackdrop.Size));
                if (src.Width > 0 && src.Height > 0)
                    e.Graphics.DrawImage(gridBackdrop, src, src, GraphicsUnit.Pixel);
            }
            else
            {
                using (SolidBrush baseBrush = new SolidBrush(Color.FromArgb(26, 29, 36)))
                    e.Graphics.FillRectangle(baseBrush, e.CellBounds);
            }

            Color overlay = grid.Rows[e.RowIndex].Selected
                ? Color.FromArgb(205, 35, 55, 64)
                : Color.FromArgb(185, 15, 18, 24);
            using (SolidBrush b = new SolidBrush(overlay))
                e.Graphics.FillRectangle(b, e.CellBounds);

            e.Paint(e.CellBounds,
                DataGridViewPaintParts.Border |
                DataGridViewPaintParts.ContentForeground |
                DataGridViewPaintParts.ErrorIcon |
                DataGridViewPaintParts.Focus);
        }

        private void ScanProcesses()
        {
            UseWaitCursor = true;
            statusLabel.Text = "正在扫描后台进程…";
            Application.DoEvents();

            grid.Rows.Clear();
            DisposeIconCache();
            allItems.Clear();

            Process[] processes = Process.GetProcesses();
            foreach (Process process in processes)
            {
                try
                {
                    string name = process.ProcessName;
                    string path = TryGetPath(process);
                    long memory = 0;
                    try { memory = process.WorkingSet64; } catch { }

                    allItems.Add(new ProcessItem
                    {
                        Pid = process.Id,
                        Name = name,
                        MemoryBytes = memory,
                        Path = path,
                        ProtectionReason = GetProtectionReason(process.Id, name)
                    });
                }
                catch { }
                finally { process.Dispose(); }
            }

            allItems.Sort(delegate(ProcessItem a, ProcessItem b) { return b.MemoryBytes.CompareTo(a.MemoryBytes); });
            RenderRows();
            UpdatePhysicalMemoryLabel();
            statusLabel.Text = "已扫描 " + allItems.Count + " 个进程 · 按内存占用从高到低排列";
            UseWaitCursor = false;
        }

        private string GetProtectionReason(int pid, string processName)
        {
            if (pid == selfPid) return "本工具自身 · 永久保护";
            if (ChatNames.Contains(processName)) return "ChatGPT / OpenAI · 永久保护";
            if (CriticalNames.Contains(processName)) return "Windows 系统保护";
            if (protectBrowsers.Checked && BrowserNames.Contains(processName)) return "浏览器保护";
            return string.Empty;
        }

        private void RenderRows()
        {
            HashSet<int> selectedPids = new HashSet<int>();
            foreach (DataGridViewRow row in grid.Rows)
            {
                ProcessItem old = row.Tag as ProcessItem;
                if (old != null && Convert.ToBoolean(row.Cells["Pick"].Value ?? false))
                    selectedPids.Add(old.Pid);
            }

            string query = searchBox.Text.Trim();
            grid.Rows.Clear();

            foreach (ProcessItem item in allItems)
            {
                if (!showProtected.Checked && item.IsProtected) continue;
                if (!string.IsNullOrWhiteSpace(query))
                {
                    string hay = item.Name + " " + item.Pid + " " + item.Path;
                    if (hay.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
                }

                Bitmap icon = GetProcessIcon(item);
                int index = grid.Rows.Add(
                    selectedPids.Contains(item.Pid) && !item.IsProtected,
                    icon,
                    item.Name,
                    item.Pid,
                    FormatBytes(item.MemoryBytes),
                    item.IsProtected ? item.ProtectionReason : "可手动关闭",
                    string.IsNullOrWhiteSpace(item.Path) ? "（无权限读取）" : item.Path
                );

                DataGridViewRow row = grid.Rows[index];
                row.Tag = item;
                row.Cells["Pick"].ReadOnly = item.IsProtected;
                if (item.IsProtected)
                {
                    row.DefaultCellStyle.ForeColor = Color.FromArgb(148, 155, 167);
                    row.Cells["Status"].Style.ForeColor = Color.FromArgb(190, 199, 213);
                }
                else if (item.MemoryBytes >= 500L * 1024L * 1024L)
                {
                    row.Cells["Memory"].Style.ForeColor = Color.FromArgb(255, 208, 111);
                }
            }

            UpdateSelectionSummary();
            UpdateGridBackdrop();
        }

        private Bitmap GetProcessIcon(ProcessItem item)
        {
            string key = !string.IsNullOrWhiteSpace(item.Path) ? item.Path : "#" + item.Name;
            Bitmap cached;
            if (iconCache.TryGetValue(key, out cached)) return cached;

            Bitmap result = null;
            if (!string.IsNullOrWhiteSpace(item.Path) && File.Exists(item.Path))
            {
                try
                {
                    using (Icon extracted = Icon.ExtractAssociatedIcon(item.Path))
                    {
                        if (extracted != null)
                            result = MakeRoundedIcon(extracted.ToBitmap(), 34, 9);
                    }
                }
                catch { }
            }

            if (result == null)
                result = CreateFallbackProcessIcon(item.Name, 34);

            iconCache[key] = result;
            return result;
        }

        private static Bitmap MakeRoundedIcon(Bitmap source, int size, int radius)
        {
            Bitmap output = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(output))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                using (GraphicsPath path = RoundedRect(new Rectangle(0, 0, size - 1, size - 1), radius))
                {
                    g.SetClip(path);
                    g.DrawImage(source, new Rectangle(0, 0, size, size));
                    g.ResetClip();
                    using (Pen pen = new Pen(Color.FromArgb(85, 255, 255, 255), 1f))
                        g.DrawPath(pen, path);
                }
            }
            source.Dispose();
            return output;
        }

        private static Bitmap CreateFallbackProcessIcon(string name, int size)
        {
            Bitmap output = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(output))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath path = RoundedRect(new Rectangle(0, 0, size - 1, size - 1), 9))
                using (SolidBrush bg = new SolidBrush(Color.FromArgb(50, 60, 72)))
                    g.FillPath(bg, path);

                string letter = string.IsNullOrWhiteSpace(name) ? "?" : name.Substring(0, 1).ToUpperInvariant();
                using (Font f = new Font("Segoe UI", 12F, FontStyle.Bold))
                using (SolidBrush fg = new SolidBrush(Color.FromArgb(210, 240, 247)))
                {
                    SizeF s = g.MeasureString(letter, f);
                    g.DrawString(letter, f, fg, (size - s.Width) / 2f, (size - s.Height) / 2f - 1f);
                }
            }
            return output;
        }

        private void ClearSelections()
        {
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (!row.Cells["Pick"].ReadOnly)
                    row.Cells["Pick"].Value = false;
            }
            UpdateSelectionSummary();
        }

        private void UpdateSelectionSummary()
        {
            int count = 0;
            long bytes = 0;
            foreach (DataGridViewRow row in grid.Rows)
            {
                bool picked = Convert.ToBoolean(row.Cells["Pick"].Value ?? false);
                ProcessItem item = row.Tag as ProcessItem;
                if (!picked || item == null || item.IsProtected) continue;
                count++;
                bytes += item.MemoryBytes;
            }
            selectionLabel.Text = "已选 " + count + " 个 · 约 " + FormatBytes(bytes);
            killButton.Enabled = count > 0;
        }

        private void KillSelected()
        {
            List<ProcessItem> selected = new List<ProcessItem>();
            foreach (DataGridViewRow row in grid.Rows)
            {
                bool picked = Convert.ToBoolean(row.Cells["Pick"].Value ?? false);
                ProcessItem item = row.Tag as ProcessItem;
                if (picked && item != null && !item.IsProtected)
                    selected.Add(item);
            }

            if (selected.Count == 0) return;
            long estimate = selected.Sum(delegate(ProcessItem p) { return p.MemoryBytes; });
            string preview = string.Join("、", selected.Take(8).Select(delegate(ProcessItem p) { return p.Name + " (" + p.Pid + ")"; }).ToArray());
            if (selected.Count > 8) preview += " 等 " + selected.Count + " 个";

            DialogResult confirm = MessageBox.Show(
                "即将强制关闭：\n\n" + preview + "\n\n当前内存合计约 " + FormatBytes(estimate) +
                "。\n\n未保存的数据可能丢失。确定继续吗？",
                "确认强制关闭",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (confirm != DialogResult.Yes) return;

            int success = 0;
            List<string> failed = new List<string>();
            foreach (ProcessItem item in selected)
            {
                if (!string.IsNullOrEmpty(GetProtectionReason(item.Pid, item.Name)))
                {
                    failed.Add(item.Name + "（已进入保护名单）");
                    continue;
                }

                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo();
                    psi.FileName = "taskkill.exe";
                    psi.Arguments = "/PID " + item.Pid + " /T /F";
                    psi.UseShellExecute = false;
                    psi.CreateNoWindow = true;
                    psi.RedirectStandardOutput = true;
                    psi.RedirectStandardError = true;
                    using (Process killer = Process.Start(psi))
                    {
                        if (killer == null)
                        {
                            failed.Add(item.Name);
                            continue;
                        }
                        if (!killer.WaitForExit(5000))
                        {
                            try { killer.Kill(); } catch { }
                            failed.Add(item.Name);
                        }
                        else if (killer.ExitCode == 0)
                        {
                            success++;
                        }
                        else
                        {
                            failed.Add(item.Name);
                        }
                    }
                }
                catch { failed.Add(item.Name); }
            }

            ScanProcesses();
            string message = "已成功处理 " + success + " 个进程。";
            if (failed.Count > 0)
                message += "\n\n未能关闭：" + string.Join("、", failed.Take(10).ToArray());
            MessageBox.Show(message, "处理完成", MessageBoxButtons.OK, failed.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }

        private static string TryGetPath(Process process)
        {
            try
            {
                if (process.MainModule != null) return process.MainModule.FileName;
            }
            catch { }
            return string.Empty;
        }

        private void UpdatePhysicalMemoryLabel()
        {
            MEMORYSTATUSEX status = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(status))
            {
                ulong used = status.ullTotalPhys - status.ullAvailPhys;
                memoryLabel.Text = "内存 " + FormatBytes((long)used) + " / " + FormatBytes((long)status.ullTotalPhys) + " · " + status.dwMemoryLoad + "%";
            }
            else memoryLabel.Text = "内存状态读取失败";
        }

        private void ChooseBackground()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "选择界面背景图片";
                dialog.Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp;*.gif|所有文件|*.*";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                backgroundPath = dialog.FileName;
                SaveBackgroundSetting();
                UpdateBackgroundLabel();
                RebuildBackground();
            }
        }

        private void ClearBackground()
        {
            backgroundPath = string.Empty;
            try { if (File.Exists(BackgroundSettingFile)) File.Delete(BackgroundSettingFile); } catch { }
            UpdateBackgroundLabel();
            RebuildBackground();
        }

        private void TryRestoreBackgroundSetting()
        {
            try
            {
                if (File.Exists(BackgroundSettingFile))
                {
                    string path = File.ReadAllText(BackgroundSettingFile).Trim();
                    if (File.Exists(path)) backgroundPath = path;
                }
            }
            catch { }
        }

        private void SaveBackgroundSetting()
        {
            try
            {
                Directory.CreateDirectory(SettingsFolder);
                File.WriteAllText(BackgroundSettingFile, backgroundPath);
            }
            catch { }
        }

        private void UpdateBackgroundLabel()
        {
            backgroundLabel.Text = string.IsNullOrWhiteSpace(backgroundPath)
                ? "背景：默认渐变"
                : "背景：" + Path.GetFileName(backgroundPath);
        }

        private void RebuildBackground()
        {
            if (ClientSize.Width < 2 || ClientSize.Height < 2) return;
            if (renderedBackground != null) { renderedBackground.Dispose(); renderedBackground = null; }
            if (blurredBackground != null) { blurredBackground.Dispose(); blurredBackground = null; }

            try
            {
                if (!string.IsNullOrWhiteSpace(backgroundPath) && File.Exists(backgroundPath))
                {
                    using (FileStream stream = new FileStream(backgroundPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (Image image = Image.FromStream(stream))
                        renderedBackground = RenderCover(image, ClientSize);
                }
            }
            catch { renderedBackground = null; }

            if (renderedBackground == null)
                renderedBackground = CreateDefaultBackground(ClientSize);

            blurredBackground = CreateSoftBlur(renderedBackground, 14);
            shell.BlurredBackdrop = blurredBackground;
            Invalidate(true);
            shell.Invalidate();
            UpdateGridBackdrop();
        }

        private void UpdateGridBackdrop()
        {
            if (!IsHandleCreated || grid.Width < 2 || grid.Height < 2 || blurredBackground == null) return;
            if (gridBackdrop != null) { gridBackdrop.Dispose(); gridBackdrop = null; }

            try
            {
                Point topLeft = PointToClient(grid.PointToScreen(Point.Empty));
                Rectangle src = new Rectangle(topLeft.X, topLeft.Y, grid.Width, grid.Height);
                Rectangle imageRect = new Rectangle(Point.Empty, blurredBackground.Size);
                Rectangle clipped = Rectangle.Intersect(src, imageRect);
                if (clipped.Width <= 0 || clipped.Height <= 0) return;

                gridBackdrop = new Bitmap(grid.Width, grid.Height, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(gridBackdrop))
                {
                    g.Clear(Color.FromArgb(18, 21, 27));
                    Rectangle dest = new Rectangle(clipped.X - src.X, clipped.Y - src.Y, clipped.Width, clipped.Height);
                    g.DrawImage(blurredBackground, dest, clipped, GraphicsUnit.Pixel);
                    using (SolidBrush tint = new SolidBrush(Color.FromArgb(80, 8, 11, 16)))
                        g.FillRectangle(tint, new Rectangle(Point.Empty, gridBackdrop.Size));
                }
                grid.Invalidate();
            }
            catch { }
        }

        private static Bitmap RenderCover(Image source, Size target)
        {
            Bitmap output = new Bitmap(Math.Max(1, target.Width), Math.Max(1, target.Height), PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(output))
            {
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                float scale = Math.Max((float)target.Width / source.Width, (float)target.Height / source.Height);
                int w = (int)Math.Ceiling(source.Width * scale);
                int h = (int)Math.Ceiling(source.Height * scale);
                int x = (target.Width - w) / 2;
                int y = (target.Height - h) / 2;
                g.DrawImage(source, new Rectangle(x, y, w, h));
                using (SolidBrush shade = new SolidBrush(Color.FromArgb(65, 0, 0, 0)))
                    g.FillRectangle(shade, new Rectangle(Point.Empty, target));
            }
            return output;
        }

        private static Bitmap CreateDefaultBackground(Size target)
        {
            Bitmap output = new Bitmap(Math.Max(1, target.Width), Math.Max(1, target.Height), PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(output))
            {
                Rectangle r = new Rectangle(Point.Empty, target);
                using (LinearGradientBrush gradient = new LinearGradientBrush(r,
                    Color.FromArgb(8, 11, 16), Color.FromArgb(16, 29, 37), 32f))
                    g.FillRectangle(gradient, r);
                using (SolidBrush glow = new SolidBrush(Color.FromArgb(38, 90, 220, 235)))
                    g.FillEllipse(glow, -target.Width / 5, -target.Height / 4, target.Width / 2, target.Height / 2);
                using (SolidBrush glow2 = new SolidBrush(Color.FromArgb(28, 180, 90, 210)))
                    g.FillEllipse(glow2, target.Width * 2 / 3, target.Height / 2, target.Width / 2, target.Height / 2);
            }
            return output;
        }

        private static Bitmap CreateSoftBlur(Bitmap source, int factor)
        {
            int smallW = Math.Max(2, source.Width / factor);
            int smallH = Math.Max(2, source.Height / factor);
            using (Bitmap small = new Bitmap(smallW, smallH, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(small))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    g.DrawImage(source, new Rectangle(0, 0, smallW, smallH));
                }
                Bitmap blurred = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(blurred))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(small, new Rectangle(0, 0, blurred.Width, blurred.Height));
                }
                return blurred;
            }
        }

        private void ApplyWindowIcon()
        {
            Bitmap bmp = CreateLogoBitmap(64);
            IntPtr hIcon = bmp.GetHicon();
            try
            {
                using (Icon temp = Icon.FromHandle(hIcon))
                    Icon = (Icon)temp.Clone();
            }
            finally
            {
                DestroyIcon(hIcon);
                bmp.Dispose();
            }
        }

        private static Bitmap CreateLogoBitmap(int size)
        {
            Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath tile = RoundedRect(new Rectangle(1, 1, size - 2, size - 2), Math.Max(8, size / 4)))
                using (LinearGradientBrush fill = new LinearGradientBrush(new Rectangle(0, 0, size, size),
                    Color.FromArgb(116, 239, 255), Color.FromArgb(112, 156, 255), 45f))
                    g.FillPath(fill, tile);
                using (Pen p = new Pen(Color.FromArgb(20, 31, 39), Math.Max(2f, size / 12f)))
                {
                    p.StartCap = LineCap.Round;
                    p.EndCap = LineCap.Round;
                    float x1 = size * 0.27f;
                    float x2 = size * 0.73f;
                    g.DrawLine(p, x1, size * 0.36f, x2, size * 0.36f);
                    g.DrawLine(p, x1, size * 0.52f, size * 0.62f, size * 0.52f);
                    g.DrawLine(p, x1, size * 0.68f, size * 0.52f, size * 0.68f);
                }
            }
            return bmp;
        }

        private static void ConfigureButton(RoundButton button, string text, Color back, Color fore, int width)
        {
            button.Text = text;
            button.Width = width;
            button.Height = 36;
            button.CornerRadius = 11;
            button.BackColor = back;
            button.ForeColor = fore;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            button.Margin = new Padding(0, 0, 8, 0);
            button.UseVisualStyleBackColor = false;
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 0) bytes = 0;
            double value = bytes;
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            int index = 0;
            while (value >= 1024d && index < units.Length - 1)
            {
                value /= 1024d;
                index++;
            }
            return index <= 1 ? value.ToString("0") + " " + units[index] : value.ToString("0.0") + " " + units[index];
        }

        private void DisposeIconCache()
        {
            foreach (Bitmap bmp in iconCache.Values) bmp.Dispose();
            iconCache.Clear();
        }

        private void DisposeVisualCache()
        {
            DisposeIconCache();
            if (renderedBackground != null) renderedBackground.Dispose();
            if (blurredBackground != null) blurredBackground.Dispose();
            if (gridBackdrop != null) gridBackdrop.Dispose();
        }

        internal static GraphicsPath RoundedRect(Rectangle rect, int radius)
        {
            int d = Math.Max(2, radius * 2);
            GraphicsPath path = new GraphicsPath();
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static void SetCueBanner(TextBox box, string text)
        {
            SendMessage(box.Handle, 0x1501, (IntPtr)1, text);
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr handle);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private sealed class MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;

            public MEMORYSTATUSEX()
            {
                dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            }
        }

        private sealed class ProcessItem
        {
            public int Pid;
            public string Name = string.Empty;
            public long MemoryBytes;
            public string Path = string.Empty;
            public string ProtectionReason = string.Empty;
            public bool IsProtected { get { return !string.IsNullOrEmpty(ProtectionReason); } }
        }
    }

    internal sealed class GlassPanel : Panel
    {
        public Bitmap BlurredBackdrop { get; set; }
        public int CornerRadius { get; set; }
        public Color TintColor { get; set; }

        public GlassPanel()
        {
            DoubleBuffered = true;
            BackColor = Color.Transparent;
            CornerRadius = 20;
            TintColor = Color.FromArgb(160, 10, 13, 18);
            Resize += delegate { UpdateRegion(); };
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Form form = FindForm();
            if (BlurredBackdrop != null && form != null)
            {
                try
                {
                    Point p = form.PointToClient(PointToScreen(Point.Empty));
                    Rectangle src = new Rectangle(p.X, p.Y, Width, Height);
                    Rectangle valid = Rectangle.Intersect(src, new Rectangle(Point.Empty, BlurredBackdrop.Size));
                    if (valid.Width > 0 && valid.Height > 0)
                    {
                        Rectangle dest = new Rectangle(valid.X - src.X, valid.Y - src.Y, valid.Width, valid.Height);
                        e.Graphics.DrawImage(BlurredBackdrop, dest, valid, GraphicsUnit.Pixel);
                    }
                }
                catch { }
            }
            else e.Graphics.Clear(Color.FromArgb(18, 21, 27));

            using (SolidBrush tint = new SolidBrush(TintColor))
                e.Graphics.FillRectangle(tint, ClientRectangle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (GraphicsPath path = MainForm.RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), CornerRadius))
            using (Pen pen = new Pen(Color.FromArgb(70, 255, 255, 255), 1f))
                e.Graphics.DrawPath(pen, path);
        }

        private void UpdateRegion()
        {
            if (Width <= 0 || Height <= 0) return;
            using (GraphicsPath path = MainForm.RoundedRect(new Rectangle(0, 0, Width, Height), CornerRadius))
            {
                Region old = Region;
                Region = new Region(path);
                if (old != null) old.Dispose();
            }
        }
    }

    internal sealed class RoundButton : Button
    {
        public int CornerRadius { get; set; }

        public RoundButton()
        {
            CornerRadius = 10;
            Resize += delegate { UpdateRegion(); };
        }

        private void UpdateRegion()
        {
            if (Width <= 0 || Height <= 0) return;
            using (GraphicsPath path = MainForm.RoundedRect(new Rectangle(0, 0, Width, Height), CornerRadius))
            {
                Region old = Region;
                Region = new Region(path);
                if (old != null) old.Dispose();
            }
        }
    }

    internal sealed class RoundedLabel : Label
    {
        public int CornerRadius { get; set; }

        public RoundedLabel()
        {
            CornerRadius = 10;
            Resize += delegate { UpdateRegion(); };
        }

        private void UpdateRegion()
        {
            if (Width <= 0 || Height <= 0) return;
            using (GraphicsPath path = MainForm.RoundedRect(new Rectangle(0, 0, Width, Height), CornerRadius))
            {
                Region old = Region;
                Region = new Region(path);
                if (old != null) old.Dispose();
            }
        }
    }
}
