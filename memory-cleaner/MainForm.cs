using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MemoryCleaner;

public sealed class MainForm : Form
{
    private readonly DataGridView grid = new();
    private readonly TextBox searchBox = new();
    private readonly CheckBox protectBrowsers = new();
    private readonly CheckBox showProtected = new();
    private readonly Label memoryLabel = new();
    private readonly Label selectionLabel = new();
    private readonly Label statusLabel = new();
    private readonly Button refreshButton = new();
    private readonly Button killButton = new();
    private readonly Button clearSelectionButton = new();

    private readonly List<ProcessItem> allItems = new();
    private readonly int selfPid = Environment.ProcessId;

    private static readonly HashSet<string> CriticalNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "system", "idle", "registry", "secure system", "memory compression",
        "smss", "csrss", "wininit", "winlogon", "services", "lsass", "svchost",
        "fontdrvhost", "dwm", "sihost", "taskhostw", "explorer",
        "startmenuexperiencehost", "shellexperiencehost", "searchhost",
        "securityhealthservice", "msmpeng", "audiodg", "ctfmon"
    };

    private static readonly HashSet<string> BrowserNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "firefox", "brave", "opera", "opera_gx",
        "vivaldi", "arc", "msedgewebview2"
    };

    private static readonly HashSet<string> ChatNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "chatgpt", "openai"
    };

    private static readonly Color Bg = Color.FromArgb(12, 14, 18);
    private static readonly Color Panel = Color.FromArgb(20, 23, 29);
    private static readonly Color Panel2 = Color.FromArgb(27, 31, 39);
    private static readonly Color TextMain = Color.FromArgb(239, 242, 247);
    private static readonly Color TextMuted = Color.FromArgb(148, 156, 169);
    private static readonly Color Accent = Color.FromArgb(117, 235, 255);
    private static readonly Color Danger = Color.FromArgb(255, 104, 119);
    private static readonly Color Border = Color.FromArgb(49, 55, 66);

    public MainForm()
    {
        Text = "后台进程整理器 · V0.1";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 600);
        Size = new Size(1160, 760);
        BackColor = Bg;
        ForeColor = TextMain;
        Font = new Font("Segoe UI", 9F);

        BuildUi();
        Shown += (_, _) => ScanProcesses();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Bg,
            Padding = new Padding(22),
            ColumnCount = 1,
            RowCount = 5,
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        var header = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var titleStack = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(0)
        };
        var title = new Label
        {
            Text = "后台进程整理器",
            AutoSize = true,
            ForeColor = TextMain,
            Font = new Font("Segoe UI", 22F, FontStyle.Bold),
            Margin = new Padding(0)
        };
        var subtitle = new Label
        {
            Text = "扫描正在运行的进程 · 只关闭你亲自勾选的项目 · 系统核心进程永久保护",
            AutoSize = true,
            ForeColor = TextMuted,
            Font = new Font("Segoe UI", 10F),
            Margin = new Padding(1, 6, 0, 0)
        };
        titleStack.Controls.Add(title);
        titleStack.Controls.Add(subtitle);
        header.Controls.Add(titleStack, 0, 0);

        memoryLabel.Text = "内存读取中…";
        memoryLabel.AutoSize = true;
        memoryLabel.ForeColor = Accent;
        memoryLabel.Font = new Font("Consolas", 11F, FontStyle.Bold);
        memoryLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        memoryLabel.Margin = new Padding(18, 8, 0, 0);
        header.Controls.Add(memoryLabel, 1, 0);
        root.Controls.Add(header, 0, 0);

        var toolbar = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 6,
            Margin = new Padding(0, 18, 0, 12)
        };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        searchBox.Dock = DockStyle.Fill;
        searchBox.PlaceholderText = "搜索进程名 / PID / 路径";
        searchBox.BackColor = Panel;
        searchBox.ForeColor = TextMain;
        searchBox.BorderStyle = BorderStyle.FixedSingle;
        searchBox.Font = new Font("Segoe UI", 10F);
        searchBox.Margin = new Padding(0, 2, 14, 2);
        searchBox.TextChanged += (_, _) => RenderRows();
        toolbar.Controls.Add(searchBox, 0, 0);

        protectBrowsers.Text = "保护浏览器 / ChatGPT";
        protectBrowsers.Checked = true;
        protectBrowsers.AutoSize = true;
        protectBrowsers.ForeColor = TextMain;
        protectBrowsers.Margin = new Padding(0, 8, 16, 0);
        protectBrowsers.CheckedChanged += (_, _) => ScanProcesses();
        toolbar.Controls.Add(protectBrowsers, 1, 0);

        showProtected.Text = "显示受保护进程";
        showProtected.Checked = true;
        showProtected.AutoSize = true;
        showProtected.ForeColor = TextMain;
        showProtected.Margin = new Padding(0, 8, 16, 0);
        showProtected.CheckedChanged += (_, _) => RenderRows();
        toolbar.Controls.Add(showProtected, 2, 0);

        ConfigureButton(refreshButton, "刷新扫描", Accent, Color.FromArgb(9, 22, 25));
        refreshButton.Click += (_, _) => ScanProcesses();
        toolbar.Controls.Add(refreshButton, 3, 0);

        ConfigureButton(clearSelectionButton, "取消勾选", Panel2, TextMain);
        clearSelectionButton.Click += (_, _) => ClearSelections();
        toolbar.Controls.Add(clearSelectionButton, 4, 0);

        ConfigureButton(killButton, "强制关闭选中", Danger, Color.White);
        killButton.Click += (_, _) => KillSelected();
        toolbar.Controls.Add(killButton, 5, 0);
        root.Controls.Add(toolbar, 0, 1);

        ConfigureGrid();
        root.Controls.Add(grid, 0, 2);

        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            Margin = new Padding(0, 12, 0, 0)
        };
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

        var warning = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            MaximumSize = new Size(1100, 0),
            Text = "⚠ 强制结束进程可能导致未保存数据丢失。浏览器默认受保护，是为了避免把当前 ChatGPT 会话一起关掉；如果你主动取消保护，请先保存网页工作。",
            ForeColor = Color.FromArgb(224, 190, 119),
            BackColor = Color.FromArgb(35, 29, 20),
            Padding = new Padding(12, 10, 12, 10),
            Margin = new Padding(0, 12, 0, 0)
        };
        root.Controls.Add(warning, 0, 4);
    }

    private void ConfigureGrid()
    {
        grid.Dock = DockStyle.Fill;
        grid.BackgroundColor = Panel;
        grid.BorderStyle = BorderStyle.None;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.GridColor = Border;
        grid.RowHeadersVisible = false;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;
        grid.MultiSelect = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
        grid.RowTemplate.Height = 34;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersHeight = 38;
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(31, 35, 43);
        grid.ColumnHeadersDefaultCellStyle.ForeColor = TextMain;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(31, 35, 43);
        grid.DefaultCellStyle.BackColor = Panel;
        grid.DefaultCellStyle.ForeColor = TextMain;
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(42, 57, 65);
        grid.DefaultCellStyle.SelectionForeColor = Color.White;
        grid.DefaultCellStyle.Padding = new Padding(4, 0, 4, 0);

        grid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "Pick",
            HeaderText = "选",
            Width = 44,
            FalseValue = false,
            TrueValue = true
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "进程", Width = 190 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Pid", HeaderText = "PID", Width = 80 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Memory", HeaderText = "内存", Width = 105 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "状态", Width = 180 });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Path",
            HeaderText = "路径",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            MinimumWidth = 260
        });

        grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (grid.IsCurrentCellDirty)
                grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        grid.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex == grid.Columns["Pick"].Index)
                UpdateSelectionSummary();
        };
    }

    private void ScanProcesses()
    {
        UseWaitCursor = true;
        statusLabel.Text = "正在扫描后台进程…";
        Application.DoEvents();

        allItems.Clear();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                string name = process.ProcessName;
                string path = TryGetPath(process);
                long memory = 0;
                try { memory = process.WorkingSet64; } catch { }

                string reason = GetProtectionReason(process.Id, name);
                allItems.Add(new ProcessItem
                {
                    Pid = process.Id,
                    Name = name,
                    MemoryBytes = memory,
                    Path = path,
                    ProtectionReason = reason
                });
            }
            catch
            {
                // A process can disappear while scanning; just skip it.
            }
            finally
            {
                process.Dispose();
            }
        }

        allItems.Sort((a, b) => b.MemoryBytes.CompareTo(a.MemoryBytes));
        RenderRows();
        UpdatePhysicalMemoryLabel();
        statusLabel.Text = $"已扫描 {allItems.Count} 个进程 · 按内存占用从高到低排列";
        UseWaitCursor = false;
    }

    private string GetProtectionReason(int pid, string processName)
    {
        if (pid == selfPid) return "🔒 本工具自身";
        if (ChatNames.Contains(processName)) return "🔒 ChatGPT / OpenAI";
        if (CriticalNames.Contains(processName)) return "🔒 Windows 系统保护";
        if (protectBrowsers.Checked && BrowserNames.Contains(processName)) return "🔒 浏览器保护";
        return string.Empty;
    }

    private void RenderRows()
    {
        var selectedPids = new HashSet<int>();
        foreach (DataGridViewRow row in grid.Rows)
        {
            if (row.Tag is ProcessItem old && Convert.ToBoolean(row.Cells["Pick"].Value ?? false))
                selectedPids.Add(old.Pid);
        }

        string query = searchBox.Text.Trim();
        grid.Rows.Clear();

        foreach (var item in allItems)
        {
            if (!showProtected.Checked && item.IsProtected) continue;
            if (!string.IsNullOrWhiteSpace(query))
            {
                string hay = $"{item.Name} {item.Pid} {item.Path}";
                if (!hay.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
            }

            int index = grid.Rows.Add(
                selectedPids.Contains(item.Pid) && !item.IsProtected,
                item.Name,
                item.Pid,
                FormatBytes(item.MemoryBytes),
                item.IsProtected ? item.ProtectionReason : "可手动关闭",
                string.IsNullOrWhiteSpace(item.Path) ? "（无权限读取）" : item.Path
            );
            var row = grid.Rows[index];
            row.Tag = item;
            row.Cells["Pick"].ReadOnly = item.IsProtected;

            if (item.IsProtected)
            {
                row.DefaultCellStyle.ForeColor = Color.FromArgb(132, 139, 151);
                row.Cells["Status"].Style.ForeColor = Color.FromArgb(185, 193, 207);
            }
            else if (item.MemoryBytes >= 500L * 1024 * 1024)
            {
                row.Cells["Memory"].Style.ForeColor = Color.FromArgb(255, 205, 104);
            }
        }
        UpdateSelectionSummary();
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
            if (!picked || row.Tag is not ProcessItem item || item.IsProtected) continue;
            count++;
            bytes += item.MemoryBytes;
        }
        selectionLabel.Text = $"已选 {count} 个 · 约 {FormatBytes(bytes)}";
        killButton.Enabled = count > 0;
    }

    private void KillSelected()
    {
        var items = new List<ProcessItem>();
        foreach (DataGridViewRow row in grid.Rows)
        {
            bool picked = Convert.ToBoolean(row.Cells["Pick"].Value ?? false);
            if (picked && row.Tag is ProcessItem item && !item.IsProtected)
                items.Add(item);
        }
        if (items.Count == 0) return;

        long estimate = items.Sum(x => x.MemoryBytes);
        string names = string.Join("\n", items.Take(12).Select(x => $"• {x.Name}  (PID {x.Pid}, {FormatBytes(x.MemoryBytes)})"));
        if (items.Count > 12) names += $"\n• …以及另外 {items.Count - 12} 个进程";

        var result = MessageBox.Show(
            $"确定强制关闭下面这些进程吗？\n\n{names}\n\n当前内存占用合计约 {FormatBytes(estimate)}。\n未保存的数据可能丢失。",
            "确认强制关闭",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (result != DialogResult.Yes) return;

        int success = 0;
        var failures = new List<string>();
        foreach (var item in items)
        {
            try
            {
                using var process = Process.GetProcessById(item.Pid);
                string currentName = process.ProcessName;
                string currentProtection = GetProtectionReason(process.Id, currentName);
                if (!string.IsNullOrEmpty(currentProtection))
                {
                    failures.Add($"{currentName}: 已被保护，跳过");
                    continue;
                }

                process.Kill(entireProcessTree: true);
                process.WaitForExit(2500);
                success++;
            }
            catch (ArgumentException)
            {
                // It already exited between scan and click; count as completed.
                success++;
            }
            catch (Exception ex)
            {
                failures.Add($"{item.Name}: {ex.Message}");
            }
        }

        string summary = $"已处理 {success} 个进程。";
        if (failures.Count > 0)
            summary += $"\n\n{failures.Count} 个未能关闭：\n" + string.Join("\n", failures.Take(8));

        MessageBox.Show(summary, "完成", MessageBoxButtons.OK,
            failures.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        ScanProcesses();
    }

    private void UpdatePhysicalMemoryLabel()
    {
        var status = new MEMORYSTATUSEX();
        if (GlobalMemoryStatusEx(status))
        {
            ulong used = status.ullTotalPhys - status.ullAvailPhys;
            memoryLabel.Text = $"RAM  {FormatBytes((long)used)} / {FormatBytes((long)status.ullTotalPhys)}  ·  {status.dwMemoryLoad}%";
        }
        else
        {
            memoryLabel.Text = "RAM 状态读取失败";
        }
    }

    private static string TryGetPath(Process process)
    {
        try { return process.MainModule?.FileName ?? string.Empty; }
        catch { return string.Empty; }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 0) return "0 MB";
        double value = bytes;
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit <= 1 ? $"{value:0} {units[unit]}" : $"{value:0.0} {units[unit]}";
    }

    private static void ConfigureButton(Button button, string text, Color background, Color foreground)
    {
        button.Text = text;
        button.AutoSize = true;
        button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = background;
        button.ForeColor = foreground;
        button.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        button.Padding = new Padding(12, 7, 12, 7);
        button.Margin = new Padding(6, 0, 0, 0);
        button.Cursor = Cursors.Hand;
    }

    private sealed class ProcessItem
    {
        public int Pid { get; init; }
        public string Name { get; init; } = string.Empty;
        public long MemoryBytes { get; init; }
        public string Path { get; init; } = string.Empty;
        public string ProtectionReason { get; init; } = string.Empty;
        public bool IsProtected => !string.IsNullOrEmpty(ProtectionReason);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MEMORYSTATUSEX
    {
        public uint dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>();
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);
}
